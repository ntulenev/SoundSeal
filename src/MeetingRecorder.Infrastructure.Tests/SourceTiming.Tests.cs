using FluentAssertions;

using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;

namespace MeetingRecorder.Infrastructure.Tests;

/// <summary>Tests device timestamp recovery.</summary>
[Trait("Category", "Unit")]
public sealed class SourceTimingTests
{
    private static readonly InputFormat FORMAT = new(16000, 1, 16, false, 0);

    /// <summary>Unrecoverable device-position and QPC discontinuities interrupt capture.</summary>
    [Theory(DisplayName = "Unrecoverable device-position and QPC discontinuities interrupt capture")]
    [InlineData(159, 100000L, false)]
    [InlineData(170, 100000L, false)]
    [InlineData(160, 100000L, true)]
    [InlineData(160, -400000L, false)]
    public void BrokenContinuityShouldInterrupt(long position, long qpc, bool discontinuity)
    {
        var track = new SourceTrack(0);
        track.Add(new(false, FORMAT, 0, 0, 160, []), 0);
        var next = () => track.Add(new(false, FORMAT, position, qpc, 160, [],
            discontinuity), 0);
        next.Should().Throw<RecordingException>().WithMessage("*discontinuity*");
    }

    /// <summary>Samples arriving after their output frames were written cannot replace silence.</summary>
    [Fact(DisplayName = "Samples arriving after their output frames were written cannot replace silence")]
    public void LatePacketShouldInterrupt()
    {
        var track = new SourceTrack(0);
        var add = () => track.Add(new(true, FORMAT, 0, 0, 160, []), 80);
        add.Should().Throw<RecordingException>().WithMessage("*too late*");
    }

    /// <summary>A wildly different measured rate interrupts instead of stretching speech.</summary>
    [Fact(DisplayName = "A wildly different measured rate interrupts instead of stretching speech")]
    public void UnreliableRateShouldInterrupt()
    {
        var track = new SourceTrack(0);
        track.Add(new(false, FORMAT, 0, 0, 16000, []), 0);
        var next = () => track.Add(new(false, FORMAT, 16000, 10250000, 160, []), 0);
        next.Should().Throw<RecordingException>().WithMessage("*synchronized*");
    }

    /// <summary>One invalid loopback timestamp can be extrapolated on a forward-moving transport clock.</summary>
    [Fact(DisplayName = "Loopback extrapolates one missing timestamp and rejects ambiguous positions")]
    public void LoopbackShouldRequireReliableTimeAfterOneMissingTimestamp()
    {
        var track = new SourceTrack(0);
        track.Add(new(true, FORMAT, 1000, 0, 160, []), 0);
        track.Add(new(true, FORMAT, 1080, null, 160, []), 0);
        Assert.Throws<RecordingException>(() => track.Add(new(true, FORMAT, 1160, null, 160, []), 0));
        track.Add(new(true, FORMAT, 1160, 200000, 160, []), 0);
        Assert.Throws<RecordingException>(() => track.Add(new(true, FORMAT, 1160, null, 160, []), 0));
        Assert.Throws<RecordingException>(() => track.Add(new(true, FORMAT, 1240, null, 160, [], true), 0));
        Assert.Throws<RecordingException>(() => track.Add(new(true, FORMAT, 1000, 300000, 160, []), 0));
    }

    /// <summary>A small forward gap on a frame-based loopback clock leaves silence in that timeline slot.</summary>
    [Fact(DisplayName = "An established mix-frame counter identifies a short unflagged playback gap")]
    public void ShortPlaybackGapShouldKeepItsPosition()
    {
        var track = new SourceTrack(0);
        var data = Enumerable.Repeat((byte)64, 320).ToArray();
        track.Add(new(true, FORMAT, 0, 0, 160, data), 0);
        track.Add(new(true, FORMAT, 160, 100000, 160, data), 0);
        track.Add(new(true, FORMAT, 480, 300000, 160, data), 0);
        var gap = new float[160];
        track.Read(320, gap);
        Assert.All(gap, sample => Assert.Equal(0f, sample));
        track.Read(480, gap);
        Assert.Contains(gap, sample => sample > 0.05f);
    }

    /// <summary>A flagged packet with overlapping timestamps cannot replace earlier audio.</summary>
    [Fact(DisplayName = "A timestamped loopback restart preserves the previous segment up to its end")]
    public void FlaggedPlaybackOverlapShouldNotDuplicateSamples()
    {
        var track = new SourceTrack(0);
        var first = Enumerable.Repeat((byte)64, 320).ToArray();
        var next = Enumerable.Repeat((byte)192, 320).ToArray();
        track.Add(new(true, FORMAT, 0, 0, 160, first), 0);
        track.Add(new(true, FORMAT, 160, 50000, 160, next, true), 0);
        var data = new float[240];
        track.Read(0, data);
        Assert.All(data[80..160], sample => Assert.True(sample > 0.05f));
        Assert.All(data[165..], sample => Assert.True(sample < -0.05f));
    }

    /// <summary>Loopback after a connected silent gap resumes at its new timestamp.</summary>
    [Fact(DisplayName = "Loopback after a connected silent gap resumes at its new timestamp")]
    public void SilentGapShouldRestartLoopbackTrack()
    {
        var track = new SourceTrack(0);
        track.Add(new(true, FORMAT, 0, 0, 160, []), 0);
        var bytes = new byte[320];
        Array.Fill(bytes, (byte)64);
        track.Add(new(true, FORMAT, 16000, TimeSpan.TicksPerSecond, 160, bytes, true), 0);
        var silent = new float[160];
        var active = new float[160];
        track.Read(160, silent);
        track.Read(16000, active);
        silent.Should().OnlyContain(value => value == 0);
        active.Should().Contain(value => value > 0.05f);
    }
}
