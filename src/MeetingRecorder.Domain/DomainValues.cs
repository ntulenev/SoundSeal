namespace MeetingRecorder.Domain;

/// <summary>Shared construction checks for immutable domain values.</summary>
internal static class DomainValues
{
    /// <summary>Requires an absolute path accepted by the platform path parser.</summary>
    internal static void RequireFullPath(string? path, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameter);
        _ = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A fully qualified path is required.", parameter);
        }
    }

    /// <summary>Copies a collection and rejects missing entries before exposing it.</summary>
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source, string parameter)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source, parameter);
        var copy = source.ToArray();
        if (Array.IndexOf(copy, null) >= 0)
        {
            throw new ArgumentException("A snapshot cannot contain null entries.", parameter);
        }

        return Array.AsReadOnly(copy);
    }
}
