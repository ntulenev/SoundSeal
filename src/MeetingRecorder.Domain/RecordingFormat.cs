namespace MeetingRecorder.Domain;

/// <summary>The on-disk format is little-endian signed mono PCM16.</summary>
public static class RecordingFormat
{
    /// <summary>Output frames per second for mono PCM16.</summary>
    public static int SampleRate => 16_000;

    /// <summary>100 ns clock ticks occupied by one output frame.</summary>
    public static long TicksPerFrame => TimeSpan.TicksPerSecond / SampleRate;

    /// <summary>Converts complete mono frames to their exact playing time.</summary>
    /// <param name="frames">Number of complete 16 kHz PCM frames in the data chunk.</param>
    /// <returns>The playing time encoded by the file header.</returns>
    public static RecordingDuration Duration(FrameCount frames) => TimeSpan.FromTicks(checked(frames.Value * TicksPerFrame));
}
