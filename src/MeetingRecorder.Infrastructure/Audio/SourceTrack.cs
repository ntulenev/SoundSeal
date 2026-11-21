using MeetingRecorder.Domain;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Maps a device clock to the shared timeline and resamples one post-gain track.</summary>
internal sealed class SourceTrack
{
    private const long MAX_TIMESTAMP_JUMP = 30 * TimeSpan.TicksPerMillisecond;
    private readonly Queue<(long Start, float[] Samples)> _segments = new();
    private readonly long _origin;
    private long _expectedPosition;
    private long _anchorPosition;
    private double _anchorTime;
    private double _nextTime;
    private double _rate;
    private bool _invalid;
    private double _s1, _s2, _s3, _s4;
    private double _b0, _b1, _b2, _a1, _a2;
    private float _previous;
    private long _nextOutput;

    /// <summary>Starts a resampled track at the shared interval origin.</summary>
    internal SourceTrack(long origin)
    {
        _origin = origin;
    }

    /// <summary>Aligns the packet against previously written output frames.</summary>
    internal void Add(AudioPacket packet, long written)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var initial = _rate == 0;
        var nominal = packet.Format.Rate;
        if (initial)
        {
            _rate = nominal;
            ConfigureFilter(nominal);
        }

        var gap = packet.Position != _expectedPosition;
        if (packet.Qpc is null && (initial || _invalid || gap))
        {
            throw new RecordingException("The device stopped providing reliable audio timestamps.");
        }

        _invalid = packet.Qpc is null;
        var time = packet.Qpc ?? _nextTime;
        var offset = time - _nextTime;
        var restart = initial || gap || Math.Abs(offset) > MAX_TIMESTAMP_JUMP;
        if (!initial && (packet.Position < _expectedPosition || offset < -MAX_TIMESTAMP_JUMP ||
            (!packet.Loopback && restart) ||
            (packet.Discontinuity && (!packet.Loopback || !restart))))
        {
            throw new RecordingException("Audio input was interrupted by a device discontinuity.");
        }

        if (restart)
        {
            _anchorPosition = packet.Position;
            _anchorTime = time;
            _s1 = _s2 = _s3 = _s4 = 0;
            _previous = 0;
            _nextOutput = (long)Math.Ceiling((time - _origin) / RecordingFormat.TicksPerFrame);
        }
        else
        {
            if (packet.Qpc.HasValue && time - _anchorTime >= TimeSpan.TicksPerSecond)
            {
                var measured = (packet.Position - _anchorPosition) * (double)TimeSpan.TicksPerSecond
                    / (time - _anchorTime);
                if (Math.Abs((measured / nominal) - 1) > 0.02)
                {
                    throw new RecordingException("The audio device clock could not be synchronized.");
                }

                _rate += (measured - _rate) * 0.2;
                _anchorPosition = packet.Position;
                _anchorTime = time;
            }

            // Follow reliable QPC readings without jumping at each packet boundary.
            time = _nextTime + Math.Clamp(offset * 0.08, -1000, 1000);
        }

        _expectedPosition = packet.Position + packet.Frames;
        var step = TimeSpan.TicksPerSecond / _rate;
        _nextTime = time + (packet.Frames * step);
        var end = (long)Math.Ceiling((_nextTime - _origin) / RecordingFormat.TicksPerFrame);
        var start = Math.Max(0, _nextOutput);
        _nextOutput = end;
        if (end <= 0)
        {
            return;
        }

        if (start < written && end > start)
        {
            throw new RecordingException("Audio packets arrived too late to synchronize the recording.");
        }

        if (packet.Data.Length == 0 && Math.Abs(_previous) < 1e-15
            && Math.Abs(_s1) + Math.Abs(_s2) + Math.Abs(_s3) + Math.Abs(_s4) < 1e-15)
        {
            _previous = 0;
            _s1 = _s2 = _s3 = _s4 = 0;
            return;
        }

        var samples = new float[Math.Max(0, end - start)];
        var output = start;
        for (var i = 0; i < packet.Frames; i++)
        {
            var sampleTime = time + (i * step);
            if (sampleTime < _origin)
            {
                continue;
            }

            var value = packet.Data.Length == 0 ? 0 :
                packet.Format.Decode(packet.Data.AsSpan(i * packet.Format.Stride));
            var filtered = Filter(value);
            var next = sampleTime + step;
            while (output < end)
            {
                var outputTime = _origin + (output * RecordingFormat.TicksPerFrame);
                if (outputTime >= next)
                {
                    break;
                }

                var fraction = (float)Math.Clamp((outputTime - sampleTime) / step, 0, 1);
                samples[output - start] = _previous + ((filtered - _previous) * fraction);
                output++;
            }

            _previous = filtered;
        }

        if (samples.Length > 0)
        {
            _segments.Enqueue((start, samples));
        }
    }

    /// <summary>Whether a buffered segment starts before the exclusive output-frame boundary.</summary>
    internal bool HasSamplesBefore(long end) => _segments.TryPeek(out var segment) && segment.Start < end;

    /// <summary>Fills the requested output slice with aligned samples and silence where needed.</summary>
    internal void Read(long start, Span<float> target)
    {
        target.Clear();
        var end = start + target.Length;
        while (_segments.TryPeek(out var segment) && segment.Start < end)
        {
            var from = Math.Max(start, segment.Start);
            var through = Math.Min(end, segment.Start + segment.Samples.Length);
            if (through > from)
            {
                segment.Samples.AsSpan((int)(from - segment.Start), (int)(through - from))
                    .CopyTo(target[(int)(from - start)..]);
            }

            if (segment.Start + segment.Samples.Length > end)
            {
                break;
            }

            _ = _segments.Dequeue();
        }
    }

    private void ConfigureFilter(int rate)
    {
        // Two cascaded low-pass biquads before rate conversion. Delay state spans packets.
        var omega = 2 * Math.PI * Math.Min(7000, rate * 0.43) / rate;
        var cos = Math.Cos(omega);
        var alpha = Math.Sin(omega) / Math.Sqrt(2);
        _b0 = (1 - cos) / (2 * (1 + alpha));
        _b1 = 2 * _b0;
        _b2 = _b0;
        _a1 = -2 * cos / (1 + alpha);
        _a2 = (1 - alpha) / (1 + alpha);
    }

    private float Filter(float value)
    {
        var first = (_b0 * value) + _s1;
        _s1 = (_b1 * value) - (_a1 * first) + _s2;
        _s2 = (_b2 * value) - (_a2 * first);
        var second = (_b0 * first) + _s3;
        _s3 = (_b1 * first) - (_a1 * second) + _s4;
        _s4 = (_b2 * first) - (_a2 * second);
        return (float)second;
    }
}
