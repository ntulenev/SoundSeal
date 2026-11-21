using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Application;

/// <summary>Owns transitions, active time and exactly one finalization per recording.</summary>
public sealed class RecordingCoordinator : IRecorder, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ICaptureFactory _factory;
    private readonly IRecordingClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile RecorderSnapshot _snapshot = new(RecorderState.Ready);
    private volatile ICaptureSession? _session;
    private long _generation;
    private int _disposed;

    /// <summary>Creates the session coordinator with its capture factory and shared clock.</summary>
    public RecordingCoordinator(ICaptureFactory factory, IRecordingClock clock)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);
        _factory = factory;
        _clock = clock;
    }

    /// <inheritdoc/>
    public RecorderSnapshot Snapshot => _snapshot;

    /// <inheritdoc/>
    public RecordingDuration Elapsed => _snapshot.Elapsed(_clock.Now);

    /// <inheritdoc/>
    public Levels Levels => _snapshot.State == RecorderState.Recording
        ? _session?.Levels ?? default : default;

    /// <summary>Creates a fresh file and enters Recording after the selected devices start.</summary>
    public async Task StartAsync(RecordingSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var requested = _snapshot;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                _session = await _factory.OpenAsync(settings, cancellationToken).ConfigureAwait(false);
                _session.Interrupted += OnInterrupted;
                var start = await _session.BeginIntervalAsync(cancellationToken).ConfigureAwait(false);
                _snapshot = new(RecorderState.Recording, started: start);
            }
            catch (OperationCanceledException)
            {
                await PublishAsync("Recording canceled.", EmptyRecordingPolicy.Discard, _lifetime.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (RecordingException ex)
            {
                await PublishAsync(ex.Message, EmptyRecordingPolicy.Discard, _lifetime.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <summary>Closes the current interval at the instant of the pause request.</summary>
    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        var boundary = _clock.Now;
        var requested = _snapshot;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(requested, _snapshot) || requested.State != RecorderState.Recording)
            {
                return;
            }

            await EndAsync(boundary, RecorderState.Pausing, _lifetime.Token).ConfigureAwait(false);
            if (_session!.Problem is not null)
            {
                await PublishAsync(_session.Problem, EmptyRecordingPolicy.Keep, _lifetime.Token).ConfigureAwait(false);
            }
            else
            {
                _snapshot = new(RecorderState.Paused, _snapshot.ActiveTime);
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <summary>Reopens the pinned endpoints and appends a new interval.</summary>
    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        var requested = _snapshot;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(requested, _snapshot) || requested.State != RecorderState.Paused)
            {
                return;
            }

            _snapshot = new(RecorderState.Resuming, requested.ActiveTime);
            try
            {
                var start = await _session!.BeginIntervalAsync(cancellationToken).ConfigureAwait(false);
                _snapshot = new(RecorderState.Recording, requested.ActiveTime, start);
            }
            catch (OperationCanceledException)
            {
                await PublishAsync("Recording canceled.", EmptyRecordingPolicy.Keep, _lifetime.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (RecordingException ex)
            {
                await PublishAsync(ex.Message, EmptyRecordingPolicy.Keep, _lifetime.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <summary>Drains an open interval and awaits publication or a recovery result.</summary>
    public Task FinishAsync(CancellationToken cancellationToken) =>
        FinishSessionAsync(Volatile.Read(ref _generation), _clock.Now, null, cancellationToken);

    /// <summary>Waits for any active recording to finalize, then releases the command gate.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                FinishAsync(_lifetime.Token).GetAwaiter().GetResult();
            }
            finally
            {
                _lifetime.Cancel();
                _lifetime.Dispose();
                _gate.Dispose();
            }
        }
    }

    private void OnInterrupted(object? sender, EventArgs e)
    {
        if (sender is ICaptureSession session)
        {
            _ = FinishSessionAsync(Volatile.Read(ref _generation), _clock.Now, session, _lifetime.Token);
        }
    }

    private async Task FinishSessionAsync(long generation, CaptureTimestamp end, ICaptureSession? interrupted, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is not { } owner || generation != _generation ||
                (interrupted is not null && !ReferenceEquals(interrupted, owner)))
            {
                return;
            }

            if (_snapshot.Started.HasValue)
            {
                await EndAsync(end, RecorderState.Saving, _lifetime.Token).ConfigureAwait(false);
            }

            await PublishAsync(owner.Problem, EmptyRecordingPolicy.Keep, _lifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task EndAsync(CaptureTimestamp end, RecorderState transition, CancellationToken cancellationToken)
    {
        _snapshot = new(transition, _snapshot.Elapsed(end));
        await _session!.EndIntervalAsync(end, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishAsync(RecordingProblem? problem, EmptyRecordingPolicy emptyPolicy, CancellationToken cancellationToken)
    {
        _snapshot = new(RecorderState.Saving, _snapshot.ActiveTime);
        var result = new RecordingResult(null, 0, problem, false);
        if (_session is { } session)
        {
            session.Interrupted -= OnInterrupted;
            try
            {
                result = await session.CompleteAsync(emptyPolicy, cancellationToken).ConfigureAwait(false);
                if (problem is not null)
                {
                    result = result.WithCaptureProblem(problem);
                }
            }
            finally
            {
                session.Dispose();
                _session = null;
            }
        }

        _snapshot = new(result.IsSuccessful
            ? RecorderState.Saved : RecorderState.Error,
            result: result);
    }
}
