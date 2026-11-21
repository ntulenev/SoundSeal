namespace MeetingRecorder.Domain;

/// <summary>Snapshot of available audio endpoints.</summary>
public sealed record Devices
{
    /// <summary>Creates a validated snapshot of available audio endpoints.</summary>
    /// <param name="microphones">Available capture devices.</param>
    /// <param name="outputs">Available playback devices.</param>
    public Devices(IReadOnlyList<Endpoint> microphones, IReadOnlyList<Endpoint> outputs)
    {
        Microphones = CopyEndpoints(microphones, nameof(microphones));
        Outputs = CopyEndpoints(outputs, nameof(outputs));
    }

    /// <summary>Available capture devices.</summary>
    public IReadOnlyList<Endpoint> Microphones { get; }

    /// <summary>Available playback devices.</summary>
    public IReadOnlyList<Endpoint> Outputs { get; }

    private static IReadOnlyList<Endpoint> CopyEndpoints(IReadOnlyList<Endpoint> endpoints, string parameter)
    {
        var copy = DomainValues.Copy(endpoints, parameter);
        var identifiers = new HashSet<EndpointId>();
        var hasDefault = false;
        foreach (var endpoint in copy)
        {
            if (!identifiers.Add(endpoint.Id))
            {
                throw new ArgumentException("A device list must contain distinct endpoint IDs.", parameter);
            }

            if (hasDefault && endpoint.IsDefault)
            {
                throw new ArgumentException("A device list can have only one default endpoint.", parameter);
            }

            hasDefault |= endpoint.IsDefault;
        }

        return copy;
    }
}
