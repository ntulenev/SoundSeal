namespace MeetingRecorder.Domain;

/// <summary>States of one recorder, including exclusive transition states.</summary>
public enum RecorderState
{
    Ready, Starting, Recording, Pausing, Paused, Resuming, Saving, Saved, Error
}
