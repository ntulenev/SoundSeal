namespace MeetingRecorder.Domain;

/// <summary>A single child folder name or a drive root shown in the folder chooser.</summary>
public sealed record FolderEntryName
{
    /// <summary>Rejects traversal, paths with multiple components, and empty labels.</summary>
    public FolderEntryName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var root = Path.GetPathRoot(value);
        IsRoot = Path.IsPathFullyQualified(value) && root == value;
        if (!IsRoot && (value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new ArgumentException("A folder entry must be a drive root or a single folder name.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Whether this entry identifies a drive root.</summary>
    public bool IsRoot { get; }

    /// <summary>Name returned by the directory listing.</summary>
    public string Value { get; }

    /// <summary>Validates a directory entry from the filesystem.</summary>
    public static FolderEntryName FromString(string value) => new(value);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <inheritdoc cref="FromString"/>
    public static implicit operator FolderEntryName(string value) => FromString(value);
}
