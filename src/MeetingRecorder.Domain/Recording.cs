namespace MeetingRecorder.Domain;

/// <summary>States of one recorder, including exclusive transition states.</summary>
public enum RecorderState
{
    Ready, Starting, Recording, Pausing, Paused, Resuming, Saving, Saved, Error
}

/// <summary>A half-open interval on the WASAPI QPC timeline, in 100 ns units.</summary>
/// <param name="Start">Inclusive start on the shared performance-counter timeline.</param>
/// <param name="End">Exclusive pause or finish boundary on the same timeline.</param>
public readonly record struct ActiveInterval(long Start, long End)
{
    public long Ticks => Math.Max(0, End - Start);
    public long Frames => Ticks / RecordingFormat.TicksPerFrame;
}

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

/// <summary>Stable endpoint identity and the verbatim name supplied by its driver.</summary>
public sealed record Endpoint(string Id, string Name, bool IsDefault);

/// <summary>Active capture and render endpoints at the time of enumeration.</summary>
public sealed record Devices(IReadOnlyList<Endpoint> Microphones,
    IReadOnlyList<Endpoint> Outputs);

/// <summary>The endpoint IDs and destination pinned for the entire recording.</summary>
public sealed record RecordingSettings(string MicrophoneId, string OutputId, string Folder);

/// <summary>Independent post-gain peaks from samples written to the file.</summary>
public readonly record struct Levels(float Microphone, float Computer);

/// <summary>Outcome after all handles close. An unfinished path requires recovery.</summary>
/// <param name="Path">Published WAV or retained temporary file, if available.</param>
/// <param name="Frames">Number of successfully written mono frames.</param>
/// <param name="Problem">English interruption or storage error for the user.</param>
/// <param name="Published">True only after the final name has been published.</param>
public sealed record RecordingResult(string? Path, long Frames, string? Problem,
    bool Published);

/// <summary>An immutable state snapshot. The running interval starts at QPC Started.</summary>
public sealed record RecorderSnapshot(RecorderState State, long ActiveTicks = 0,
    long? Started = null, RecordingResult? Result = null)
{
    /// <summary>Sums closed intervals and the current interval, including silence.</summary>
    /// <param name="now">Current QPC reading expressed in 100 ns units.</param>
    /// <returns>Active time, excluding all completed and current user pauses.</returns>
    public TimeSpan Elapsed(long now) => TimeSpan.FromTicks(
        ActiveTicks + (Started.HasValue ? new ActiveInterval(Started.Value, now).Ticks : 0));
}

/// <summary>An actionable English error suitable for display to the recorder user.</summary>
public class RecordingException : Exception
{
    public RecordingException() : base("An audio recording operation failed.") { }
    public RecordingException(string message) : base(message) { }
    public RecordingException(string message, Exception innerException)
        : base(message, innerException) { }
}
