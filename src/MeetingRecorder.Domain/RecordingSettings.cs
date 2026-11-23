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
        : this(microphoneId, outputId, folder, mode, RecordingLayout.SingleFile) { }

    /// <summary>Pins the audio sources and file publication schedule for this session.</summary>
    public RecordingSettings(EndpointId? microphoneId, EndpointId? outputId, RecordingFolder folder,
        RecordingMode mode, RecordingLayout layout)
        : this(microphoneId, outputId, folder, mode, layout, RecordingFileFormat.Wav) { }

    /// <summary>Pins the sources, publication schedule and file encoding for this session.</summary>
    public RecordingSettings(EndpointId? microphoneId, EndpointId? outputId, RecordingFolder folder,
        RecordingMode mode, RecordingLayout layout, RecordingFileFormat format)
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

        if (!Enum.IsDefined(layout))
        {
            throw new ArgumentOutOfRangeException(nameof(layout));
        }

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        Format = format;
        Layout = layout;
        Mode = mode;
        MicrophoneId = mode == RecordingMode.Audio ? null : microphoneId;
        OutputId = mode == RecordingMode.Dictaphone ? null : outputId;
        Folder = folder;
    }

    /// <summary>File encoding used for every part of this session.</summary>
    public RecordingFileFormat Format { get; }

    /// <summary>File publication schedule pinned across pauses.</summary>
    public RecordingLayout Layout { get; }

    /// <summary>Capture mode pinned across pauses.</summary>
    public RecordingMode Mode { get; }

    /// <summary>Selected capture endpoint ID.</summary>
    public EndpointId? MicrophoneId { get; }

    /// <summary>Selected playback endpoint ID.</summary>
    public EndpointId? OutputId { get; }

    /// <summary>Fully qualified destination directory.</summary>
    public RecordingFolder Folder { get; }
}
