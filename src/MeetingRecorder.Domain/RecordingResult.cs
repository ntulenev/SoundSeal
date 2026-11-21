namespace MeetingRecorder.Domain;

/// <summary>Outcome after all handles close. An unfinished path requires recovery.</summary>
/// <param name="Path">Published WAV or retained temporary file, if available.</param>
/// <param name="Frames">Number of successfully written mono frames.</param>
/// <param name="Problem">English interruption or storage error for the user.</param>
/// <param name="Published">True only after the final name has been published.</param>
public sealed record RecordingResult(string? Path, long Frames, string? Problem,
    bool Published);
