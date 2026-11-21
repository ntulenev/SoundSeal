using NAudio.CoreAudioApi;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>Adapts the capture client owned and released by the parent AudioClient.</summary>
internal sealed class WasapiBuffer : IWasapiBuffer
{
    private readonly AudioCaptureClient _capture;

    /// <summary>Adapts the native capture client to the packet pump.</summary>
    internal WasapiBuffer(AudioCaptureClient capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
    }

    /// <inheritdoc/>
    public int GetNextPacketSize() => _capture.GetNextPacketSize();

    /// <inheritdoc/>
    public nint Acquire(out int frames, out AudioClientBufferFlags flags,
        out long position, out long qpc) =>
        _capture.GetBuffer(out frames, out flags, out position, out qpc);

    /// <inheritdoc/>
    public void Release(int frames) => _capture.ReleaseBuffer(frames);
}
