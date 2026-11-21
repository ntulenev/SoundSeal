namespace MeetingRecorder.Domain;

/// <summary>Snapshot of a directory listing.</summary>
public sealed record FolderListing
{
    /// <summary>Creates a validated snapshot of a directory listing.</summary>
    /// <param name="directory">Current folder, or null for the drive list.</param>
    /// <param name="names">Child folders, or drive names at the computer root.</param>
    public FolderListing(RecordingFolder? directory, IReadOnlyList<FolderEntryName> names)
    {
        Directory = directory;
        Names = DomainValues.Copy(names, nameof(names));
        if (Names.Any(entry => entry.IsRoot != (directory is null)))
        {
            throw new ArgumentException("The drive list contains roots and a folder listing contains child names.", nameof(names));
        }
    }

    /// <summary>Current folder, or null for the drive list.</summary>
    public RecordingFolder? Directory { get; }

    /// <summary>Child folders, or drive names at the computer root.</summary>
    public IReadOnlyList<FolderEntryName> Names { get; }
}
