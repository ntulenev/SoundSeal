namespace MeetingRecorder.Domain;

/// <summary>A nonnegative number of complete mono output frames.</summary>
public readonly record struct FrameCount
{
    /// <summary>Creates a count of output frames whose playing time fits a TimeSpan.</summary>
    public FrameCount(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, long.MaxValue / RecordingFormat.TicksPerFrame);
        Value = value;
    }

    /// <summary>A nonnegative number of complete mono output frames.</summary>
    public long Value { get; }

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static FrameCount FromInt64(long value) => new(value);

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static implicit operator FrameCount(long value) => FromInt64(value);
}
