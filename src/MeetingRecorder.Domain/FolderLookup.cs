namespace MeetingRecorder.Domain;

/// <summary>Determines where folder browsing begins for a path entered by the user.</summary>
public enum FolderLookup
{
    /// <summary>List exactly the requested folder or report the access error.</summary>
    Exact,

    /// <summary>Walk up to the first existing directory before listing its children.</summary>
    NearestExisting
}
