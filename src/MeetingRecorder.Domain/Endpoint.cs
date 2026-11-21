namespace MeetingRecorder.Domain;

/// <summary>Windows audio endpoint identity.</summary>
public sealed record Endpoint
{
    /// <summary>Describes a Windows endpoint and its default selection status.</summary>
    /// <param name="id">Stable Windows device ID.</param>
    /// <param name="name">Display name supplied by the driver.</param>
    /// <param name="isDefault">Whether Windows selected this endpoint as the default.</param>
    public Endpoint(EndpointId id, EndpointName name, bool isDefault)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);

        Id = id;
        Name = name;
        IsDefault = isDefault;
    }

    /// <summary>Stable Windows device ID.</summary>
    public EndpointId Id { get; }

    /// <summary>Display name supplied by the driver.</summary>
    public EndpointName Name { get; }

    /// <summary>Whether Windows selected this endpoint as the default.</summary>
    public bool IsDefault { get; }
}
