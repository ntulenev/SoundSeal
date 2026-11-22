using System.ComponentModel;
using System.Diagnostics;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Storage;

/// <summary>Opens the containing folder in the operating system shell.</summary>
public sealed class FolderLauncher : IFolderLauncher
{
    private readonly Action<string> _launch;

    /// <summary>Uses the Windows shell to open recording folders.</summary>
    public FolderLauncher() : this(Launch) { }

    internal FolderLauncher(Action<string> launch)
    {
        _launch = launch;
    }

    /// <inheritdoc/>
    public void Open(RecordingFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var directory = Path.GetDirectoryName(file.Value);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new RecordingException("The recording folder is no longer available.");
        }
        OpenDirectory(directory);
    }

    /// <inheritdoc/>
    public void OpenDestination(RecordingFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        try
        {
            _ = Directory.CreateDirectory(folder.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new RecordingException("Cannot open the recording folder. Check the path and your access permissions.", ex);
        }
        OpenDirectory(folder.Value);
    }

    private void OpenDirectory(string directory)
    {
        try
        {
            _launch(directory);
        }
        catch (Win32Exception ex)
        {
            throw new RecordingException("Windows could not open the recording folder.", ex);
        }
    }

    private static void Launch(string directory)
    {
        using var process = Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }
}
