namespace MeetingRecorder.Platform;

/// <summary>Opens the directory containing a saved or recoverable recording.</summary>
internal interface IFolderLauncher
{
    void Open(string file);
}
