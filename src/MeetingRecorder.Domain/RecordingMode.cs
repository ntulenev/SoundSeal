namespace MeetingRecorder.Domain;

/// <summary>Audio sources included in one recording.</summary>
public enum RecordingMode
{
    /// <summary>Mix the selected microphone and computer playback.</summary>
    Meeting,
    /// <summary>Record computer playback through loopback.</summary>
    Audio,
    /// <summary>Record the selected microphone.</summary>
    Dictaphone
}
