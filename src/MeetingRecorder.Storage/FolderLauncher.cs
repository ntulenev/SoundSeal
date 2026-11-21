using System.ComponentModel;
using System.Diagnostics;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Storage;

/// <summary>Opens the containing folder in the operating system shell.</summary>
public sealed class FolderLauncher : IFolderLauncher
{
    /// <inheritdoc/>
    public void Open(RecordingFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        try
        {
            var directory = Path.GetDirectoryName(file.Value);
            if (!Directory.Exists(directory))
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
