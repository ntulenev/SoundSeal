using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Reads the shared performance counter in 100 ns units, independent of UTC.</summary>
public interface IRecordingClock
{
    /// <summary>Monotonic time in 100 ns units shared by both streams.</summary>
    CaptureTimestamp Now { get; }
}
