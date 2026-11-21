using System.Runtime.InteropServices;

using FluentAssertions;

using MeetingRecorder.Domain;
using MeetingRecorder.Platform;

namespace MeetingRecorder.Tests;

/// <summary>Tests clipboard file URI formatting.</summary>
public sealed class RecordingLinkClipboardTests
{
    /// <summary>Clipboard links preserve the recording's Windows path.</summary>
    [Trait("Category", "Unit")]
    [Theory(DisplayName = "Clipboard links preserve the recording's Windows path")]
    [InlineData(@"C:\Recordings\Meetings #1\100%20 🎙.wav", "file:///C:/Recordings/Meetings%20%231/100%2520%20%F0%9F%8E%99.wav")]
    [InlineData(@"\\studio\shared audio\sample.wav", "file://studio/shared%20audio/sample.wav")]
    [InlineData(@"\\?\C:\Recordings\sample.wav", "file:///C:/Recordings/sample.wav")]
    [InlineData(@"\\?\UNC\studio\audio\sample.wav", "file://studio/audio/sample.wav")]
    public void CopyShouldWriteEscapedFileLink(string path, string expected)
    {
        string? written = null;
        new RecordingLinkClipboard(text => written = text).Copy(path);
        written.Should().Be(expected);
    }

    /// <summary>A busy clipboard can be retried.</summary>
    [Trait("Category", "Unit")]
    [Fact(DisplayName = "A busy clipboard can be retried")]
    public void CopyShouldExplainClipboardFailure()
    {
        var busy = true;
        string? written = null;
        var clipboard = new RecordingLinkClipboard(text =>
        {
            if (busy)
            {
                Marshal.ThrowExceptionForHR(unchecked((int)0x800401D0)); // CLIPBRD_E_CANT_OPEN
            }

            written = text;
        });
        var copy = () => clipboard.Copy(@"C:\Recordings\meeting.wav");
        copy.Should().Throw<RecordingException>().WithMessage("*Try copying the link again.*");
        busy = false;
        copy();
        written.Should().Be("file:///C:/Recordings/meeting.wav");
    }
}
