namespace MeetingRecorder.Domain;

/// <summary>The nonblank display name supplied by the audio driver.</summary>
public sealed record EndpointName
{
    /// <summary>Preserves a nonblank device label supplied by Windows.</summary>
    public EndpointName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The nonblank display name supplied by the audio driver.</summary>
    public string Value { get; }

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static EndpointName FromString(string value) => new(value);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static implicit operator EndpointName(string value) => FromString(value);
}
