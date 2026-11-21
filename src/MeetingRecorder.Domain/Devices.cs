namespace MeetingRecorder.Domain;

/// <summary>Active capture and render endpoints at the time of enumeration.</summary>
public sealed record Devices(IReadOnlyList<Endpoint> Microphones,
    IReadOnlyList<Endpoint> Outputs);
