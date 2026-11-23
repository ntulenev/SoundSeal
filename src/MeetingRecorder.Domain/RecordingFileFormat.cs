namespace MeetingRecorder.Domain;

/// <summary>On-disk encoding selected before a session starts.</summary>
public enum RecordingFileFormat
{
    /// <summary>Mono MPEG Layer III at 48 kbit/s.</summary>
    Mp3,

    /// <summary>Uncompressed 16-bit PCM in a RIFF WAVE container.</summary>
    Wav
}
