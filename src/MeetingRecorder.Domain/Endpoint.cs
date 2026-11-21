namespace MeetingRecorder.Domain;

/// <summary>Stable endpoint identity and the verbatim name supplied by its driver.</summary>
public sealed record Endpoint(string Id, string Name, bool IsDefault);
