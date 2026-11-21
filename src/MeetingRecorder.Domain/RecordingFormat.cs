namespace MeetingRecorder.Domain;

/// <summary>The on-disk format is little-endian signed mono PCM16.</summary>
public static class RecordingFormat
{
    public static int SampleRate => 16_000;
    public static long TicksPerFrame => TimeSpan.TicksPerSecond / SampleRate;

    /// <summary>Converts complete mono frames to their exact playing time.</summary>
    /// <param name="frames">Number of complete 16 kHz PCM frames in the data chunk.</param>
    /// <returns>The playing time encoded by the file header.</returns>
    /// <exception cref="OverflowException">The duration exceeds the tick range.</exception>
    public static TimeSpan Duration(long frames) =>
        TimeSpan.FromTicks(checked(frames * TicksPerFrame));
}
