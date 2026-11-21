namespace MeetingRecorder.Domain;

/// <summary>Elapsed recording time with all paused intervals removed.</summary>
public readonly record struct RecordingDuration
{
    /// <summary>Creates a duration from a nonnegative length of time.</summary>
    public RecordingDuration(TimeSpan value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
        Value = value;
    }

    /// <summary>Length of active recording time.</summary>
    public TimeSpan Value { get; }

    /// <summary>Validates an elapsed time supplied by a clock or a file reader.</summary>
    public static RecordingDuration FromTimeSpan(TimeSpan value) => new(value);

    /// <inheritdoc cref="FromTimeSpan"/>
    public static implicit operator RecordingDuration(TimeSpan value) => FromTimeSpan(value);
}
