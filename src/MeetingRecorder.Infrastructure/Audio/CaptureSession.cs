using MeetingRecorder.Application.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Files;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Owns the temporary WAV across pauses and resets the independent peak holds.</summary>
internal sealed class CaptureSession : ICaptureSession
{
    private readonly RecordingSettings _settings;
    private readonly IRecordingClock _clock;
    private readonly WaveDestination _file;
    private readonly Func<string, bool, IInputEndpoint> _open;
    private readonly Lock _levelsLock = new();
    private const long HOLD = TimeSpan.TicksPerMillisecond * 180;
    private float _micPeak, _outputPeak;
    private long _micUntil, _outputUntil;
    private CaptureInterval? _interval;
    private string? _problem;
    private bool _completed;

    internal CaptureSession(RecordingSettings settings, IRecordingClock clock,
        WaveDestination file, Func<string, bool, IInputEndpoint> open)
    {
        _settings = settings;
        _clock = clock;
        _file = file;
        _open = open;
    }

    public event EventHandler? Interrupted;
    public string? Problem => Volatile.Read(ref _problem);

    public Levels Levels {
        get
        {
            lock (_levelsLock)
            {
                var now = _clock.Now;
                return new(now < _micUntil ? _micPeak : 0,
                    now < _outputUntil ? _outputPeak : 0);
            }
        }
    }

    public Task<long> BeginIntervalAsync()
    {
        _interval = new CaptureInterval(_settings, _clock, _open, _file, Update, Fail);
        return _interval.Started;
    }

    public async Task EndIntervalAsync(long boundary)
    {
        if (_interval is not null)
        {
            await _interval.StopAsync(boundary).ConfigureAwait(false);
            _interval = null;
        }

        lock (_levelsLock)
        {
            _micUntil = _outputUntil = 0;
        }
    }

    public async Task<RecordingResult> CompleteAsync(bool discardEmpty)
    {
        await EndIntervalAsync(_clock.Now).ConfigureAwait(false);
        var result = await Task.Run(() => _file.Complete(discardEmpty, Problem))
            .ConfigureAwait(false);
        _completed = true;
        return result;
    }

    private void Fail(string message)
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
            var now = _clock.Now;
            Hold(peaks.Microphone, now, ref _micPeak, ref _micUntil);
            Hold(peaks.Computer, now, ref _outputPeak, ref _outputUntil);
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

    public void Dispose()
    {
        try
        {
            if (!_completed)
            {
                // Also release both devices if an owner exits before normal publication.
                _ = CompleteAsync(discardEmpty: true).GetAwaiter().GetResult();
            }
        }
        finally
        {
            _file.Dispose();
        }
    }
}
