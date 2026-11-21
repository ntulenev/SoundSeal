namespace MeetingRecorder.Domain;

/// <summary>Snapshot of the recorder timeline and file outcome.</summary>
public sealed record RecorderSnapshot
{
    /// <summary>Creates a validated snapshot of the recorder timeline and file outcome.</summary>
    /// <param name="state">Current recorder state.</param>
    /// <param name="activeTime">Accumulated ticks in completed intervals.</param>
    /// <param name="started">Start of the active interval, present only while recording.</param>
    /// <param name="result">Final file outcome, present only after completion.</param>
    public RecorderSnapshot(RecorderState state,
        RecordingDuration? activeTime = null,
        CaptureTimestamp? started = null,
        RecordingResult? result = null)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        var running = state == RecorderState.Recording;
        if (running != started.HasValue)
        {
            throw new ArgumentException("A running recording requires a nonnegative start time.", nameof(started));
        }

        var validOutcome = result switch
        {
            null => state is not (RecorderState.Saved or RecorderState.Error),
            { IsSuccessful: true } => state == RecorderState.Saved,
            { Problem: not null } => state == RecorderState.Error,
            _ => false
        };
        if (!validOutcome)
        {
            throw new ArgumentException("The file outcome must match the terminal state.", nameof(result));
        }

        var duration = result is null ? activeTime.GetValueOrDefault() : RecordingFormat.Duration(result.Frames);
        if ((activeTime.HasValue && activeTime.Value != duration)
            || (state is RecorderState.Ready or RecorderState.Starting && duration != default))
        {
            throw new ArgumentException("Active time must match the recording state and written frames.", nameof(activeTime));
        }

        State = state;
        ActiveTime = duration;
        Started = started;
        Result = result;
    }

    /// <summary>Current recorder state.</summary>
    public RecorderState State { get; }

    /// <summary>Accumulated ticks in completed intervals.</summary>
    public RecordingDuration ActiveTime { get; }

    /// <summary>Start of the active interval, present only while recording.</summary>
    public CaptureTimestamp? Started { get; }

    /// <summary>Final file outcome, present only after completion.</summary>
    public RecordingResult? Result { get; }

    /// <summary>Includes the current interval and clamps stale clock samples to its start.</summary>
    public RecordingDuration Elapsed(CaptureTimestamp now)
    {
        var current = Started is { } start ? Math.Max(0, now.Ticks - start.Ticks) : 0;
        return TimeSpan.FromTicks(checked(ActiveTime.Value.Ticks + current));
    }
}
