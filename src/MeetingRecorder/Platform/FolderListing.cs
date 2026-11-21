namespace MeetingRecorder.Platform;

/// <summary>A directory and its immediate children, or drive names at the computer root.</summary>
internal sealed record FolderListing(string? Directory, IReadOnlyList<string> Names);
