using System.IO;
using System.Runtime.InteropServices;

using MeetingRecorder.Domain;

namespace MeetingRecorder.Platform;

/// <summary>Copies a local or network recording path as an escaped file URI.</summary>
internal sealed class RecordingLinkClipboard
{
    private readonly Action<string> _setText;

    /// <summary>Creates a clipboard writer for escaped file links.</summary>
    internal RecordingLinkClipboard(Action<string> setText)
    {
        ArgumentNullException.ThrowIfNull(setText);
        _setText = setText;
    }

    /// <summary>Writes the published recording as an absolute file URI.</summary>
    internal void Copy(RecordingFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var path = Path.GetFullPath(file.Value);
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            path = @"\\" + path[8..];
        }
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            path = path[4..];
        }

        var prefix = path.StartsWith(@"\\", StringComparison.Ordinal) ? "file:" : "file:///";
        // Escape literal percent signs before Uri interprets them as escape sequences.
        var escaped = path.Replace('\\', '/').Replace("%", "%25", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal);
        if (!Uri.TryCreate(prefix + escaped, UriKind.Absolute, out var uri))
        {
            throw new RecordingException("Could not create a file link for this recording path.");
        }

        try
        {
            _setText(uri.AbsoluteUri);
        }
        catch (ExternalException ex)
        {
            throw new RecordingException("Could not access the clipboard. Try copying the link again.", ex);
        }
    }
}
