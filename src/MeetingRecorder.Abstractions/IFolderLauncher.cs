using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Opens the directory containing a saved or recoverable recording.</summary>
public interface IFolderLauncher
{
    /// <summary>Opens the parent directory of a fully qualified recording path.</summary>
    void Open(RecordingFile file);
}
