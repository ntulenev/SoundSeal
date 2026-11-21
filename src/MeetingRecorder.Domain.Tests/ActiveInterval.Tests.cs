using FluentAssertions;

namespace MeetingRecorder.Domain.Tests;

/// <summary>Tests interval timing and frame counts.</summary>
[Trait("Category", "Unit")]
public sealed class ActiveIntervalTests
{
    /// <summary>Only complete active frames contribute to the WAV duration.</summary>
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
        interval.Frames.Value.Should().Be(frames);
        RecordingFormat.Duration(frames).Value.Ticks.Should().Be(frames * 625);
    }

    /// <summary>Elapsed time sums completed and current active intervals.</summary>
    [Fact(DisplayName = "Elapsed time sums completed and current active intervals")]
    public void SnapshotShouldSumActiveTime()
    {
        var running = new RecorderSnapshot(RecorderState.Recording, TimeSpan.FromTicks(1000), 8000);
        running.Elapsed(8500).Value.Ticks.Should().Be(1500);
        running.Elapsed(7500).Value.Ticks.Should().Be(1000);
        new RecorderSnapshot(RecorderState.Paused, running.ActiveTime).Elapsed(9900).Value.Ticks.Should().Be(1000);
    }
}
