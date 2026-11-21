using MeetingRecorder.Domain;

namespace MeetingRecorder.Application.Abstractions;

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
