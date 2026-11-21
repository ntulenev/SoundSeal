namespace MeetingRecorder.Domain;

/// <summary>An immutable state snapshot. The running interval starts at QPC Started.</summary>
public sealed record RecorderSnapshot(RecorderState State, long ActiveTicks = 0,
    long? Started = null, RecordingResult? Result = null)
{
    /// <summary>Sums closed intervals and the current interval, including silence.</summary>
    /// <param name="now">Current QPC reading expressed in 100 ns units.</param>
    /// <returns>Active time, excluding all completed and current user pauses.</returns>
    public TimeSpan Elapsed(long now) => TimeSpan.FromTicks(
        ActiveTicks + (Started.HasValue ? new ActiveInterval(Started.Value, now).Ticks : 0));
}
