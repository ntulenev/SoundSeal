using System.Buffers.Binary;
using System.Numerics;

using MeetingRecorder.Domain;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>One endpoint's native sample layout and signed, normalized channel gains.</summary>
internal sealed class InputFormat
{
    private readonly float[] _weights;
    private readonly bool _floating;
    private readonly int _bytes;

    internal InputFormat(int rate, int channels, int bits, bool floating, int speakerMask)
    {
        if (rate is < 8000 or > 384000 || channels is < 1 or > 32 ||
            (floating ? bits is not (32 or 64) : bits is not (8 or 16 or 24 or 32)))
        {
            throw new RecordingException("The selected device uses an unsupported audio format.");
        }

        Rate = rate;
        _floating = floating;
        _bytes = bits / 8;
        Stride = channels * _bytes;
        _weights = new float[channels];
        var mask = (uint)speakerMask;
        for (var i = 0; i < channels; i++)
        {
            var role = BitOperations.TrailingZeroCount(mask);
            _weights[i] = role switch
            {
                3 => 0, // Low-frequency effects
                2 => 1, // Front center, usually dialogue
                0 or 1 or 32 => 1,
                _ => 0.7071f
            };
            mask &= mask - 1;
        }

        var total = Math.Max(1, _weights.Sum());
        for (var i = 0; i < channels; i++)
        {
            _weights[i] /= total;
        }
    }

    internal int Rate { get; }
    internal int Stride { get; }

    internal float Decode(ReadOnlySpan<byte> frame)
    {
        var mono = 0f;
        foreach (var weight in _weights)
        {
            var sample = (_floating, _bytes) switch
            {
                (true, 4) => BinaryPrimitives.ReadSingleLittleEndian(frame),
                (true, 8) => (float)BinaryPrimitives.ReadDoubleLittleEndian(frame),
                (false, 1) => (frame[0] - 128) / 128f,
                (false, 2) => BinaryPrimitives.ReadInt16LittleEndian(frame) / 32768f,
                (false, 3) => ((frame[0] << 8) | (frame[1] << 16) | (frame[2] << 24))
                    / 2147483648f,
                _ => BinaryPrimitives.ReadInt32LittleEndian(frame) / 2147483648f
            };
            if (float.IsFinite(sample))
            {
                mono += weight * Math.Clamp(sample, -1, 1);
            }

            frame = frame[_bytes..];
        }

        return mono * 0.48f;
    }
}
