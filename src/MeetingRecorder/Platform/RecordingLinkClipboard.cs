using System.IO;
using System.Runtime.InteropServices;

using MeetingRecorder.Domain;

namespace MeetingRecorder.Platform;

/// <summary>Copies a local or network recording path as an escaped file URI.</summary>
internal sealed class RecordingLinkClipboard(Action<string> setText)
{
    internal void Copy(string file)
    {
        var path = Path.GetFullPath(file);
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
            setText(uri.AbsoluteUri);
        }
        catch (ExternalException ex)
        {
            throw new RecordingException("Could not access the clipboard. Try copying the link again.", ex);
        }
    }
}
