namespace MeetingRecorder.Domain;

/// <summary>An actionable English error suitable for display to the recorder user.</summary>
public class RecordingException : Exception
{
    /// <summary>Creates a recording failure with a generic message.</summary>
    public RecordingException() : base("An audio recording operation failed.") { }

    /// <summary>Creates a failure with a user-facing explanation.</summary>
    public RecordingException(string message) : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }

    /// <summary>Preserves the underlying device or filesystem failure.</summary>
    public RecordingException(string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
