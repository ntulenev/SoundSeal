namespace MeetingRecorder.Domain;

/// <summary>Selection pinned for a recording session.</summary>
public sealed record RecordingSettings
{
    /// <summary>Creates a validated selection pinned for a recording session.</summary>
    /// <param name="microphoneId">Selected capture endpoint ID.</param>
    /// <param name="outputId">Selected playback endpoint ID.</param>
    /// <param name="folder">Fully qualified destination directory.</param>
    public RecordingSettings(EndpointId microphoneId, EndpointId outputId, RecordingFolder folder)
    {
        ArgumentNullException.ThrowIfNull(microphoneId);
        ArgumentNullException.ThrowIfNull(outputId);
        ArgumentNullException.ThrowIfNull(folder);

        MicrophoneId = microphoneId;
        OutputId = outputId;
        Folder = folder;
    }

    /// <summary>Selected capture endpoint ID.</summary>
    public EndpointId MicrophoneId { get; }

    /// <summary>Selected playback endpoint ID.</summary>
    public EndpointId OutputId { get; }

    /// <summary>Fully qualified destination directory.</summary>
    public RecordingFolder Folder { get; }
}
