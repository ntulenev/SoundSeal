namespace MeetingRecorder.Domain;

/// <summary>A fully qualified destination directory.</summary>
public sealed record RecordingFolder
{
    /// <summary>Creates an absolute destination path without accessing the filesystem.</summary>
    public RecordingFolder(string value)
    {
        DomainValues.RequireFullPath(value, nameof(value));
        Value = value;
    }

    /// <summary>A fully qualified destination directory.</summary>
    public string Value { get; }

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static RecordingFolder FromString(string value) => new(value);

    /// <summary>Parses editable folder text without requiring the directory to exist.</summary>
    public static bool TryParse(string? text,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RecordingFolder? folder)
    {
        folder = null;
        if (string.IsNullOrWhiteSpace(text) || !Path.IsPathFullyQualified(text))
        {
            return false;
        }

        try
        {
            folder = new RecordingFolder(text);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static implicit operator RecordingFolder(string value) => FromString(value);
}
