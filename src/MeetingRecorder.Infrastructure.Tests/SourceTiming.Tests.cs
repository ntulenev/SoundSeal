using FluentAssertions;

using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;

namespace MeetingRecorder.Infrastructure.Tests;

[Trait("Category", "Unit")]
public sealed class SourceTimingTests
{
    private static readonly InputFormat FORMAT = new(16000, 1, 16, false, 0);

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

    [Fact(DisplayName = "Samples arriving after their output frames were written cannot replace silence")]
    public void LatePacketShouldInterrupt()
    {
        var track = new SourceTrack(0);
        var add = () => track.Add(new(true, FORMAT, 0, 0, 160, []), 80);
        add.Should().Throw<RecordingException>().WithMessage("*too late*");
    }

    [Fact(DisplayName = "A wildly different measured rate interrupts instead of stretching speech")]
    public void UnreliableRateShouldInterrupt()
    {
        var track = new SourceTrack(0);
        track.Add(new(false, FORMAT, 0, 0, 16000, []), 0);
        var next = () => track.Add(new(false, FORMAT, 16000, 10250000, 160, []), 0);
        next.Should().Throw<RecordingException>().WithMessage("*synchronized*");
    }

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
