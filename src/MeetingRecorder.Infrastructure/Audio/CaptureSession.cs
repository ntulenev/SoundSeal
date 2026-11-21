using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Owns the temporary WAV across pauses and resets the independent peak holds.</summary>
internal sealed class CaptureSession : ICaptureSession
{
    private const long HOLD = TimeSpan.TicksPerMillisecond * 180;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly RecordingSettings _settings;
    private readonly IRecordingClock _clock;
    private readonly IRecordingDestination _file;
    private readonly Func<EndpointId, bool, IInputEndpoint> _open;
    private readonly int _capacity;
    private readonly Lock _levelsLock = new();
    private float _micPeak, _outputPeak;
    private long _micUntil, _outputUntil;
    private CaptureInterval? _interval;
    private RecordingProblem? _problem;
    private bool _completed;
    private bool _disposed;

    /// <summary>Pins the device selection and destination across pause and resume.</summary>
    internal CaptureSession(RecordingSettings settings, IRecordingClock clock,
        IRecordingDestination file, Func<EndpointId, bool, IInputEndpoint> open,
        int capacity = AudioCaptureOptions.DEFAULT_QUEUE_CAPACITY)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _settings = settings;
        _clock = clock;
        _file = file;
        _open = open;
    }

    /// <inheritdoc/>
    public event EventHandler? Interrupted;

    /// <inheritdoc/>
    public RecordingProblem? Problem => Volatile.Read(ref _problem);

    /// <inheritdoc/>
    public Levels Levels {
        get
        {
            lock (_levelsLock)
            {
                var now = _clock.Now.Ticks;
                return new(now < _micUntil ? _micPeak : 0,
                    now < _outputUntil ? _outputPeak : 0);
            }
        }
    }

    /// <inheritdoc/>
    public Task<CaptureTimestamp> BeginIntervalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_completed, this);
        if (_interval is not null)
        {
            throw new InvalidOperationException("A capture interval is already active.");
        }

        _interval = new CaptureInterval(_settings, _clock, _open, _file, Update, Fail, _capacity, _lifetime.Token);
        return _interval.Started.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task EndIntervalAsync(CaptureTimestamp boundary, CancellationToken cancellationToken)
    {
        if (_interval is not null)
        {
            await _interval.StopAsync(boundary, cancellationToken).ConfigureAwait(false);
            _interval = null;
        }

        lock (_levelsLock)
        {
            _micUntil = _outputUntil = 0;
        }
    }

    /// <inheritdoc/>
    public async Task<RecordingResult> CompleteAsync(EmptyRecordingPolicy emptyPolicy, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(emptyPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(emptyPolicy));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_completed, this);
        await EndIntervalAsync(_clock.Now, _lifetime.Token).ConfigureAwait(false);
        var result = await _file.CompleteAsync(emptyPolicy, Problem, _lifetime.Token)
            .ConfigureAwait(false);
        _completed = true;
        return result;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (!_completed)
            {
                // Also release both devices if an owner exits before normal publication.
                _ = CompleteAsync(EmptyRecordingPolicy.Discard, _lifetime.Token).GetAwaiter().GetResult();
            }
        }
        finally
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            _file.Dispose();
        }
    }

    private void Fail(RecordingProblem message)
    {
        if (Interlocked.CompareExchange(ref _problem, message, null) is null)
        {
            Interrupted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Update(Levels peaks)
    {
        lock (_levelsLock)
        {
            var now = _clock.Now.Ticks;
            Hold(peaks.Microphone.Value, now, ref _micPeak, ref _micUntil);
            Hold(peaks.Computer.Value, now, ref _outputPeak, ref _outputUntil);
        }
    }

    private static void Hold(float value, long now, ref float peak, ref long until)
    {
        if (value >= peak || now >= until)
        {
            peak = value;
            until = now + HOLD;
        }
    }
}
