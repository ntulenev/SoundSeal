using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Serializes recording commands and exposes a thread-safe display snapshot.</summary>
public interface IRecorder
{
    /// <summary>Latest immutable state and file outcome.</summary>
    RecorderSnapshot Snapshot { get; }

    /// <summary>Current microphone and computer audio peaks.</summary>
    Levels Levels { get; }

    /// <summary>Total active time across intervals.</summary>
    RecordingDuration Elapsed { get; }

    /// <summary>Starts a new recording and resets elapsed time.</summary>
    /// <param name="settings">Device IDs and output folder selected by the user.</param>
    /// <param name="cancellationToken">Signals cancellation of the operation.</param>
    /// <returns>A task that completes after startup or cleanup of a failed attempt.</returns>
    Task StartAsync(RecordingSettings settings, CancellationToken cancellationToken);

    /// <summary>Ends the active interval and clears both meters.</summary>
    Task PauseAsync(CancellationToken cancellationToken);

    /// <summary>Starts another interval on the original endpoints in the same recording.</summary>
    Task ResumeAsync(CancellationToken cancellationToken);

    /// <summary>Waits for the single owner to publish the recording result.</summary>
    Task FinishAsync(CancellationToken cancellationToken);
}
