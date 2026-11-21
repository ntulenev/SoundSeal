namespace MeetingRecorder.Domain;

/// <summary>The fully qualified path of a published or recoverable recording.</summary>
public sealed record RecordingFile
{
    /// <summary>Creates an absolute file path with a final filename component.</summary>
    public RecordingFile(string value)
    {
        DomainValues.RequireFullPath(value, nameof(value));
        if (string.IsNullOrEmpty(Path.GetFileName(value)) || Path.GetFileName(value) is "." or "..")
        {
            throw new ArgumentException("A recording path must end with a file name.", nameof(value));
        }

        Value = value;
    }

    /// <summary>The fully qualified path of a published or recoverable recording.</summary>
    public string Value { get; }

    /// <summary>Converts an optional value and preserves absence.</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(value))]
    public static RecordingFile? FromString(string? value) => value is null ? null : new(value);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Converts an optional value and preserves absence.</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(value))]
    public static implicit operator RecordingFile?(string? value) => FromString(value);
}
