using NAudio.CoreAudioApi;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>Thread-affine access to a WASAPI capture buffer borrowed from its audio client.</summary>
internal interface IWasapiBuffer
{
    /// <summary>Returns the size of the next available native packet in frames.</summary>
    int GetNextPacketSize();

    /// <summary>Acquires native frames. Position is in device frames, QPC in 100 ns ticks.
    /// A nonempty packet remains valid until the matching release on this thread.</summary>
    nint Acquire(out int frames, out AudioClientBufferFlags flags,
        out long position, out long qpc);

    /// <summary>Releases the packet on the thread that acquired it.</summary>
    void Release(int frames);
}
