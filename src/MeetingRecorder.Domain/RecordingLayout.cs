namespace MeetingRecorder.Domain;

/// <summary>Publication schedule for a recording session.</summary>
public enum RecordingLayout
{
    /// <summary>Publish one file when the session finishes.</summary>
    SingleFile,

    /// <summary>Publish successive files every fifteen minutes of active audio.</summary>
    QuarterHourParts
}
