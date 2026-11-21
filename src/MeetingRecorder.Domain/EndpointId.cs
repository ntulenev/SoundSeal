namespace MeetingRecorder.Domain;

/// <summary>The opaque, nonblank identifier of a Windows audio endpoint.</summary>
public sealed record EndpointId
{
    /// <summary>Preserves the nonblank Windows identifier exactly as supplied.</summary>
    public EndpointId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The opaque, nonblank identifier of a Windows audio endpoint.</summary>
    public string Value { get; }

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static EndpointId FromString(string value) => new(value);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static implicit operator EndpointId(string value) => FromString(value);
}
