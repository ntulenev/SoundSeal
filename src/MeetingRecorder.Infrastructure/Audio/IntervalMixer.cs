using System.Buffers.Binary;

using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Files;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Commits chronological blocks, filling missing samples on either track with zero.</summary>
internal sealed class IntervalMixer
{
    private const int BLOCK = 320;
    private readonly float[] _micBuffer = new float[BLOCK];
    private readonly float[] _outputBuffer = new float[BLOCK];
    private readonly byte[] _pcm = new byte[BLOCK * 2];
    private readonly WaveDestination _file;
    private readonly Action<Levels> _meter;
    private readonly SourceTrack _mic;
    private readonly SourceTrack _output;
    private readonly long _start;
    private long _written;

    internal IntervalMixer(long start, WaveDestination file, Action<Levels> meter)
    {
        _start = start;
        _file = file;
        _meter = meter;
        _mic = new(start);
        _output = new(start);
    }

    internal void Add(AudioPacket packet) =>
        (packet.Loopback ? _output : _mic).Add(packet, _written);

    internal void Drain(long boundary)
    {
        var frames = new ActiveInterval(_start, boundary).Frames;
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

            _file.Append(_pcm.AsSpan(0, count * 2));
            _meter(new(micPeak, outPeak));
            _written += count;
        }
    }
}
