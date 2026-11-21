using FluentAssertions;

namespace MeetingRecorder.Domain.Tests;

[Trait("Category", "Unit")]
public sealed class ActiveIntervalTests
{
    [Theory(DisplayName = "Only complete active frames contribute to the WAV duration")]
    [InlineData(0, 0)]
    [InlineData(624, 0)]
    [InlineData(625, 1)]
    [InlineData(10_000_000, 16_000)]
    public void IntervalShouldCountCompleteFrames(long ticks, long frames)
    {
        // Arrange
        var interval = new ActiveInterval(12345, 12345 + ticks);

        // Act / Assert
        interval.Frames.Should().Be(frames);
        RecordingFormat.Duration(frames).Ticks.Should().Be(frames * 625);
    }

    [Fact(DisplayName = "Elapsed time sums completed and current active intervals")]
    public void SnapshotShouldSumActiveTime()
    {
        var running = new RecorderSnapshot(RecorderState.Recording, 1000, 8000);
        running.Elapsed(8500).Ticks.Should().Be(1500);
        running.Elapsed(7500).Ticks.Should().Be(1000);
        (running with { Started = null }).Elapsed(9900).Ticks.Should().Be(1000);
    }
}
