using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Enumerates folders without blocking the presentation dispatcher.</summary>
public interface IFolderCatalog
{
    /// <summary>Lists children, optionally starting at the nearest existing ancestor.</summary>
    /// <param name="path">Editable path text, or null to list drives.</param>
    /// <param name="lookup">How to handle a directory that does not exist.</param>
    /// <param name="cancellationToken">Cancels pending enumeration.</param>
    /// <returns>The resolved directory and an immutable snapshot of its entries.</returns>
    Task<FolderListing> ListAsync(string? path, FolderLookup lookup, CancellationToken cancellationToken);
}
