using System.Threading.Channels;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Owns the capture thread, bounded handoff and mixing worker for a single
/// interval. Starting another interval always allocates new queues and filter state.</summary>
internal sealed class CaptureInterval
{
    private const long OPEN_BOUNDARY = -1;
    private const int POLL_MILLISECONDS = 5;
    private const long WATERMARK_PERIOD = 25 * TimeSpan.TicksPerMillisecond;
    private const long REORDER_DELAY = 150 * TimeSpan.TicksPerMillisecond;
    private const long FUTURE_TOLERANCE = 100 * TimeSpan.TicksPerMillisecond;

    private readonly Channel<Message> _queue;
    private readonly TaskCompletionSource<CaptureTimestamp> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IRecordingClock _clock;
    private readonly Action<RecordingProblem> _interrupt;
    private readonly TaskCompletionSource _producer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _consumer;
    private long _boundary = OPEN_BOUNDARY;

    /// <summary>Starts the native capture thread and asynchronous mixer for a single interval.</summary>
    internal CaptureInterval(RecordingSettings settings, IRecordingClock clock,
        Func<EndpointId, bool, IInputEndpoint> open, IRecordingDestination file,
        Action<Levels> meter, Action<RecordingProblem> interrupt, int capacity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(interrupt);
        _clock = clock;
        _interrupt = interrupt;
        _queue = Channel.CreateBounded<Message>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        _consumer = MixAsync(file, meter, cancellationToken);
        var thread = new Thread(() => Capture(settings, open, cancellationToken))
        {
            IsBackground = true,
            Name = "SoundSeal WASAPI capture"
        };
        thread.Start();
    }

    /// <summary>Completes with the shared timestamp after the selected native clients have started.</summary>
    internal Task<CaptureTimestamp> Started => _ready.Task;

    /// <summary>Commits the exclusive cutoff and waits for both workers to drain.</summary>
    internal async Task StopAsync(CaptureTimestamp boundary, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Interlocked.CompareExchange(ref _boundary, boundary.Ticks, OPEN_BOUNDARY);
        await Task.WhenAll(_producer.Task, _consumer).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "The producer must settle startup and notify the owner on every thread exit.")]
    private void Capture(RecordingSettings settings, Func<EndpointId, bool, IInputEndpoint> open, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var mic = settings.MicrophoneId is { } micId ? open(micId, false) : null;
            using var output = settings.OutputId is { } outputId ? open(outputId, true) : null;
            mic?.Start();
            output?.Start();
            var start = _clock.Now.Ticks;
            _ready.SetResult(start);
            var watermark = start;
            while (Volatile.Read(ref _boundary) == OPEN_BOUNDARY)
            {
                var now = _clock.Now.Ticks;
                mic?.Drain(Enqueue);
                output?.Drain(Enqueue);
                if (now - watermark >= WATERMARK_PERIOD)
                {
                    // Leave room for engine buffering and ordinary scheduler jitter.
                    Send(new(null, now - REORDER_DELAY));
                    watermark = now;
                }

                _ = cancellationToken.WaitHandle.WaitOne(POLL_MILLISECONDS);
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Allow the engine to deliver a packet that straddles the command boundary.
            var period = TimeSpan.FromTicks(Math.Max(mic?.EnginePeriod.Ticks ?? 0, output?.EnginePeriod.Ticks ?? 0));
            Thread.Sleep(period + TimeSpan.FromMilliseconds(POLL_MILLISECONDS));
            mic?.Drain(Enqueue);
            output?.Drain(Enqueue);
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
            _producer.SetResult();
        }
    }

    private void Enqueue(AudioPacket packet)
    {
        if (packet.Qpc < 0 || packet.Qpc - _clock.Now.Ticks > FUTURE_TOLERANCE)
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

    private void Fail(RecordingProblem message)
    {
        _ = Interlocked.CompareExchange(ref _boundary, _clock.Now.Ticks, OPEN_BOUNDARY);
        _interrupt(message);
    }

    private async Task MixAsync(IRecordingDestination file, Action<Levels> meter, CancellationToken cancellationToken)
    {
        IntervalMixer? mixer = null;
        try
        {
            var start = await Started.WaitAsync(cancellationToken).ConfigureAwait(false);
            mixer = new IntervalMixer(start, file, meter);
            await foreach (var message in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (message.Packet is { } packet)
                {
                    mixer.Add(packet);
                }
                else
                {
                    var cutoff = Volatile.Read(ref _boundary);
                    mixer.Drain(cutoff == OPEN_BOUNDARY ? message.Watermark : Math.Min(message.Watermark, cutoff));
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

    private readonly record struct Message(AudioPacket? Packet, long Watermark);
}
