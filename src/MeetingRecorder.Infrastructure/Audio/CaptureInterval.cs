using System.Diagnostics;
using System.Threading.Channels;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Owns the capture thread, bounded handoff and mixing worker for a single
/// interval. Starting another interval always allocates new queues and filter state.</summary>
internal sealed partial class CaptureInterval
{
    private const long OPEN_BOUNDARY = -1;
    private const int POLL_MILLISECONDS = 5;
    private const long WATERMARK_PERIOD = 25 * TimeSpan.TicksPerMillisecond;
    private const long REORDER_DELAY = 150 * TimeSpan.TicksPerMillisecond;
    private const long FUTURE_TOLERANCE = 100 * TimeSpan.TicksPerMillisecond;
    private const int MAX_BATCH_MESSAGES = 32;
    private const int MAX_BATCH_BYTES = 128 * 1024;

    private readonly Channel<List<Message>> _queue;
    private readonly ILogger<CaptureInterval> _logger;
    private readonly int _capacity;
    private readonly long _created = Stopwatch.GetTimestamp();
    private readonly TaskCompletionSource<CaptureTimestamp> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IRecordingClock _clock;
    private readonly Action<RecordingProblem> _interrupt;
    private readonly TaskCompletionSource<List<Message>> _lastBatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _consumer;
    private List<Message> _pending = [];
    private int _pendingBytes;
    private long _microphonePackets;
    private long _outputPackets;
    private long _lastMixedBatch;
    private int? _queuedAtFailure;
    private long _boundary = OPEN_BOUNDARY;

    /// <summary>Starts the native capture thread and asynchronous mixer for a single interval.</summary>
    internal CaptureInterval(EndpointId? microphone, EndpointId? outputId, IRecordingClock clock,
        Func<EndpointId, bool, IInputEndpoint> open, IPcmSink file,
        Action<Levels> meter, Action<RecordingProblem> interrupt, int capacity, CancellationToken cancellationToken,
        ILogger<CaptureInterval>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(interrupt);
        _clock = clock;
        _capacity = capacity;
        _logger = logger ?? NullLogger<CaptureInterval>.Instance;
        _lastMixedBatch = _created;
        _interrupt = interrupt;
        _queue = Channel.CreateBounded<List<Message>>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        _consumer = MixAsync(file, meter, cancellationToken);
        var thread = new Thread(() => Capture(microphone, outputId, open, cancellationToken))
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
        await Task.WhenAll(_lastBatch.Task, _consumer).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "The producer must settle startup and notify the owner on every thread exit.")]
    private void Capture(EndpointId? microphone, EndpointId? outputId, Func<EndpointId, bool, IInputEndpoint> open, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var mic = microphone is not null ? open(microphone, false) : null;
            using var output = outputId is not null ? open(outputId, true) : null;
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

                // Hand off immediately when caught up. While the mixer is busy writing,
                // pack small device packets and watermarks together in capture order.
                if (_pending.Count > 0 && _queue.Reader.Count == 0)
                {
                    PublishBatch();
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
                var queued = _queuedAtFailure ?? _queue.Reader.Count;
                var elapsed = Stopwatch.GetElapsedTime(_created);
                var mixerDelay = Stopwatch.GetElapsedTime(Volatile.Read(ref _lastMixedBatch));
                Fail(failure.Message);
                if (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    LogCaptureFailure(_logger, failure, elapsed, mixerDelay, queued, _capacity, _pending.Count,
                        _pendingBytes, _microphonePackets, _outputPackets);
                }
            }
        }
        finally
        {
            _ = _queue.Writer.TryComplete();
            // Transfer the producer's tail even if the channel was full at stop or device loss.
            _lastBatch.SetResult(_pending);
        }
    }

    private void Enqueue(AudioPacket packet)
    {
        if (packet.Loopback)
        {
            _outputPackets++;
        }
        else
        {
            _microphonePackets++;
        }
        if (packet.Qpc < 0 || packet.Qpc - _clock.Now.Ticks > FUTURE_TOLERANCE)
        {
            throw new RecordingException("The device returned an invalid audio timestamp.");
        }

        Send(new(packet, 0));
    }

    private void Send(Message message)
    {
        var bytes = message.Packet?.Data.Length ?? 0;
        if (_pending.Count == MAX_BATCH_MESSAGES || (_pending.Count > 0 && bytes > MAX_BATCH_BYTES - _pendingBytes))
        {
            PublishBatch();
        }

        _pending.Add(message);
        _pendingBytes += bytes;
    }

    private void PublishBatch()
    {
        if (!_queue.Writer.TryWrite(_pending))
        {
            // Endpoint disposal can take time while the mixer is already catching up.
            _queuedAtFailure = _capacity;
            throw new RecordingException("The capture queue filled up. The recording is incomplete.");
        }
        _pending = [];
        _pendingBytes = 0;
    }

    private void Fail(RecordingProblem message)
    {
        _ = Interlocked.CompareExchange(ref _boundary, _clock.Now.Ticks, OPEN_BOUNDARY);
        _interrupt(message);
    }

    private async Task MixAsync(IPcmSink file, Action<Levels> meter, CancellationToken cancellationToken)
    {
        IntervalMixer? mixer = null;
        try
        {
            var start = await Started.WaitAsync(cancellationToken).ConfigureAwait(false);
            mixer = new IntervalMixer(start, file, meter);
            await foreach (var batch in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                MixBatch(mixer, batch);
                Volatile.Write(ref _lastMixedBatch, Stopwatch.GetTimestamp());
            }

            MixBatch(mixer, await _lastBatch.Task.ConfigureAwait(false));
            mixer.Drain(Volatile.Read(ref _boundary));
        }
        catch (RecordingException ex) when (mixer is not null)
        {
            var queued = _queue.Reader.Count;
            var elapsed = Stopwatch.GetElapsedTime(_created);
            Fail(ex.Message);
            LogMixFailure(_logger, ex, "Mix", elapsed, queued, _capacity, mixer.WrittenFrames);
            try
            {
                mixer.Drain(Volatile.Read(ref _boundary));
            }
            catch (RecordingException drainError)
            {
                LogMixFailure(_logger, drainError, "RecoveryDrain", Stopwatch.GetElapsedTime(_created),
                    _queue.Reader.Count, _capacity, mixer.WrittenFrames);
            }
        }
        catch (RecordingException)
        {
            // Startup failure is delivered to BeginIntervalAsync through Started.
        }
        catch (Exception ex) when (mixer is not null && ex is not OperationCanceledException)
        {
            LogMixFailure(_logger, ex, "Mix", Stopwatch.GetElapsedTime(_created), _queue.Reader.Count,
                _capacity, mixer.WrittenFrames);
            throw;
        }
    }

    private void MixBatch(IntervalMixer mixer, List<Message> batch)
    {
        foreach (var message in batch)
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
    }

    private readonly record struct Message(AudioPacket? Packet, long Watermark);

    [LoggerMessage(1, LogLevel.Error, "Audio capture failed after {IntervalElapsed}. Time since the last mixed batch={MixerDelay}, queued batches={QueuedBatches}/{QueueCapacity}, pending messages={PendingMessages}, pending bytes={PendingBytes}, microphone packets={MicrophonePackets}, output packets={OutputPackets}.")]
    private static partial void LogCaptureFailure(ILogger logger, Exception exception, TimeSpan intervalElapsed,
        TimeSpan mixerDelay, int queuedBatches, int queueCapacity, int pendingMessages, int pendingBytes, long microphonePackets, long outputPackets);

    [LoggerMessage(2, LogLevel.Error, "Audio mixing or writing failed during {Stage} after {IntervalElapsed}. Queued batches={QueuedBatches}/{QueueCapacity}, written frames={WrittenFrames}.")]
    private static partial void LogMixFailure(ILogger logger, Exception exception, string stage, TimeSpan intervalElapsed,
        int queuedBatches, int queueCapacity, long writtenFrames);
}
