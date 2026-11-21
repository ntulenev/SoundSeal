namespace MeetingRecorder.Domain;

/// <summary>Selection pinned for a recording session.</summary>
public sealed record RecordingSettings
{
    /// <summary>Creates a validated selection pinned for a recording session.</summary>
    /// <param name="microphoneId">Selected capture endpoint ID.</param>
    /// <param name="outputId">Selected playback endpoint ID.</param>
    /// <param name="folder">Fully qualified destination directory.</param>
    /// <param name="mode">Sources required for this recording.</param>
    public RecordingSettings(EndpointId? microphoneId, EndpointId? outputId, RecordingFolder folder,
        RecordingMode mode = RecordingMode.Meeting)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (mode != RecordingMode.Audio)
        {
            ArgumentNullException.ThrowIfNull(microphoneId);
        }

        if (mode != RecordingMode.Dictaphone)
        {
            ArgumentNullException.ThrowIfNull(outputId);
        }
        ArgumentNullException.ThrowIfNull(folder);

        Mode = mode;
        MicrophoneId = mode == RecordingMode.Audio ? null : microphoneId;
        OutputId = mode == RecordingMode.Dictaphone ? null : outputId;
        Folder = folder;
    }

    /// <summary>Capture mode pinned across pauses.</summary>
    public RecordingMode Mode { get; }

    /// <summary>Selected capture endpoint ID.</summary>
    public EndpointId? MicrophoneId { get; }

    /// <summary>Selected playback endpoint ID.</summary>
    public EndpointId? OutputId { get; }

    /// <summary>Fully qualified destination directory.</summary>
    public RecordingFolder Folder { get; }
}
