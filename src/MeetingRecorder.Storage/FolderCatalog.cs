using System.Security;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Storage;

/// <summary>Lists immediate children and logical drives on a filesystem worker.</summary>
public sealed class FolderCatalog : IFolderCatalog
{
    /// <inheritdoc/>
    public Task<FolderListing> ListAsync(string? path, FolderLookup lookup, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(lookup))
        {
            throw new ArgumentOutOfRangeException(nameof(lookup));
        }

        return Task.Run(() => List(path, lookup, cancellationToken), cancellationToken);
    }

    private static FolderListing List(string? path, FolderLookup lookup, CancellationToken cancellationToken)
    {
        try
        {
            var directory = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
            while (lookup == FolderLookup.NearestExisting && directory is not null && !Directory.Exists(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                directory = Path.GetDirectoryName(directory);
            }

            if (directory is null)
            {
                return new(null, [.. Directory.GetLogicalDrives().Select(FolderEntryName.FromString)]);
            }

            var names = Directory.EnumerateDirectories(directory)
                .Select(entry =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Path.GetFileName(entry);
                })
                .Order(StringComparer.OrdinalIgnoreCase).Select(FolderEntryName.FromString).ToArray();
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
