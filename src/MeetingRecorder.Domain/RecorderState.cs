namespace MeetingRecorder.Domain;

/// <summary>States of one recorder, including exclusive transition states.</summary>
public enum RecorderState
{
    /// <summary>Waiting for a new recording.</summary>
    Ready,

    /// <summary>Opening the selected endpoints.</summary>
    Starting,

    /// <summary>Capturing samples from the pinned endpoints.</summary>
    Recording,

    /// <summary>Draining the active interval.</summary>
    Pausing,

    /// <summary>Retaining the output file between intervals.</summary>
    Paused,

    /// <summary>Reopening the original endpoints.</summary>
    Resuming,

    /// <summary>Finalizing the output file.</summary>
    Saving,

    /// <summary>The final recording is available.</summary>
    Saved,

    /// <summary>Recording or publication was interrupted.</summary>
    Error
}
