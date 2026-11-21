namespace MeetingRecorder.Domain;

/// <summary>The endpoint IDs and destination pinned for the entire recording.</summary>
public sealed record RecordingSettings(string MicrophoneId, string OutputId, string Folder);
