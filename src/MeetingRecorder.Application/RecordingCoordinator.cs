using MeetingRecorder.Application.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Application;

/// <summary>Owns transitions, active time and exactly one finalization per recording.</summary>
public sealed class RecordingCoordinator : IRecorder, IDisposable
{
    private readonly ICaptureFactory _factory;
    private readonly IRecordingClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile RecorderSnapshot _snapshot = new(RecorderState.Ready);
    private volatile ICaptureSession? _session;
    private long _generation;
    private int _disposed;

    public RecordingCoordinator(ICaptureFactory factory, IRecordingClock clock)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);
        _factory = factory;
        _clock = clock;
    }

    public RecorderSnapshot Snapshot => _snapshot;
    public TimeSpan Elapsed => _snapshot.Elapsed(_clock.Now);
    public Levels Levels => _snapshot.State == RecorderState.Recording
        ? _session?.Levels ?? default : default;

    /// <summary>Creates a fresh file and enters Recording after both devices start.</summary>
    public async Task StartAsync(RecordingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var requested = _snapshot;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(requested, _snapshot) ||
                requested.State is not (RecorderState.Ready or RecorderState.Saved
                    or RecorderState.Error))
            {
                return;
            }

            _ = Interlocked.Increment(ref _generation);
            _snapshot = new(RecorderState.Starting);
            try
            {
                _session = await Task.Run(() => _factory.Open(settings)).ConfigureAwait(false);
                _session.Interrupted += OnInterrupted;
                var start = await _session.BeginIntervalAsync().ConfigureAwait(false);
                _snapshot = new(RecorderState.Recording, Started: start);
            }
            catch (RecordingException ex)
            {
                await PublishAsync(ex.Message, discardEmpty: true).ConfigureAwait(false);
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <summary>Closes the current interval at the instant of the pause request.</summary>
    public async Task PauseAsync()
    {
        var boundary = _clock.Now;
        var requested = _snapshot;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(requested, _snapshot) || requested.State != RecorderState.Recording)
            {
                return;
            }

            await EndAsync(boundary, RecorderState.Pausing).ConfigureAwait(false);
            if (_session!.Problem is not null)
            {
                await PublishAsync(_session.Problem).ConfigureAwait(false);
            }
            else
            {
                _snapshot = _snapshot with { State = RecorderState.Paused };
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <summary>Reopens the pinned endpoints and appends a new interval.</summary>
    public async Task ResumeAsync()
    {
        var requested = _snapshot;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(requested, _snapshot) || requested.State != RecorderState.Paused)
            {
                return;
            }

            _snapshot = requested with { State = RecorderState.Resuming };
            try
            {
                var start = await _session!.BeginIntervalAsync().ConfigureAwait(false);
                _snapshot = requested with { State = RecorderState.Recording, Started = start };
            }
            catch (RecordingException ex)
            {
                await PublishAsync(ex.Message).ConfigureAwait(false);
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <summary>Drains an open interval and awaits publication or a recovery result.</summary>
    public Task FinishAsync() =>
        FinishSessionAsync(Volatile.Read(ref _generation), _clock.Now);

    private void OnInterrupted(object? sender, EventArgs e)
    {
        if (sender is ICaptureSession session)
        {
            _ = FinishSessionAsync(Volatile.Read(ref _generation), _clock.Now, session);
        }
    }

    private async Task FinishSessionAsync(long generation, long end,
        ICaptureSession? interrupted = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is not { } owner || generation != _generation ||
                (interrupted is not null && !ReferenceEquals(interrupted, owner)))
            {
                return;
            }

            if (_snapshot.Started.HasValue)
            {
                await EndAsync(end, RecorderState.Saving).ConfigureAwait(false);
            }

            await PublishAsync(owner.Problem).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task EndAsync(long end, RecorderState transition)
    {
        _snapshot = new(transition, _snapshot.Elapsed(end).Ticks);
        await _session!.EndIntervalAsync(end).ConfigureAwait(false);
    }

    private async Task PublishAsync(string? problem, bool discardEmpty = false)
    {
        _snapshot = _snapshot with { State = RecorderState.Saving };
        var result = new RecordingResult(null, 0, problem, false);
        if (_session is { } session)
        {
            session.Interrupted -= OnInterrupted;
            try
            {
                result = await session.CompleteAsync(discardEmpty).ConfigureAwait(false);
                result = result with { Problem = result.Problem ?? problem };
            }
            finally
            {
                session.Dispose();
                _session = null;
            }
        }

        _snapshot = new(result.Published && result.Problem is null
            ? RecorderState.Saved : RecorderState.Error,
            RecordingFormat.Duration(result.Frames).Ticks, Result: result);
    }

    /// <summary>Waits for any active recording to finalize, then releases the command gate.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                FinishAsync().GetAwaiter().GetResult();
            }
            finally
            {
                _gate.Dispose();
            }
        }
    }
}
