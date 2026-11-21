using FluentAssertions;

using MeetingRecorder.Application;
using MeetingRecorder.Application.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;
using MeetingRecorder.Infrastructure.DependencyInjection;
using MeetingRecorder.Infrastructure.Files;
using MeetingRecorder.Infrastructure.Wasapi;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace MeetingRecorder.Infrastructure.Tests;

[Trait("Category", "Unit")]
public sealed class CaptureSessionTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory();
    private readonly PerformanceClock _clock = new();

    [Fact(DisplayName = "Offline recording reopens pinned devices and appends to the same WAV after a pause")]
    public async Task OfflineSessionShouldKeepBothMarkersAndRemoveThePause()
    {
        // Arrange two pairs of fresh device handles and advance only their shared clock.
        long now = 0;
        var clock = new Mock<IRecordingClock>(MockBehavior.Strict);
        clock.SetupGet(c => c.Now).Returns(() => Interlocked.Read(ref now));
        using var mic = new TestEndpoint { NextPacket = MixingTests.Tone(false, 0, 0, 0.1, 440) };
        using var output = new TestEndpoint();
        using var resumedMic = new TestEndpoint();
        using var resumedOutput = new TestEndpoint
        {
            NextPacket = MixingTests.Tone(true, 0, 8 * TimeSpan.TicksPerSecond, 0.1, 880)
        };
        var handles = new Queue<TestEndpoint>([mic, output, resumedMic, resumedOutput]);
        var opened = new List<string>();
        var settings = new RecordingSettings("selected mic", "alternate output", _folder.FullName);
        using var file = WaveDestination.Create(_folder.FullName, DateTime.Now);
        using var session = new CaptureSession(settings, clock.Object, file, (id, _) =>
        {
            opened.Add(id);
            return handles.Dequeue();
        });
        var factory = new Mock<ICaptureFactory>(MockBehavior.Strict);
        factory.Setup(f => f.Open(settings)).Returns(session);
        using var recorder = new RecordingCoordinator(factory.Object, clock.Object);

        // Act
        await recorder.StartAsync(settings);
        Interlocked.Exchange(ref now, 5 * TimeSpan.TicksPerSecond);
        await recorder.PauseAsync();
        Interlocked.Exchange(ref now, 8 * TimeSpan.TicksPerSecond);
        recorder.Elapsed.Should().Be(TimeSpan.FromSeconds(5));
        await recorder.ResumeAsync();
        Interlocked.Exchange(ref now, 13 * TimeSpan.TicksPerSecond);
        await recorder.FinishAsync();

        // Assert the playback marker follows the pause at frame 80000, not frame 128000.
        recorder.Snapshot.State.Should().Be(RecorderState.Saved);
        recorder.Elapsed.Should().Be(TimeSpan.FromSeconds(10));
        opened.Should().Equal(settings.MicrophoneId, settings.OutputId,
            settings.MicrophoneId, settings.OutputId);
        var bytes = await File.ReadAllBytesAsync(recorder.Snapshot.Result!.Path!);
        bytes.Length.Should().Be(320044);
        bytes.Skip(44).Take(3000).Should().Contain(v => v != 0);
        bytes.Skip(3244).Take(156800).Should().OnlyContain(v => v == 0);
        bytes.Skip(160044).Take(3000).Should().Contain(v => v != 0);
        resumedOutput.Disposed.Should().BeTrue();
    }

    [Theory(DisplayName = "Startup failure releases acquired handles and removes empty output")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedStartShouldReleaseBothEndpoints(bool loopback)
    {
        using var mic = new TestEndpoint { FailStart = !loopback };
        using var output = new TestEndpoint { FailStart = loopback };
        using var session = Create(mic, output);

        var act = () => session.BeginIntervalAsync();
        await act.Should().ThrowAsync<RecordingException>();
        var result = await session.CompleteAsync(true);

        result.Path.Should().BeNull();
        _folder.GetFiles().Should().BeEmpty();
        mic.Disposed.Should().BeTrue();
        output.Disposed.Should().BeTrue();
    }

    [Fact(DisplayName = "Disconnected endpoint interrupts, publishes available audio and releases the file")]
    public async Task DeviceLossShouldSaveAvailableAudio()
    {
        using var mic = new TestEndpoint();
        using var output = new TestEndpoint();
        using var session = Create(mic, output);
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Interrupted += (_, _) => fault.TrySetResult();
        var start = await session.BeginIntervalAsync();
        mic.NextPacket = MixingTests.Tone(false, 0, start, 0.03, 440);
        await Task.Delay(45);
        output.FailRead = true;
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await session.CompleteAsync(false);
        result.Published.Should().BeTrue();
        result.Problem.Should().Contain("disconnected");
        result.Frames.Should().BePositive();
        using var file = File.Open(result.Path!, FileMode.Open, FileAccess.Read, FileShare.None);
        file.Length.Should().Be(44 + (result.Frames * 2));
        file.Position = 44;
        var audio = new byte[640];
        await file.ReadExactlyAsync(audio);
        audio.Should().Contain(value => value != 0);
        mic.Disposed.Should().BeTrue();
        output.Disposed.Should().BeTrue();
    }

    [Fact(DisplayName = "A disconnected device on resume preserves the first interval")]
    public async Task ResumeFailureShouldPreservePriorAudio()
    {
        using var mic = new TestEndpoint();
        using var output = new TestEndpoint();
        using var session = Create(mic, output);
        var start = await session.BeginIntervalAsync();
        await session.EndIntervalAsync(start + (TimeSpan.TicksPerMillisecond * 100));
        output.FailStart = true;
        var resume = () => session.BeginIntervalAsync();
        await resume.Should().ThrowAsync<RecordingException>();
        var result = await session.CompleteAsync(false);
        result.Frames.Should().Be(1600);
        result.Published.Should().BeTrue();
    }

    [Fact(DisplayName = "Queue pressure interrupts without blocking the capture thread")]
    public async Task QueueOverflowShouldInterrupt()
    {
        using var file = WaveDestination.Create(_folder.FullName, DateTime.Now);
        using var stalled = new ManualResetEventSlim();
        var fault = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var mic = new TestEndpoint();
        using var output = new TestEndpoint();
        var interval = new CaptureInterval(new("mic", "out", _folder.FullName), _clock,
            (_, loopback) => loopback ? output : mic, file,
            _ => stalled.Wait(TimeSpan.FromSeconds(3)), message => fault.TrySetResult(message), 1);
        await interval.Started;
        try
        {
            (await fault.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Contain("queue");
        }
        finally
        {
            stalled.Set();
            await interval.StopAsync(_clock.Now);
        }

        file.Complete(false, null).Published.Should().BeTrue();
    }

    [Fact(DisplayName = "Concurrent filenames reserve separate paths and never overwrite")]
    public void FileCollisionShouldChooseSuffix()
    {
        var now = DateTime.Now;
        using var first = WaveDestination.Create(_folder.FullName, now);
        using var next = WaveDestination.Create(_folder.FullName, now);
        var a = first.Complete(false, null);
        var b = next.Complete(false, null);
        a.Path.Should().NotBe(b.Path);
        b.Path.Should().EndWith("-1.wav");
        using var third = WaveDestination.Create(_folder.FullName, now);
        third.Complete(false, null).Path.Should().EndWith("-2.wav");
    }

    [Fact(DisplayName = "Failed publication preserves the unfinished file and existing destination")]
    public void PublicationFailureShouldPreservePartial()
    {
        using var writer = WaveDestination.Create(_folder.FullName, DateTime.Now);
        writer.Append([1, 2, 3, 4]);
        var final = Path.ChangeExtension(_folder.GetFiles().Single().FullName, null);
        File.WriteAllText(final, "existing file");

        var result = writer.Complete(false, null);

        result.Published.Should().BeFalse();
        result.Path.Should().EndWith(".partial");
        new FileInfo(result.Path!).Length.Should().Be(48);
        File.ReadAllText(final).Should().Be("existing file");
    }

    [Fact(DisplayName = "Partial disk writes are truncated to the last complete block")]
    public void DiskFailureShouldKeepEarlierFrames()
    {
        var path = Path.Combine(_folder.FullName, "sound.partial");
        var final = Path.Combine(_folder.FullName, "sound.wav");
        using var stream = new FailingFile(path);
        stream.Position = 44;
        using var writer = new WaveDestination(stream, path, final);
        writer.Append(new byte[100]);
        stream.FailNext = true;

        var act = () => writer.Append(new byte[100]);
        act.Should().Throw<RecordingException>().WithMessage("*disk*");
        act.Should().Throw<RecordingException>().WithMessage("*disk*");
        var result = writer.Complete(false, "Writing stopped.");

        result.Published.Should().BeTrue();
        result.Frames.Should().Be(50);
        File.ReadAllBytes(final).Length.Should().Be(144);
    }

    [Fact(DisplayName = "The RIFF boundary is checked before writing a block")]
    public void LastBlockShouldFitBothRiffSizeFields()
    {
        var last = (uint.MaxValue - 36L) / 2;
        WaveDestination.CheckCapacity(last - 1, 2);
        var write = () => WaveDestination.CheckCapacity(last, 2);
        write.Should().Throw<RecordingException>().WithMessage("*RIFF*");
    }

    [Theory(DisplayName = "Driver failures are explained in English for the affected source")]
    [InlineData(false, -2147024891, "desktop apps")]
    [InlineData(true, -2147024891, "Computer audio access was denied")]
    [InlineData(true, unchecked((int)0x88890004), "disconnected")]
    [InlineData(false, unchecked((int)0x8889000A), "busy")]
    public void DeviceErrorsShouldIdentifySourceAndCause(bool loopback, int code, string expected)
    {
        var native = new IOException("Gerätefehler", code);
        var translated = DeviceErrors.Translate(loopback, native);
        translated.Message.Should().Contain(expected);
        translated.Message.Should().NotContain("Gerätefehler");
    }

    [Fact(DisplayName = "Infrastructure registers the clock, endpoint catalog and capture factory")]
    public void ServicesShouldResolve()
    {
        var config = new ConfigurationBuilder().Build();
        using var services = new ServiceCollection().AddMeetingRecorderInfrastructure(config)
            .BuildServiceProvider();
        services.GetRequiredService<IDeviceCatalog>().Should().BeOfType<EndpointCatalog>();
        services.GetRequiredService<ICaptureFactory>().Should().BeOfType<CaptureFactory>();
        services.GetRequiredService<IRecordingClock>().Now.Should().BePositive();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "CaptureSession owns and disposes the destination returned here.")]
    private CaptureSession Create(TestEndpoint mic, TestEndpoint output) =>
        new(new("mic", "out", _folder.FullName), _clock,
            WaveDestination.Create(_folder.FullName, DateTime.Now),
            (_, loopback) => loopback ? output : mic);

    private sealed class TestEndpoint : IInputEndpoint
    {
        public TimeSpan EnginePeriod => TimeSpan.FromMilliseconds(10);
        internal bool FailStart { get; set; }
        internal volatile bool FailRead;
        internal AudioPacket? NextPacket;
        internal bool Disposed { get; private set; }

        public void Start()
        {
            if (FailStart)
            {
                throw new RecordingException("Device failed to start.");
            }
        }

        public void Drain(Action<AudioPacket> accept)
        {
            if (Interlocked.Exchange(ref NextPacket, null) is { } packet)
            {
                accept(packet);
            }

            if (FailRead)
            {
                throw new RecordingException("Computer audio was disconnected.");
            }
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FailingFile : FileStream
    {
        internal FailingFile(string path) : base(path, FileMode.CreateNew, FileAccess.Write) { }

        internal bool FailNext { get; set; }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (FailNext)
            {
                FailNext = false;
                base.Write(buffer[..2]);
                throw new IOException("Simulated disk failure");
            }

            base.Write(buffer);
        }
    }

    public void Dispose() => _folder.Delete(true);
}
