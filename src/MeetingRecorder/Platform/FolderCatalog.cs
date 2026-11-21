using System.IO;
using System.Security;

using MeetingRecorder.Domain;

namespace MeetingRecorder.Platform;

internal sealed class FolderCatalog : IFolderCatalog
{
    public Task<FolderListing> ListAsync(string? path, bool nearestExisting) =>
        Task.Run(() => List(path, nearestExisting));

    private static FolderListing List(string? path, bool nearestExisting)
    {
        try
        {
            var directory = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
            while (nearestExisting && directory is not null && !Directory.Exists(directory))
            {
                directory = Path.GetDirectoryName(directory);
            }

            if (directory is null)
            {
                return new(null, Directory.GetLogicalDrives());
            }

            var names = Directory.EnumerateDirectories(directory)
                .Select(entry => Path.GetFileName(entry))
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return new(directory, names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or SecurityException)
        {
            throw new RecordingException("This folder could not be opened. " +
                "Check the path and your access.", ex);
        }
    }
}
