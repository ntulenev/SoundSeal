using System.Buffers.Binary;
using System.Numerics;

using MeetingRecorder.Domain;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>One endpoint's native sample layout and signed, normalized channel gains.</summary>
internal sealed class InputFormat
{
    private const float TRACK_GAIN = 0.48f;
    private readonly float[] _weights;
    private readonly SampleEncoding _encoding;
    private readonly int _bytes;

    /// <summary>Negotiates a native sample layout against the configured limits.</summary>
    internal InputFormat(int rate, int channels, int bits, bool floating, int speakerMask,
        AudioCaptureOptions? options = null)
    {
        var policy = options ?? AudioCaptureOptions.Default;
        _encoding = (floating, bits) switch
        {
            (false, 8) => SampleEncoding.Pcm8,
            (false, 16) => SampleEncoding.Pcm16,
            (false, 24) => SampleEncoding.Pcm24,
            (false, 32) => SampleEncoding.Pcm32,
            (true, 32) => SampleEncoding.Float32,
            (true, 64) => SampleEncoding.Float64,
            _ => throw new RecordingException("The selected device uses an unsupported audio format.")
        };
        if (rate < policy.MinimumSampleRate || rate > policy.MaximumSampleRate
            || channels < 1 || channels > policy.MaximumChannels ||
            !policy.AllowedEncodings.Contains(_encoding))
        {
            throw new RecordingException("The selected device uses an unsupported audio format.");
        }

        Rate = rate;
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

    /// <summary>Native frames per second.</summary>
    internal int Rate { get; }

    /// <summary>Bytes occupied by a complete interleaved native frame.</summary>
    internal int Stride { get; }

    /// <summary>Decodes and downmixes the first native frame at the fixed track gain.</summary>
    internal float Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < Stride)
        {
            throw new ArgumentException("A complete source frame is required.", nameof(frame));
        }

        var mono = 0f;
        foreach (var weight in _weights)
        {
            var sample = _encoding switch
            {
                SampleEncoding.Float32 => BinaryPrimitives.ReadSingleLittleEndian(frame),
                SampleEncoding.Float64 => (float)BinaryPrimitives.ReadDoubleLittleEndian(frame),
                SampleEncoding.Pcm8 => (frame[0] - 128) / 128f,
                SampleEncoding.Pcm16 => BinaryPrimitives.ReadInt16LittleEndian(frame) / 32768f,
                SampleEncoding.Pcm24 => ((frame[0] << 8) | (frame[1] << 16) | (frame[2] << 24))
                    / 2147483648f,
                SampleEncoding.Pcm32 => BinaryPrimitives.ReadInt32LittleEndian(frame) / 2147483648f,
                _ => throw new InvalidOperationException("Unrecognized sample encoding.")
            };
            if (float.IsFinite(sample))
            {
                mono += weight * Math.Clamp(sample, -1, 1);
            }

            frame = frame[_bytes..];
        }

        return mono * TRACK_GAIN;
    }
}
