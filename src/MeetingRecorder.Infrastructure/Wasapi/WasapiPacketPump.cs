using System.Runtime.InteropServices;

using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;

using NAudio.CoreAudioApi;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>Transfers packet ownership from WASAPI memory to the recorder's bounded queue.</summary>
internal sealed class WasapiPacketPump
{
    private readonly IWasapiBuffer _buffer;
    private readonly InputFormat _format;
    private readonly bool _loopback;

    /// <summary>Reads one native capture buffer with its negotiated layout and device role.</summary>
    internal WasapiPacketPump(IWasapiBuffer buffer, InputFormat format, bool loopback)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(format);
        _buffer = buffer;
        _format = format;
        _loopback = loopback;
    }

    /// <summary>Copies pending packets and releases each buffer before delivering the packet.</summary>
    internal void Drain(Action<AudioPacket> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        while (_buffer.GetNextPacketSize() > 0)
        {
            var pointer = _buffer.Acquire(out var frames, out var flags,
                out var position, out var qpc);
            if (frames == 0)
            {
                break;
            }

            byte[] bytes;
            try
            {
                if (frames < 0 || frames > Array.MaxLength / _format.Stride)
                {
                    throw new RecordingException("The audio device returned an invalid capture buffer.");
                }

                bytes = flags.HasFlag(AudioClientBufferFlags.Silent)
                    ? [] : CopyFrames(pointer, frames);
            }
            finally
            {
                _buffer.Release(frames);
            }

            accept(new(_loopback, _format, position,
                flags.HasFlag(AudioClientBufferFlags.TimestampError) ? null : qpc,
                frames, bytes, flags.HasFlag(AudioClientBufferFlags.DataDiscontinuity)));
        }
    }

    private byte[] CopyFrames(nint pointer, int frames)
    {
        if (pointer == 0)
        {
            throw new RecordingException("The audio device returned an invalid capture buffer.");
        }

        var bytes = new byte[frames * _format.Stride];
        Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return bytes;
    }
}
