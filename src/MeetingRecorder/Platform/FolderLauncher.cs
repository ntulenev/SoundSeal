using System.ComponentModel;
using System.Diagnostics;
using System.IO;

using MeetingRecorder.Domain;

namespace MeetingRecorder.Platform;

internal sealed class FolderLauncher : IFolderLauncher
{
    public void Open(string file)
    {
        try
        {
            var directory = Path.GetDirectoryName(file);
            if (directory is null || !Directory.Exists(directory))
            {
                throw new RecordingException("The recording folder is no longer available.");
            }

            using var process = Process.Start(new ProcessStartInfo(directory)
            {
                UseShellExecute = true
            });
        }
        catch (Win32Exception ex)
        {
            throw new RecordingException("Windows could not open the recording folder.", ex);
        }
    }
}
