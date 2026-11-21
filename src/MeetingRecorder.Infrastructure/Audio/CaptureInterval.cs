using System.Threading.Channels;

using MeetingRecorder.Application.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Files;
using MeetingRecorder.Infrastructure.Wasapi;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Owns the capture thread, bounded handoff and mixing worker for a single
/// interval. Starting another interval always allocates new queues and filter state.</summary>
internal sealed class CaptureInterval
{
    private readonly Channel<Message> _queue;
    private readonly TaskCompletionSource<long> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IRecordingClock _clock;
    private readonly Action<string> _interrupt;
    private readonly Task _producer;
    private readonly Task _consumer;
    private long _boundary = long.MaxValue;

    private readonly record struct Message(AudioPacket? Packet, long Watermark);

    internal CaptureInterval(RecordingSettings settings, IRecordingClock clock,
        Func<string, bool, IInputEndpoint> open, WaveDestination file,
        Action<Levels> meter, Action<string> interrupt, int capacity = 512)
    {
        _clock = clock;
        _interrupt = interrupt;
        _queue = Channel.CreateBounded<Message>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        _consumer = Task.Run(() => MixAsync(file, meter));
        _producer = Task.Factory.StartNew(() => Capture(settings, open),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    internal Task<long> Started => _ready.Task;

    internal async Task StopAsync(long boundary)
    {
        _ = Interlocked.CompareExchange(ref _boundary, boundary, long.MaxValue);
        await Task.WhenAll(_producer, _consumer).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "The producer must settle startup and notify the owner on every thread exit.")]
    private void Capture(RecordingSettings settings, Func<string, bool, IInputEndpoint> open)
    {
        try
        {
            using var mic = open(settings.MicrophoneId, false);
            using var output = open(settings.OutputId, true);
            mic.Start();
            output.Start();
            var start = _clock.Now;
            _ready.SetResult(start);
            var watermark = start;
            while (Volatile.Read(ref _boundary) == long.MaxValue)
            {
                var now = _clock.Now;
                mic.Drain(Enqueue);
                output.Drain(Enqueue);
                if (now - watermark >= TimeSpan.TicksPerMillisecond * 25)
                {
                    // Leave room for engine buffering and ordinary scheduler jitter.
                    Send(new(null, now - (TimeSpan.TicksPerMillisecond * 150)));
                    watermark = now;
                }

                Thread.Sleep(5);
            }

            // Allow the engine to deliver a packet that straddles the command boundary.
            var period = mic.EnginePeriod > output.EnginePeriod ? mic.EnginePeriod : output.EnginePeriod;
            Thread.Sleep(period + TimeSpan.FromMilliseconds(5));
            mic.Drain(Enqueue);
            output.Drain(Enqueue);
        }
        catch (Exception ex)
        {
            var failure = ex as RecordingException ?? new RecordingException(
                "The audio devices stopped responding. Check the connections and refresh devices.", ex);
            if (!_ready.TrySetException(failure))
            {
                Fail(failure.Message);
            }
        }
        finally
        {
            _ = _queue.Writer.TryComplete();
        }
    }

    private void Enqueue(AudioPacket packet)
    {
        if (packet.Qpc < 0 || packet.Qpc > _clock.Now + (TimeSpan.TicksPerMillisecond * 100))
        {
            throw new RecordingException("The device returned an invalid audio timestamp.");
        }

        Send(new(packet, 0));
    }

    private void Send(Message message)
    {
        if (!_queue.Writer.TryWrite(message))
        {
            throw new RecordingException("The capture queue filled up. The recording is incomplete.");
        }
    }

    private void Fail(string message)
    {
        _ = Interlocked.CompareExchange(ref _boundary, _clock.Now, long.MaxValue);
        _interrupt(message);
    }

    private async Task MixAsync(WaveDestination file, Action<Levels> meter)
    {
        IntervalMixer? mixer = null;
        try
        {
            var start = await Started.ConfigureAwait(false);
            mixer = new IntervalMixer(start, file, meter);
            await foreach (var message in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (message.Packet is { } packet)
                {
                    mixer.Add(packet);
                }
                else
                {
                    mixer.Drain(Math.Min(message.Watermark, Volatile.Read(ref _boundary)));
                }
            }

            mixer.Drain(Volatile.Read(ref _boundary));
        }
        catch (RecordingException ex) when (mixer is not null)
        {
            Fail(ex.Message);
            try
            {
                mixer.Drain(Volatile.Read(ref _boundary));
            }
            catch (RecordingException)
            {
                // File failure: the writer will finalize its last successful block.
            }
        }
        catch (RecordingException)
        {
            // Startup failure is delivered to BeginIntervalAsync through Started.
        }
    }
}
