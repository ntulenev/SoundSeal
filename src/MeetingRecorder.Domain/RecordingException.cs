namespace MeetingRecorder.Domain;

/// <summary>An actionable English error suitable for display to the recorder user.</summary>
public class RecordingException : Exception
{
    public RecordingException() : base("An audio recording operation failed.") { }
    public RecordingException(string message) : base(message) { }
    public RecordingException(string message, Exception innerException)
        : base(message, innerException) { }
}
