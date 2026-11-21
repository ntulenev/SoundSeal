namespace MeetingRecorder.Domain;

/// <summary>Half-open interval on the capture clock.</summary>
public readonly record struct ActiveInterval
{
    /// <summary>Creates a validated half-open interval on the capture clock.</summary>
    /// <param name="start">Inclusive start in 100 ns clock ticks.</param>
    /// <param name="end">Exclusive end on the same clock.</param>
    public ActiveInterval(CaptureTimestamp start, CaptureTimestamp end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(end.Ticks, start.Ticks, nameof(end));

        Start = start;
        End = end;
    }

    /// <summary>Inclusive start in 100 ns clock ticks.</summary>
    public CaptureTimestamp Start { get; }

    /// <summary>Exclusive end on the same clock.</summary>
    public CaptureTimestamp End { get; }

    /// <summary>Active recording time between the inclusive start and exclusive end.</summary>
    public RecordingDuration Duration => TimeSpan.FromTicks(End.Ticks - Start.Ticks);

    /// <summary>Number of complete output frames in this interval.</summary>
    public FrameCount Frames => Duration.Value.Ticks / RecordingFormat.TicksPerFrame;
}
