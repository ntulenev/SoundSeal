using System.Buffers.Binary;

using FluentAssertions;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;
using MeetingRecorder.Storage;

namespace MeetingRecorder.Infrastructure.Tests;

/// <summary>Tests timeline alignment and two-source mixing.</summary>
public sealed class MixingTests : IDisposable
{
    private const long SECOND = TimeSpan.TicksPerSecond;

    private static readonly InputFormat MIC = new(44100, 1, 16, false, 0);
    private static readonly InputFormat PLAYBACK = new(48000, 2, 32, true, 3);

    private DirectoryInfo? _folder;

    private DirectoryInfo Folder => _folder ??= Directory.CreateTempSubdirectory();

    /// <summary>An empty partial block following a tone contains only digital silence.</summary>
    [Trait("Category", "Unit")]
    [Fact(DisplayName = "Sparse mixing clears the meters and writes the exact final silent frame count")]
    public void SilentTailShouldNotRepeatThePreviousBlock()
    {
        using var file = new MemorySink();
        var meters = new List<Levels>();
        var mixer = new IntervalMixer(0, file, meters.Add);
        mixer.Add(Tone(false, 0, 0, 0.1, 440));
        mixer.Drain(SECOND / 10);
        var prefix = file.Length;
        mixer.Drain((SECOND / 10) + (13 * RecordingFormat.TicksPerFrame));
        Assert.Equal(26, file.Length - prefix);
        Assert.All(file.ToArray().Skip((int)prefix), sample => Assert.Equal(0, sample));
        Assert.Equal(default, meters[^1]);
    }

    /// <summary>Twenty seconds without loopback preserve speech order and silence.</summary>
    [Trait("Category", "Unit")]
    [Fact(DisplayName = "Twenty seconds without loopback preserve speech order and silence")]
    public void LatePlaybackShouldKeepItsTimestamp()
    {
        using var file = new MemorySink();
        var mixer = new IntervalMixer(0, file, _ => { });
        mixer.Add(Tone(false, 0, 0, 0.1, 400));
        mixer.Drain(20 * SECOND);
        mixer.Add(Tone(true, 0, 20 * SECOND, 0.1, 800));
        mixer.Drain(22 * SECOND);
        var samples = Decode(file.ToArray());
        samples.Length.Should().Be(22 * 16000);
        FirstPeak(samples, 16000, 250).Should().BeInRange(320000, 320040);
        samples.Skip(322000).Should().OnlyContain(v => v == 0);
        Energy(samples, 0, 1000, 400).Should().BeGreaterThan(0.12);
    }

    /// <summary>Separate post-gain meters and WAV frequencies follow both inputs.</summary>
    [Trait("Category", "Unit")]
    [Fact(DisplayName = "Separate post-gain meters and WAV frequencies follow both inputs")]
    public void MetersShouldDescribeExactlyTheWrittenSources()
    {
        using var file = new MemorySink();
        var levels = new Levels();
        var mixer = new IntervalMixer(0, file, l => levels = l);
        for (var i = 0; i < 4; i++)
        {
            var mic = i is 0 or 2;
            var output = i is 1 or 2;
            mixer.Add(Tone(false, i * 44100, i * SECOND, 1, mic ? 431 : 0));
            mixer.Add(Tone(true, i * 48000, i * SECOND, 1, output ? 827 : 0));
            mixer.Drain((i + 1) * SECOND);
            (levels.Microphone.Value > 0.1).Should().Be(mic);
            (levels.Computer.Value > 0.1).Should().Be(output);
        }

        var samples = Decode(file.ToArray());
        for (var i = 0; i < 4; i++)
        {
            var start = (16000 * i) + 1000;
            (Energy(samples, start, 8000, 431) > 0.1).Should().Be(i is 0 or 2);
            (Energy(samples, start, 8000, 827) > 0.1).Should().Be(i is 1 or 2);
        }

        samples.Max().Should().BeGreaterThan(20000);
        samples.Min().Should().BeLessThan(-20000);
        samples.Max(v => Math.Abs((int)v)).Should().BeLessThan(32000);
    }

    /// <summary>Pause trims crossing packets and excludes the entire paused tone.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Pause trims crossing packets and excludes the entire paused tone")]
    public void ResumeShouldAppendOnlyActiveSamples()
    {
        using var file = WaveDestination.Create(Folder.FullName, DateTime.Now);
        var before = new IntervalMixer(0, file, _ => { });
        before.Add(Tone(false, 0, 49 * SECOND / 10, 0.1, 440));
        before.Add(Tone(true, 0, 5 * SECOND, 0.2, 3000));
        before.Drain(5 * SECOND);
        var after = new IntervalMixer(8 * SECOND, file, _ => { });
        after.Add(Tone(false, 0, 79 * SECOND / 10, 0.1, 3000));
        after.Add(Tone(false, 4410, 8 * SECOND, 0.1, 880));
        after.Drain(13 * SECOND);

        var samples = Read(file.Complete(EmptyRecordingPolicy.Keep, null));
        samples.Length.Should().Be(160000);
        Energy(samples, 78500, 1400, 440).Should().BeGreaterThan(0.1);
        Energy(samples, 80100, 1400, 880).Should().BeGreaterThan(0.1);
        Energy(samples, 78000, 6000, 3000).Should().BeLessThan(0.002);
        samples.Take(78000).Should().OnlyContain(v => v == 0);
        samples.Skip(82000).Should().OnlyContain(v => v == 0);
    }

    /// <summary>Packets crossing pause and resume boundaries are trimmed by sample time.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Packets crossing pause and resume boundaries are trimmed by sample time")]
    public void CrossingPacketsShouldExcludePauseOnlySignal()
    {
        using var file = WaveDestination.Create(Folder.FullName, DateTime.Now);
        var first = new IntervalMixer(0, file, _ => { });
        var lastPacket = Tone(false, 0, 9 * SECOND / 10, 0.2, 440);
        var half = lastPacket.Data.Length / 2;
        var pausedTone = Tone(false, 0, 0, 0.1, 3000);
        pausedTone.Data.CopyTo(lastPacket.Data, half);
        first.Add(lastPacket);
        first.Drain(SECOND);

        var second = new IntervalMixer(4 * SECOND, file, _ => { });
        var firstPacket = Tone(false, 0, 39 * SECOND / 10, 0.2, 880);
        pausedTone.Data.CopyTo(firstPacket.Data, 0);
        second.Add(firstPacket);
        second.Drain(5 * SECOND);

        var samples = Read(file.Complete(EmptyRecordingPolicy.Keep, null));
        samples.Length.Should().Be(32000);
        Energy(samples, 14500, 1400, 440).Should().BeGreaterThan(0.1);
        Energy(samples, 16100, 1400, 880).Should().BeGreaterThan(0.1);
        Energy(samples, 14400, 3200, 3000).Should().BeLessThan(0.002);
    }

    /// <summary>Center dialogue survives channel-role downmix and LFE is excluded.</summary>
    [Trait("Category", "Unit")]
    [Fact(DisplayName = "Center dialogue survives channel-role downmix and LFE is excluded")]
    public void CenterShouldSurviveDownmix()
    {
        var format = new InputFormat(48000, 6, 32, true, 63);
        var bytes = new byte[24];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), 1);
        format.Decode(bytes).Should().BeGreaterThan(0.08f);
        Array.Clear(bytes);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(12), 1);
        format.Decode(bytes).Should().Be(0);
    }

    /// <summary>One missing timestamp is extrapolated and repeated errors interrupt.</summary>
    [Trait("Category", "Unit")]
    [Fact(DisplayName = "One missing timestamp is extrapolated and repeated errors interrupt")]
    public void InvalidTimestampShouldHaveBoundedRecovery()
    {
        var track = new SourceTrack(0);
        var packet = Tone(false, 0, 0, 0.01, 500);
        track.Add(packet, 0);
        track.Add(packet with { Position = 441, Qpc = null }, 0);
        var act = () => track.Add(packet with { Position = 882, Qpc = null }, 0);
        act.Should().Throw<RecordingException>().WithMessage("*timestamps*");
    }

    /// <summary>An hour with 100 ppm clock drift preserves paired marker timing.</summary>
    [Trait("Category", "Integration")]
    [Theory(DisplayName = "An hour with 100 ppm clock drift preserves paired marker timing")]
    [InlineData(44100, 48000)]
    [InlineData(48000, 44100)]
    public void DeviceDriftShouldKeepMarkersAligned(int micRate, int outputRate)
    {
        using var file = WaveDestination.Create(Folder.FullName, DateTime.Now);
        var mixer = new IntervalMixer(0, file, _ => { });
        var micFormat = new InputFormat(micRate, 1, 16, false, 0);
        var outFormat = new InputFormat(outputRate, 1, 16, false, 0);
        // Every packet covers 100 ms of device time. Wall clocks differ by 100 ppm.
        // Positive and negative pulses are deliberately separated by 100 ms.
        for (var block = 0; block < 36001; block++)
        {
            var mic = MarkerPacket(micFormat, false, block, 1.00005);
            var output = MarkerPacket(outFormat, true, block, 0.99995);
            // Deliver in varying order. Only capture timestamps may position the markers.
            mixer.Add(block % 3 == 0 ? mic : output);
            mixer.Add(block % 3 == 0 ? output : mic);
            mixer.Drain((block * SECOND / 10) - (SECOND / 2) - (block % 7 * 35000));
        }

        mixer.Drain(3600 * SECOND);
        var result = file.Complete(EmptyRecordingPolicy.Keep, null);
        result.Frames.Value.Should().Be(3600L * 16000);
        using var stream = File.OpenRead(result.Path!.Value);
        foreach (var second in new[] { 0, 3599 })
        {
            stream.Position = 44 + (second * 32000L);
            var bytes = new byte[32000];
            stream.ReadExactly(bytes);
            var segment = Decode(bytes);
            var mic = Array.FindIndex(segment, sample => sample > 2000);
            var output = Array.FindIndex(segment, sample => sample < -2000);
            mic.Should().BeInRange(3200 - 320, 3200 + 320);
            output.Should().BeInRange(4800 - 320, 4800 + 320);
            Math.Abs(output - mic - 1600).Should().BeLessThan(800);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _folder?.Delete(true);

    internal static AudioPacket Tone(bool playback, long position, long qpc,
        double seconds, double hz)
    {
        var format = playback ? PLAYBACK : MIC;
        var frames = (int)Math.Round(seconds * format.Rate);
        var bytes = hz == 0 ? [] : new byte[frames * format.Stride];
        for (var i = 0; hz > 0 && i < frames; i++)
        {
            var amplitude = 0.86 * Math.Sin(2 * Math.PI * hz * i / format.Rate);
            var frame = bytes.AsSpan(i * format.Stride);
            if (playback)
            {
                BinaryPrimitives.WriteSingleLittleEndian(frame, (float)amplitude);
                BinaryPrimitives.WriteSingleLittleEndian(frame[4..], (float)amplitude);
            }
            else
            {
                BinaryPrimitives.WriteInt16LittleEndian(frame, (short)(amplitude * 32767));
            }
        }

        return new(playback, format, position, qpc, frames, bytes);
    }

    private static AudioPacket MarkerPacket(InputFormat format, bool playback, int block,
        double speed)
    {
        var count = format.Rate / 10;
        var position = (long)block * count;
        var rate = format.Rate * speed;
        var time = position / rate;
        var second = time < 1 ? 0 : 3599;
        var marker = second + (playback ? 0.3 : 0.2);
        byte[] bytes = [];
        if (time < marker + 0.025 && time + (count / rate) >= marker)
        {
            bytes = new byte[count * 2];
            for (var i = 0; i < count; i++)
            {
                var t = time + (i / rate);
                if (t >= marker && t < marker + 0.025)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2),
                        (short)(playback ? -20000 : 20000));
                }
            }
        }

        return new(playback, format, position, (long)(time * SECOND), count, bytes);
    }

    private static short[] Read(RecordingResult result)
    {
        using var file = File.Open(result.Path!.Value, FileMode.Open, FileAccess.Read, FileShare.None);
        using var reader = new BinaryReader(file);
        var header = reader.ReadBytes(44);
        header.AsSpan(0, 4).SequenceEqual("RIFF"u8).Should().BeTrue();
        header.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8).Should().BeTrue();
        header.AsSpan(36, 4).SequenceEqual("data"u8).Should().BeTrue();
        BitConverter.ToInt32(header, 4).Should().Be((int)file.Length - 8);
        BitConverter.ToInt16(header, 20).Should().Be(1);
        BitConverter.ToInt16(header, 22).Should().Be(1);
        BitConverter.ToInt32(header, 24).Should().Be(16000);
        BitConverter.ToInt16(header, 34).Should().Be(16);
        BitConverter.ToInt32(header, 40).Should().Be((int)file.Length - 44);
        return Decode(reader.ReadBytes((int)file.Length - 44));
    }

    private static short[] Decode(byte[] bytes)
    {
        var samples = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return samples;
    }

    private static int FirstPeak(short[] samples, int start, int threshold) =>
        Array.FindIndex(samples, start, v => Math.Abs((int)v) > threshold);

    private static double Energy(short[] samples, int start, int count, double hz)
    {
        double real = 0, imaginary = 0;
        for (var i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * hz * i / 16000;
            real += samples[start + i] * Math.Cos(angle);
            imaginary += samples[start + i] * Math.Sin(angle);
        }

        return Math.Sqrt((real * real) + (imaginary * imaginary)) / count / 32768;
    }

    private sealed class MemorySink : MemoryStream, IPcmSink
    {
        public void Append(Pcm16Block block) => Write(block.Bytes);
    }
}
