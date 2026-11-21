namespace MeetingRecorder.Application.Abstractions;

/// <summary>Reads the shared performance counter in 100 ns units, independent of UTC.</summary>
public interface IRecordingClock
{
    long Now { get; }
}
