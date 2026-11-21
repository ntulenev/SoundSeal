using MeetingRecorder.Domain;

namespace MeetingRecorder.Application.Abstractions;

/// <summary>Reads the shared performance counter in 100 ns units, independent of UTC.</summary>
public interface IRecordingClock
{
    long Now { get; }
}

/// <summary>Enumerates active devices without opening a recording stream.</summary>
public interface IDeviceCatalog
{
    /// <summary>Reads active endpoints and identifies the Windows multimedia defaults.</summary>
    /// <returns>Snapshots of capture and playback identities and driver-supplied names.</returns>
    /// <exception cref="RecordingException">Windows audio enumeration failed.</exception>
    Devices Read();
}

/// <summary>Creates an isolated, file-owning capture session.</summary>
public interface ICaptureFactory
{
    /// <summary>Reserves an output file for a session without starting either device.</summary>
    /// <param name="settings">Pinned endpoint IDs and the destination folder.</param>
    /// <returns>A session owned by the caller until finalization and disposal.</returns>
    /// <exception cref="RecordingException">The destination could not be opened.</exception>
    ICaptureSession Open(RecordingSettings settings);
}

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

/// <summary>Serializes recording commands and exposes a thread-safe display snapshot.</summary>
public interface IRecorder
{
    RecorderSnapshot Snapshot { get; }
    Levels Levels { get; }
    TimeSpan Elapsed { get; }
    /// <summary>Starts a new recording and resets elapsed time.</summary>
    /// <param name="settings">Device IDs and output folder selected by the user.</param>
    /// <returns>A task that completes after startup or cleanup of a failed attempt.</returns>
    Task StartAsync(RecordingSettings settings);
    /// <summary>Ends the active interval and clears both meters.</summary>
    Task PauseAsync();
    /// <summary>Starts another interval on the original endpoints in the same WAV.</summary>
    Task ResumeAsync();
    /// <summary>Waits for the single owner to publish the recording result.</summary>
    Task FinishAsync();
}
