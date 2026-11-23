using System.Buffers.Binary;
using System.Runtime.InteropServices;

using MeetingRecorder.Domain;
using MeetingRecorder.Storage;
using MeetingRecorder.TestSupport;

using NAudio.Wave;

namespace MeetingRecorder.Infrastructure.Tests;

/// <summary>Exercises the installed Windows MP3 encoder and independently reopens its output.</summary>
[Trait("Category", "Integration")]
public sealed class Mp3RecordingTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory();

    /// <summary>Each layout writes real 48 kbit/s mono frames, including the final short part.</summary>
    [Theory]
    [InlineData(RecordingLayout.SingleFile)]
    [InlineData(RecordingLayout.QuarterHourParts)]
    public Task RecordingsDecodeAsMonoMp3(RecordingLayout layout) => TestDeadline.Run(async token =>
    {
        var sampleRate = RecordingFormat.SampleRate;
        var second = new byte[sampleRate * sizeof(short)];
        for (var i = 0; i < sampleRate; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(second.AsSpan(i * sizeof(short)),
                (short)(12000 * Math.Sin(2 * Math.PI * 440 * i / sampleRate)));
        }

        var settings = new RecordingSettings("mic", null, _folder.FullName, RecordingMode.Dictaphone, layout, RecordingFileFormat.Mp3);
        // Use a short boundary to exercise the production rollover without encoding 15 minutes in every test run.
        const int PART_SECONDS = 2;
        using var destination = layout == RecordingLayout.QuarterHourParts
            ? new SegmentedFileDestination(settings.Folder, settings.Mode, DateTime.Now, PART_SECONDS * sampleRate, settings.Format)
            : await new WaveRecordingStore().CreateAsync(settings, token);
        for (var i = 0; i < PART_SECONDS; i++)
        {
            destination.Append(new Pcm16Block(second));
        }

        if (layout == RecordingLayout.QuarterHourParts)
        {
            var first = Assert.Single(_folder.GetFiles("*.mp3"));
            Assert.EndsWith("Part-0001.mp3", first.Name, StringComparison.Ordinal);
            AssertPlayable(first.FullName, PART_SECONDS);
        }

        destination.Append(new Pcm16Block(second.AsSpan(0, second.Length / 2)));
        var result = await destination.CompleteAsync(EmptyRecordingPolicy.Keep, null, token);
        Assert.True(result.IsSuccessful, result.Problem?.Message);
        Assert.Equal((PART_SECONDS * sampleRate) + (sampleRate / 2), result.Frames.Value);
        Assert.Equal(layout == RecordingLayout.QuarterHourParts ? 2 : 1, result.PublishedFiles.Count);
        AssertPlayable(result.PublishedFiles[^1].Value, layout == RecordingLayout.QuarterHourParts ? 0.5 : 2.5);
        Assert.Empty(_folder.GetFiles("*.partial"));
        Assert.Empty(_folder.GetFiles("*.wav"));
    });

    /// <summary>Finishing immediately still produces an MP3 frame that a player can open.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(160)]
    [InlineData(576)]
    [InlineData(4000)]
    [InlineData(4400)]
    public Task ImmediateFinishProducesAValidMp3(int frames) => TestDeadline.Run(async token =>
    {
        var settings = new RecordingSettings("mic", null, _folder.FullName, RecordingMode.Dictaphone,
            RecordingLayout.SingleFile, RecordingFileFormat.Mp3);
        using var destination = await new WaveRecordingStore().CreateAsync(settings, token);
        var bytes = new byte[frames * sizeof(short)];
        for (var i = Math.Max(0, frames - 160); i < frames; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * sizeof(short)),
                (short)(12000 * Math.Sin(2 * Math.PI * 440 * i / RecordingFormat.SampleRate)));
        }
        destination.Append(new Pcm16Block(bytes));
        var result = await destination.CompleteAsync(EmptyRecordingPolicy.Keep, null, token);
        Assert.True(result.IsSuccessful, result.Problem?.Message);
        Assert.Equal(frames, result.Frames.Value);
        using var file = File.OpenRead(result.Path!.Value);
        Assert.NotNull(Mp3Frame.LoadFromStream(file));
        using var reader = new MediaFoundationReader(result.Path.Value);
        using var decoded = new MemoryStream();
        await reader.CopyToAsync(decoded, token);
        Assert.True(decoded.Length > 0);
        if (frames >= 160)
        {
            var samples = MemoryMarshal.Cast<byte, short>(decoded.ToArray());
            Assert.True(samples.ContainsAnyExceptInRange((short)-1000, (short)1000));
        }
    });

    /// <summary>A failed start closes the encoder and discards its empty reservation.</summary>
    [Fact]
    public Task FailedStartDiscardsEmptyMp3() => TestDeadline.Run(async token =>
    {
        var settings = new RecordingSettings(null, "out", _folder.FullName, RecordingMode.Audio,
            RecordingLayout.QuarterHourParts, RecordingFileFormat.Mp3);
        using var destination = await new WaveRecordingStore().CreateAsync(settings, token);
        Assert.EndsWith("Part-0001.mp3.partial", Assert.Single(_folder.GetFiles()).Name, StringComparison.Ordinal);
        var result = await destination.CompleteAsync(EmptyRecordingPolicy.Discard, "Start canceled.", token);
        Assert.Empty(result.PublishedFiles);
        Assert.Empty(_folder.GetFiles());
    });

    /// <inheritdoc/>
    public void Dispose() => _folder.Delete(true);

    private static void AssertPlayable(string path, double seconds)
    {
        using var stream = File.OpenRead(path);
        var frame = Mp3Frame.LoadFromStream(stream);
        Assert.NotNull(frame);
        Assert.Equal(48000, frame.BitRate);
        Assert.Equal(RecordingFormat.SampleRate, frame.SampleRate);
        Assert.Equal(ChannelMode.Mono, frame.ChannelMode);
        Assert.InRange(stream.Length, (long)((seconds - 0.1) * 6000), (long)((seconds + 0.2) * 6000));
        using var decoded = new MediaFoundationReader(path);
        var pcm = new byte[RecordingFormat.SampleRate];
        var count = decoded.Read(pcm, 0, pcm.Length);
        Assert.True(count > 0);
        Assert.Contains(pcm.Take(count), value => value != 0);
    }
}
