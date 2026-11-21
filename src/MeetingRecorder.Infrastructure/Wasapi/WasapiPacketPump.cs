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

    internal WasapiPacketPump(IWasapiBuffer buffer, InputFormat format, bool loopback)
    {
        _buffer = buffer;
        _format = format;
        _loopback = loopback;
    }

    internal void Drain(Action<AudioPacket> accept)
    {
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
        if (pointer == 0 || frames < 0 || frames > Array.MaxLength / _format.Stride)
        {
            throw new RecordingException("The audio device returned an invalid capture buffer.");
        }

        var bytes = new byte[frames * _format.Stride];
        Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return bytes;
    }
}
