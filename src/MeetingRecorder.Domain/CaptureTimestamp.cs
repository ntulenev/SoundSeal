namespace MeetingRecorder.Domain;

/// <summary>A monotonic device timestamp in 100 ns QPC ticks.</summary>
public readonly record struct CaptureTimestamp
{
    /// <summary>Creates a clock position from nonnegative 100 ns ticks.</summary>
    public CaptureTimestamp(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Ticks = value;
    }

    /// <summary>A monotonic device timestamp in 100 ns QPC ticks.</summary>
    public long Ticks { get; }

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static CaptureTimestamp FromInt64(long value) => new(value);

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static implicit operator CaptureTimestamp(long value) => FromInt64(value);
}
