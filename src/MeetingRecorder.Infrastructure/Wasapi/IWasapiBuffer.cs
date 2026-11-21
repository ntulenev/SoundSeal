using NAudio.CoreAudioApi;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>Thread-affine access to a WASAPI capture buffer borrowed from its audio client.</summary>
internal interface IWasapiBuffer
{
    int GetNextPacketSize();

    /// <summary>Acquires native frames. Position is in device frames, QPC in 100 ns ticks.
    /// A nonempty packet remains valid until the matching release on this thread.</summary>
    nint Acquire(out int frames, out AudioClientBufferFlags flags,
        out long position, out long qpc);

    void Release(int frames);
}
