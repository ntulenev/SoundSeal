using MeetingRecorder.Domain;

namespace MeetingRecorder.Application.Abstractions;

/// <summary>Owns one temporary WAV and all intervals appended to it.</summary>
public interface ICaptureSession : IDisposable
{
    event EventHandler? Interrupted;
    string? Problem { get; }
    Levels Levels { get; }

    /// <summary>Starts both pinned devices and returns the opening QPC in 100 ns units.</summary>
    /// <returns>The inclusive start timestamp after both streams have started.</returns>
    /// <exception cref="RecordingException">A pinned endpoint could not start.</exception>
    Task<long> BeginIntervalAsync();

    /// <summary>Stops both devices and drains packets strictly before the QPC boundary.</summary>
    /// <param name="boundary">Exclusive cutoff in shared 100 ns performance-counter ticks.</param>
    /// <returns>A task that completes after the capture thread and mixer exit.</returns>
    Task EndIntervalAsync(long boundary);

    /// <summary>Publishes the file or describes the surviving unfinished file.</summary>
    /// <param name="discardEmpty">Remove an empty file when startup failed.</param>
    /// <returns>The durable file outcome, including any interruption.</returns>
    Task<RecordingResult> CompleteAsync(bool discardEmpty);
}
