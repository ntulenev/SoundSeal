namespace MeetingRecorder.Platform;

/// <summary>Enumerates folders without blocking the presentation dispatcher.</summary>
internal interface IFolderCatalog
{
    /// <summary>Lists children, optionally starting at the nearest existing ancestor.</summary>
    Task<FolderListing> ListAsync(string? path, bool nearestExisting);
}
