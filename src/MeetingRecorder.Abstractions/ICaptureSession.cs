using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Owns one temporary WAV and all intervals appended to it.</summary>
public interface ICaptureSession : IDisposable
{
    /// <summary>Signals an interruption that requires finalization.</summary>
    event EventHandler? Interrupted;

    /// <summary>First capture or storage error, if any.</summary>
    RecordingProblem? Problem { get; }

    /// <summary>Current post-gain peaks for the active interval.</summary>
    Levels Levels { get; }

    /// <summary>Starts both pinned devices and returns the opening QPC in 100 ns units.</summary>
    /// <param name="cancellationToken">Cancels the wait for device startup.</param>
    /// <returns>The inclusive start timestamp after both streams have started.</returns>
    /// <exception cref="RecordingException">A pinned endpoint could not start.</exception>
    Task<CaptureTimestamp> BeginIntervalAsync(CancellationToken cancellationToken);

    /// <summary>Stops both devices and drains packets strictly before the QPC boundary.</summary>
    /// <param name="boundary">Exclusive cutoff in shared 100 ns performance-counter ticks.</param>
    /// <param name="cancellationToken">Signals cancellation of the operation.</param>
    /// <returns>A task that completes after the capture thread and mixer exit.</returns>
    Task EndIntervalAsync(CaptureTimestamp boundary, CancellationToken cancellationToken);

    /// <summary>Publishes the file or describes the surviving unfinished file.</summary>
    /// <param name="emptyPolicy">Remove an empty file when startup failed.</param>
    /// <param name="cancellationToken">Signals cancellation of the operation.</param>
    /// <returns>The durable file outcome, including any interruption.</returns>
    Task<RecordingResult> CompleteAsync(EmptyRecordingPolicy emptyPolicy, CancellationToken cancellationToken);
}
