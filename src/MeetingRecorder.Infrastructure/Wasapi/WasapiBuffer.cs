using NAudio.CoreAudioApi;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>Adapts the capture client owned and released by the parent AudioClient.</summary>
internal sealed class WasapiBuffer : IWasapiBuffer
{
    private readonly AudioCaptureClient _capture;

    internal WasapiBuffer(AudioCaptureClient capture)
    {
        _capture = capture;
    }

    public int GetNextPacketSize() => _capture.GetNextPacketSize();

    public nint Acquire(out int frames, out AudioClientBufferFlags flags,
        out long position, out long qpc) =>
        _capture.GetBuffer(out frames, out flags, out position, out qpc);

    public void Release(int frames) => _capture.ReleaseBuffer(frames);
}
