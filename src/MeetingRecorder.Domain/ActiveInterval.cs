namespace MeetingRecorder.Domain;

/// <summary>A half-open interval on the WASAPI QPC timeline, in 100 ns units.</summary>
/// <param name="Start">Inclusive start on the shared performance-counter timeline.</param>
/// <param name="End">Exclusive pause or finish boundary on the same timeline.</param>
public readonly record struct ActiveInterval(long Start, long End)
{
    public long Ticks => Math.Max(0, End - Start);
    public long Frames => Ticks / RecordingFormat.TicksPerFrame;
}
