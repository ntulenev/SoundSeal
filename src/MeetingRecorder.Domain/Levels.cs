namespace MeetingRecorder.Domain;

/// <summary>Independent post-gain peaks from samples written to the file.</summary>
public readonly record struct Levels(float Microphone, float Computer);
