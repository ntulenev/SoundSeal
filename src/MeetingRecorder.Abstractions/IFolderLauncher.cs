using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Opens recording directories in the operating system shell.</summary>
public interface IFolderLauncher
{
    /// <summary>Opens the parent directory of a fully qualified recording path.</summary>
    void Open(RecordingFile file);

    /// <summary>Creates the selected recording directory if needed and opens it.</summary>
    void OpenDestination(RecordingFolder folder);
}
