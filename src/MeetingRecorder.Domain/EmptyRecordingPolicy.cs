namespace MeetingRecorder.Domain;

/// <summary>Controls publication when no complete sample was captured.</summary>
public enum EmptyRecordingPolicy
{
    /// <summary>Publish the WAV even when it contains only the header.</summary>
    Keep,
    /// <summary>Remove the temporary file when a failed start captured nothing.</summary>
    Discard
}
