using System.Buffers.Binary;

using MeetingRecorder.Domain;
using MeetingRecorder.Storage;
using MeetingRecorder.TestSupport;

namespace MeetingRecorder.Infrastructure.Tests;

/// <summary>Exercises independent WAV publication without a live audio endpoint or wall-clock waits.</summary>
[Trait("Category", "Integration")]
public sealed class SegmentedRecordingTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory();

    /// <summary>Files become readable at the production fifteen-minute boundary while capture still owns the destination.</summary>
    [Fact(DisplayName = "A full quarter-hour WAV survives loss of its unfinished successor")]
    public Task PublishedQuarterHourSurvivesInterruption() => TestDeadline.Run(async token =>
    {
        const int FRAMES = 15 * 60 * 16000;
        var settings = new RecordingSettings(null, "out", _folder.FullName, RecordingMode.Audio, RecordingLayout.QuarterHourParts);
        using (var destination = await new WaveRecordingStore().CreateAsync(settings, token))
        {
            var second = new byte[16000 * sizeof(short)];
            for (var i = 0; i < 900; i++)
            {
                destination.Append(new Pcm16Block(second));
            }

            var file = Assert.Single(_folder.GetFiles("*.wav"));
            Assert.StartsWith("Audio-", file.Name, StringComparison.Ordinal);
            Assert.EndsWith("Part-0001.wav", file.Name, StringComparison.Ordinal);
            Assert.Equal(44 + (FRAMES * sizeof(short)), file.Length);
            Assert.Empty(_folder.GetFiles("*.partial"));
            destination.Append(new Pcm16Block([9, 0]));
            Assert.Single(_folder.GetFiles("*.partial"));
            using var sealedFile = file.OpenRead();
            var header = new byte[44];
            await sealedFile.ReadExactlyAsync(header, token);
            Assert.Equal(FRAMES * sizeof(short), BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(40)));
            // Release the open stream without finalization to simulate the lost last part.
        }

        Assert.Single(_folder.GetFiles("*.wav"));
    });

    /// <summary>Cross-boundary writes preserve every sample and publish no trailing empty part.</summary>
    [Theory(DisplayName = "Split files concatenate to the exact input including a short tail or exact boundary")]
    [InlineData(8, 3)]
    [InlineData(9, 3)]
    [InlineData(2, 1)]
    public Task PartsRoundTripWithoutDroppedOrDuplicatedSamples(int frames, int expectedFiles) =>
        TestDeadline.Run(async token =>
        {
            using var writer = new SegmentedFileDestination(_folder.FullName, RecordingMode.Dictaphone, DateTime.Now, 3);
            var data = Enumerable.Range(1, frames * 2).Select(i => (byte)i).ToArray();
            writer.Append(new Pcm16Block(data.AsSpan(0, 2)));
            writer.Append(new Pcm16Block(data.AsSpan(2)));
            var result = await writer.CompleteAsync(EmptyRecordingPolicy.Keep, null, token);
            Assert.True(result.IsSuccessful);
            Assert.Equal(expectedFiles, result.PublishedFiles.Count);
            Assert.Equal(frames, result.Frames.Value);
            var actual = new List<byte>();
            foreach (var file in result.PublishedFiles)
            {
                var bytes = await File.ReadAllBytesAsync(file.Value, token);
                Assert.Equal(bytes.Length - 44, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40)));
                actual.AddRange(bytes.Skip(44));
            }

            Assert.Equal(data, actual);
            Assert.Empty(_folder.GetFiles("*.partial"));
        });

    /// <summary>A publication error retains earlier playable files and the recoverable final path.</summary>
    [Fact(DisplayName = "A failed rollover cannot overwrite a completed part or replay its input block")]
    public Task FailedRolloverKeepsCommittedParts() => TestDeadline.Run(async token =>
    {
        using var writer = new SegmentedFileDestination(_folder.FullName, RecordingMode.Meeting, DateTime.Now, 2);
        writer.Append(new Pcm16Block([1, 0, 2, 0, 3, 0]));
        var partial = Assert.Single(_folder.GetFiles("*.partial")).FullName;
        var collision = Path.ChangeExtension(partial, null);
        await File.WriteAllTextAsync(collision, "existing", token);
        Assert.Throws<RecordingException>(() => writer.Append(new Pcm16Block([4, 0])));
        Assert.Throws<RecordingException>(() => writer.Append(new Pcm16Block([4, 0])));
        var result = await writer.CompleteAsync(EmptyRecordingPolicy.Keep, "Device disconnected.", token);
        Assert.False(result.IsSuccessful);
        Assert.Equal(4, result.Frames.Value);
        Assert.Equal(partial, result.Path!.Value);
        Assert.Single(result.PublishedFiles);
        Assert.Contains("disconnected", result.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains("published", result.Problem.Message, StringComparison.Ordinal);
        Assert.Equal("existing", await File.ReadAllTextAsync(collision, token));
    });

    /// <summary>Empty startup reservations honor cancellation and the selected discard policy.</summary>
    [Fact(DisplayName = "An empty segmented session can retry canceled finalization and discard the reservation")]
    public Task CanceledCompletionMayBeRetried() => TestDeadline.Run(async token =>
    {
        using var writer = new SegmentedFileDestination(_folder.FullName, RecordingMode.Meeting, DateTime.Now, 3);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.CompleteAsync(EmptyRecordingPolicy.Keep, null, canceled.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = writer.CompleteAsync((EmptyRecordingPolicy)100, null, token); });
        var result = await writer.CompleteAsync(EmptyRecordingPolicy.Discard, "Canceled.", token);
        Assert.Empty(result.PublishedFiles);
        Assert.Empty(_folder.GetFiles());
        Assert.Throws<ObjectDisposedException>(() => writer.Append(new Pcm16Block([0, 0])));
        Assert.Throws<ObjectDisposedException>(() => { _ = writer.CompleteAsync(EmptyRecordingPolicy.Keep, null, token); });
    });

    /// <summary>An unavailable destination for the next file keeps the previously published path.</summary>
    [Fact(DisplayName = "Failure to create the next part preserves the completed file in the result")]
    public Task FailedReservationKeepsEarlierLinks() => TestDeadline.Run(async token =>
    {
        var location = _folder.CreateSubdirectory("recordings").FullName;
        using var writer = new SegmentedFileDestination(location, RecordingMode.Audio, DateTime.Now, 2);
        writer.Append(new Pcm16Block([1, 0, 2, 0]));
        var moved = Path.Combine(_folder.FullName, "offline");
        Directory.Move(location, moved);
        await File.WriteAllTextAsync(location, "no directory here", token);
        Assert.Throws<RecordingException>(() => writer.Append(new Pcm16Block([3, 0])));
        var result = await writer.CompleteAsync(EmptyRecordingPolicy.Keep, null, token);
        Assert.False(result.IsSuccessful);
        Assert.Equal(2, result.Frames.Value);
        Assert.Equal(result.Path, Assert.Single(result.PublishedFiles));
        Assert.Single(Directory.GetFiles(moved, "*.wav"));
    });

    /// <inheritdoc/>
    public void Dispose() => _folder.Delete(true);
}
