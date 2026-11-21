using System.Buffers.Binary;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Commits chronological blocks, filling missing samples on either track with zero.</summary>
internal sealed class IntervalMixer
{
    private const int BLOCK = 320;
    private readonly float[] _micBuffer = new float[BLOCK];
    private readonly float[] _outputBuffer = new float[BLOCK];
    private readonly byte[] _pcm = new byte[BLOCK * 2];
    private readonly IPcmSink _file;
    private readonly Action<Levels> _meter;
    private readonly SourceTrack _mic;
    private readonly SourceTrack _output;
    private readonly long _start;
    private long _written;

    /// <summary>Aligns both tracks to the same clock origin and writes sequential PCM blocks.</summary>
    internal IntervalMixer(CaptureTimestamp start, IPcmSink file, Action<Levels> meter)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(meter);
        _start = start.Ticks;
        _file = file;
        _meter = meter;
        _mic = new(start.Ticks);
        _output = new(start.Ticks);
    }

    /// <summary>Routes a device packet to its corresponding source timeline.</summary>
    internal void Add(AudioPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        (packet.Loopback ? _output : _mic).Add(packet, _written);
    }

    /// <summary>Writes complete output frames up to the exclusive clock watermark.</summary>
    internal void Drain(long boundary)
    {
        var frames = new ActiveInterval(_start, Math.Max(_start, boundary)).Frames.Value;
        while (_written < frames)
        {
            var count = (int)Math.Min(BLOCK, frames - _written);
            _mic.Read(_written, _micBuffer.AsSpan(0, count));
            _output.Read(_written, _outputBuffer.AsSpan(0, count));
            var micPeak = 0f;
            var outPeak = 0f;
            for (var i = 0; i < count; i++)
            {
                var mic = _micBuffer[i];
                var output = _outputBuffer[i];
                micPeak = Math.Max(micPeak, Math.Abs(mic));
                outPeak = Math.Max(outPeak, Math.Abs(output));
                var sample = (short)Math.Clamp(MathF.Round((mic + output) * 32768),
                    short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(_pcm.AsSpan(i * 2), sample);
            }

            _file.Append(new Pcm16Block(_pcm.AsSpan(0, count * sizeof(short))));
            _meter(new(micPeak, outPeak));
            _written += count;
        }
    }
}
