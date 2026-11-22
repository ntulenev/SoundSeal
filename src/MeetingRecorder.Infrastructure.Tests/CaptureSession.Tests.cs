using FluentAssertions;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Application;
using MeetingRecorder.Application.DependencyInjection;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;
using MeetingRecorder.Infrastructure.DependencyInjection;
using MeetingRecorder.Infrastructure.Wasapi;
using MeetingRecorder.Storage;
using MeetingRecorder.TestSupport;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace MeetingRecorder.Infrastructure.Tests;

/// <summary>Tests capture lifecycles and WAV publication.</summary>
public sealed class CaptureSessionTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory();
    private readonly PerformanceClock _clock = new();

    /// <summary>Offline recording reopens pinned devices and appends to the same WAV after a pause.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Offline recording reopens pinned devices and appends to the same WAV after a pause")]
    public Task OfflineSessionShouldKeepBothMarkersAndRemoveThePause() =>
        TestDeadline.Run(async cancellationToken =>
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
                opened.Add(id.Value);
                return handles.Dequeue();
            });
            var factory = new Mock<ICaptureFactory>(MockBehavior.Strict);
            factory.Setup(f => f.OpenAsync(settings, It.Is<CancellationToken>(t => t.CanBeCanceled))).ReturnsAsync(session);
            using var recorder = new RecordingCoordinator(factory.Object, clock.Object);

            // Act
            await recorder.StartAsync(settings, cancellationToken);
            Interlocked.Exchange(ref now, 5 * TimeSpan.TicksPerSecond);
            await recorder.PauseAsync(cancellationToken);
            Interlocked.Exchange(ref now, 8 * TimeSpan.TicksPerSecond);
            recorder.Elapsed.Value.Should().Be(TimeSpan.FromSeconds(5));
            await recorder.ResumeAsync(cancellationToken);
            Interlocked.Exchange(ref now, 13 * TimeSpan.TicksPerSecond);
            await recorder.FinishAsync(cancellationToken);

            // Assert the playback marker follows the pause at frame 80000, not frame 128000.
            recorder.Snapshot.State.Should().Be(RecorderState.Saved);
            recorder.Elapsed.Value.Should().Be(TimeSpan.FromSeconds(10));
            opened.Should().Equal(settings.MicrophoneId!.Value, settings.OutputId!.Value,
                settings.MicrophoneId!.Value, settings.OutputId!.Value);
            var bytes = await File.ReadAllBytesAsync(recorder.Snapshot.Result!.Path!.Value, cancellationToken);
            bytes.Length.Should().Be(320044);
            bytes.Skip(44).Take(3000).Should().Contain(v => v != 0);
            bytes.Skip(3244).Take(156800).Should().OnlyContain(v => v == 0);
            bytes.Skip(160044).Take(3000).Should().Contain(v => v != 0);
            resumedOutput.Disposed.Should().BeTrue();
        });

    /// <summary>Startup failure releases acquired handles and removes empty output.</summary>
    [Trait("Category", "Integration")]
    [Theory(DisplayName = "Startup failure releases acquired handles and removes empty output")]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedStartShouldReleaseBothEndpoints(bool loopback) =>
        TestDeadline.Run(async cancellationToken =>
        {
            using var mic = new TestEndpoint { FailStart = !loopback };
            using var output = new TestEndpoint { FailStart = loopback };
            using var session = Create(mic, output);

            var act = () => session.BeginIntervalAsync(cancellationToken);
            await act.Should().ThrowAsync<RecordingException>();
            var result = await session.CompleteAsync(EmptyRecordingPolicy.Discard, cancellationToken);

            result.Path.Should().BeNull();
            _folder.GetFiles().Should().BeEmpty();
            mic.Disposed.Should().BeTrue();
            output.Disposed.Should().BeTrue();
        });

    /// <summary>Disconnected endpoint interrupts, publishes available audio and releases the file.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Disconnected endpoint interrupts, publishes available audio and releases the file")]
    public Task DeviceLossShouldSaveAvailableAudio() =>
        TestDeadline.Run(async cancellationToken =>
        {
            using var mic = new TestEndpoint();
            using var output = new TestEndpoint();
            using var session = Create(mic, output);
            var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Interrupted += (_, _) => fault.TrySetResult();
            var start = await session.BeginIntervalAsync(cancellationToken);
            mic.NextPacket = MixingTests.Tone(false, 0, start.Ticks, 0.03, 440);
            await Task.Delay(45, cancellationToken);
            output.FailRead = true;
            await fault.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            var result = await session.CompleteAsync(EmptyRecordingPolicy.Keep, cancellationToken);
            result.Published.Should().BeTrue();
            result.Problem!.Message.Should().Contain("disconnected");
            result.Frames.Value.Should().BePositive();
            using var file = File.Open(result.Path!.Value, FileMode.Open, FileAccess.Read, FileShare.None);
            file.Length.Should().Be(44 + (result.Frames.Value * 2));
            file.Position = 44;
            var audio = new byte[640];
            await file.ReadExactlyAsync(audio, cancellationToken);
            audio.Should().Contain(value => value != 0);
            mic.Disposed.Should().BeTrue();
            output.Disposed.Should().BeTrue();
        });

    /// <summary>A disconnected device on resume preserves the first interval.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "A disconnected device on resume preserves the first interval")]
    public Task ResumeFailureShouldPreservePriorAudio() =>
        TestDeadline.Run(async cancellationToken =>
        {
            using var mic = new TestEndpoint();
            using var output = new TestEndpoint();
            using var session = Create(mic, output);
            var start = await session.BeginIntervalAsync(cancellationToken);
            await session.EndIntervalAsync(start.Ticks + (TimeSpan.TicksPerMillisecond * 100), cancellationToken);
            output.FailStart = true;
            var resume = () => session.BeginIntervalAsync(cancellationToken);
            await resume.Should().ThrowAsync<RecordingException>();
            var result = await session.CompleteAsync(EmptyRecordingPolicy.Keep, cancellationToken);
            result.Frames.Value.Should().Be(1600);
            result.Published.Should().BeTrue();
        });

    /// <summary>Queue pressure interrupts without blocking the capture thread.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Queue pressure interrupts without blocking the capture thread")]
    public Task QueueOverflowShouldInterrupt() =>
        TestDeadline.Run(async cancellationToken =>
        {
            using var file = WaveDestination.Create(_folder.FullName, DateTime.Now);
            using var stalled = new ManualResetEventSlim();
            var fault = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var mic = new TestEndpoint();
            using var output = new TestEndpoint();
            var interval = new CaptureInterval("mic", "out", _clock,
                (_, loopback) => loopback ? output : mic, file,
                _ => stalled.Wait(TimeSpan.FromSeconds(3)), message => fault.TrySetResult(message.Message), 1, cancellationToken);
            await interval.Started;
            try
            {
                (await fault.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)).Should().Contain("queue");
            }
            finally
            {
                stalled.Set();
                await interval.StopAsync(_clock.Now, cancellationToken);
            }

            file.Complete(EmptyRecordingPolicy.Keep, null).Published.Should().BeTrue();
        });

    /// <summary>Concurrent filenames reserve separate paths and never overwrite.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Concurrent filenames reserve separate paths and never overwrite")]
    public void FileCollisionShouldChooseSuffix()
    {
        var now = DateTime.Now;
        using var first = WaveDestination.Create(_folder.FullName, now);
        using var next = WaveDestination.Create(_folder.FullName, now);
        var a = first.Complete(EmptyRecordingPolicy.Keep, null);
        var b = next.Complete(EmptyRecordingPolicy.Keep, null);
        a.Path.Should().NotBe(b.Path);
        b.Path!.Value.Should().EndWith("-1.wav");
        using var third = WaveDestination.Create(_folder.FullName, now);
        third.Complete(EmptyRecordingPolicy.Keep, null).Path!.Value.Should().EndWith("-2.wav");
    }

    /// <summary>Failed publication preserves the unfinished file and existing destination.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Failed publication preserves the unfinished file and existing destination")]
    public void PublicationFailureShouldPreservePartial()
    {
        using var writer = WaveDestination.Create(_folder.FullName, DateTime.Now);
        writer.Append(new Pcm16Block([1, 2, 3, 4]));
        var final = Path.ChangeExtension(_folder.GetFiles().Single().FullName, null);
        File.WriteAllText(final, "existing file");

        var result = writer.Complete(EmptyRecordingPolicy.Keep, null);

        result.Published.Should().BeFalse();
        result.Path!.Value.Should().EndWith(".partial");
        new FileInfo(result.Path!.Value).Length.Should().Be(48);
        File.ReadAllText(final).Should().Be("existing file");
    }

    /// <summary>Partial disk writes are truncated to the last complete block.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Partial disk writes are truncated to the last complete block")]
    public void DiskFailureShouldKeepEarlierFrames()
    {
        var path = Path.Combine(_folder.FullName, "sound.partial");
        var final = Path.Combine(_folder.FullName, "sound.wav");
        using var stream = new FailingFile(path);
        stream.Position = 44;
        using var writer = new WaveDestination(stream, path, final);
        writer.Append(new Pcm16Block(new byte[100]));
        stream.FailNext = true;

        var act = () => writer.Append(new Pcm16Block(new byte[100]));
        act.Should().Throw<RecordingException>().WithMessage("*disk*");
        act.Should().Throw<RecordingException>().WithMessage("*disk*");
        var result = writer.Complete(EmptyRecordingPolicy.Keep, "Writing stopped.");

        result.Published.Should().BeTrue();
        result.Frames.Value.Should().Be(50);
        File.ReadAllBytes(final).Length.Should().Be(144);
    }

    /// <summary>The RIFF boundary is checked before writing a block.</summary>
    [Trait("Category", "Unit")]
    [Fact(DisplayName = "The RIFF boundary is checked before writing a block")]
    public void LastBlockShouldFitBothRiffSizeFields()
    {
        var last = (uint.MaxValue - 36L) / 2;
        WaveDestination.CheckCapacity(last - 1, 1);
        var write = () => WaveDestination.CheckCapacity(last, 1);
        write.Should().Throw<RecordingException>().WithMessage("*RIFF*");
    }

    /// <summary>Driver failures are explained in English for the affected source.</summary>
    [Trait("Category", "Unit")]
    [Theory(DisplayName = "Driver failures are explained in English for the affected source")]
    [InlineData(false, -2147024891, "desktop apps")]
    [InlineData(true, -2147024891, "Computer audio access was denied")]
    [InlineData(true, unchecked((int)0x88890004), "disconnected")]
    [InlineData(false, unchecked((int)0x8889000A), "busy")]
    [InlineData(true, unchecked((int)0x88890008), "unsupported audio format")]
    public void DeviceErrorsShouldIdentifySourceAndCause(bool loopback, int code, string expected)
    {
        var native = new IOException("Native audio driver diagnostic", code);
        var translated = DeviceErrors.Translate(loopback, native);
        translated.Message.Should().Contain(expected);
        translated.Message.Should().NotContain(native.Message);
    }

    /// <summary>Infrastructure registers the clock, endpoint catalog and capture factory.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Infrastructure registers the clock, endpoint catalog and capture factory")]
    public void ServicesShouldResolve()
    {
        var config = new ConfigurationBuilder().Build();
        using var services = new ServiceCollection().AddMeetingRecorderApplication()
            .AddSingleton<IRecordingStore, WaveRecordingStore>()
            .AddMeetingRecorderInfrastructure(config)
            .BuildServiceProvider();
        services.GetRequiredService<IDeviceCatalog>().Should().BeOfType<EndpointCatalog>();
        services.GetRequiredService<ICaptureFactory>().Should().BeOfType<CaptureFactory>();
        services.GetRequiredService<IRecordingClock>().Now.Ticks.Should().BePositive();
        services.GetRequiredService<IRecorder>().Should().BeSameAs(services.GetRequiredService<IRecorder>());
    }

    /// <summary>A disk failure in the mixing worker interrupts and publishes exactly its valid prefix.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "A disk failure in the mixing worker interrupts and publishes exactly its valid prefix")]
    public Task WorkerWriteFailureShouldKeepOnePlayableBlock() =>
        TestDeadline.Run(async cancellationToken =>
        {
            var partial = Path.Combine(_folder.FullName, "output.partial");
            var final = Path.Combine(_folder.FullName, "output.wav");
            using var stream = new FailingFile(partial) { Position = 44, FailAt = 684 };
            using var file = new WaveDestination(stream, partial, final);
            using var mic = new TestEndpoint { NextPacket = MixingTests.Tone(false, 0, 0, 0.1, 880) };
            using var output = new TestEndpoint();
            long now = 0;
            var clock = new Mock<IRecordingClock>(MockBehavior.Strict);
            clock.SetupGet(c => c.Now).Returns(() => Interlocked.Read(ref now));
            using var session = new CaptureSession(new("m", "o", _folder.FullName), clock.Object,
                file, (_, loopback) => loopback ? output : mic);
            var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Interrupted += (_, _) => interrupted.TrySetResult();

            await session.BeginIntervalAsync(cancellationToken);
            Interlocked.Exchange(ref now, TimeSpan.TicksPerSecond);
            await interrupted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            var result = await session.CompleteAsync(EmptyRecordingPolicy.Keep, cancellationToken);

            result.Problem!.Message.Should().Contain("disk");
            result.Path!.Value.Should().Be(final);
            result.Frames.Value.Should().Be(320);
            result.Published.Should().BeTrue();
            var wav = await File.ReadAllBytesAsync(final, cancellationToken);
            wav.Length.Should().Be(684);
            BitConverter.ToInt32(wav, 40).Should().Be(640);
            wav.Skip(44).Should().Contain(value => value != 0);
            mic.Disposed.Should().BeTrue();
            output.Disposed.Should().BeTrue();
        });

    /// <summary>Real capture workers publish independent peaks and clear both on pause.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Real capture workers publish independent peaks and clear both on pause")]
    public Task CaptureMetersShouldUseOnlyTheirOwnActiveInterval() =>
        TestDeadline.Run(async cancellationToken =>
        {
            long now = 0;
            var clock = new Mock<IRecordingClock>(MockBehavior.Strict);
            clock.SetupGet(c => c.Now).Returns(() => Interlocked.Read(ref now));
            using var mic = new TestEndpoint { NextPacket = MixingTests.Tone(false, 0, 0, 0.1, 440) };
            using var output = new TestEndpoint();
            using var file = WaveDestination.Create(_folder.FullName, DateTime.Now);
            using var session = new CaptureSession(new("m", "o", _folder.FullName), clock.Object,
                file, (_, loopback) => loopback ? output : mic);

            await session.BeginIntervalAsync(cancellationToken);
            Interlocked.Exchange(ref now, TimeSpan.TicksPerSecond);
            await WaitForSignalAsync(() => session.Levels.Microphone.Value > 0.1f, cancellationToken);
            session.Levels.Computer.Value.Should().Be(0);
            await session.EndIntervalAsync(now, cancellationToken);
            session.Levels.Should().Be(default(Levels));

            output.NextPacket = MixingTests.Tone(true, 0, now, 0.1, 880);
            await session.BeginIntervalAsync(cancellationToken);
            Interlocked.Add(ref now, TimeSpan.TicksPerSecond);
            await WaitForSignalAsync(() => session.Levels.Computer.Value > 0.1f, cancellationToken);
            session.Levels.Microphone.Value.Should().Be(0);
            var result = await session.CompleteAsync(EmptyRecordingPolicy.Keep, cancellationToken);
            result.Frames.Value.Should().Be(32000);
        });

    /// <summary>A nonexistent Windows endpoint rolls back the file and allows a second attempt.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "A nonexistent Windows endpoint rolls back the file and allows a second attempt")]
    public Task MissingWindowsDeviceShouldRollBackBothStarts() =>
        TestDeadline.Run(async cancellationToken =>
        {
            var config = new ConfigurationBuilder().Build();
            using var services = new ServiceCollection().AddMeetingRecorderApplication()
                .AddSingleton<IRecordingStore, WaveRecordingStore>()
                .AddMeetingRecorderInfrastructure(config).BuildServiceProvider();
            var recorder = services.GetRequiredService<IRecorder>();
            var missing = Guid.NewGuid().ToString("D");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await recorder.StartAsync(new(missing, missing, _folder.FullName), cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                recorder.Snapshot.State.Should().Be(RecorderState.Error);
                recorder.Snapshot.Result!.Path.Should().BeNull();
                recorder.Snapshot.Result.Problem!.Message.Should().Contain("Microphone");
                _folder.GetFiles().Should().BeEmpty();
            }
        });

    /// <summary>Final header failure preserves the unfinished audio and interruption cause.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Final header failure preserves the unfinished audio and interruption cause")]
    public void HeaderFailureShouldKeepUnfinishedAudio()
    {
        var partial = Path.Combine(_folder.FullName, "sound.partial");
        var final = Path.Combine(_folder.FullName, "sound.wav");
        using var stream = new FailingFile(partial) { Position = 44 };
        using var writer = new WaveDestination(stream, partial, final);
        writer.Append(new Pcm16Block([1, 2, 3, 4]));
        stream.FailNext = true;
        var result = writer.Complete(EmptyRecordingPolicy.Keep, "Device disconnected.");

        result.Published.Should().BeFalse();
        result.Path!.Value.Should().Be(partial);
        result.Problem!.Message.Should().Contain("disconnected").And.Contain("finalized");
        File.Exists(final).Should().BeFalse();
        File.ReadAllBytes(partial).Skip(44).Should().Equal(1, 2, 3, 4);
    }

    /// <summary>The real capture factory reserves a file and discards an empty session.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "The real capture factory reserves a file and discards an empty session")]
    public Task UnstartedSessionShouldReleaseItsReservationAsync() =>
        TestDeadline.Run(async cancellationToken =>
        {
            var factory = new CaptureFactory(_clock, new WaveRecordingStore(), AudioCaptureOptions.Default);
            using (var session = await factory.OpenAsync(new("m", "o", _folder.FullName), cancellationToken))
            {
                _folder.GetFiles("*.partial").Should().ContainSingle();
            }

            _folder.GetFiles().Should().BeEmpty();
            var relative = () => factory.OpenAsync(new("m", "o", "relative"), cancellationToken);
            (await relative.Should().ThrowAsync<ArgumentException>()).WithMessage("*fully qualified*");
            var blocked = Path.Combine(_folder.FullName, "file");
            await File.WriteAllTextAsync(blocked, "existing", cancellationToken);
            var unavailable = () => factory.OpenAsync(new("m", "o", blocked), cancellationToken);
            (await unavailable.Should().ThrowAsync<RecordingException>()).WithMessage("*Cannot create*");
        });

    /// <summary>A live session rejects an overlapping interval and keeps its original handles.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "A live session rejects an overlapping interval and keeps its original handles")]
    public Task OverlappingIntervalsMustNotReplaceTheFirst() =>
        TestDeadline.Run(async token =>
        {
            using var mic = new TestEndpoint();
            using var output = new TestEndpoint();
            using var session = Create(mic, output);
            var start = await session.BeginIntervalAsync(token);
            Assert.Throws<InvalidOperationException>(() => { _ = session.BeginIntervalAsync(token); });
            await session.EndIntervalAsync(start, token);
            Assert.True(mic.Disposed);
            Assert.True(output.Disposed);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.CompleteAsync((EmptyRecordingPolicy)10, token));
            var saved = await session.CompleteAsync(EmptyRecordingPolicy.Keep, token);
            Assert.True(saved.Published);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => session.CompleteAsync(EmptyRecordingPolicy.Keep, token));
        });

    /// <summary>Cancellation before file creation leaves the destination directory untouched.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Cancellation before file creation leaves the destination directory untouched")]
    public Task CanceledStorageOperationsLeaveNoFiles() =>
        TestDeadline.Run(async token =>
        {
            var folder = Path.Combine(_folder.FullName, "new");
            using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new WaveRecordingStore().CreateAsync(new("mic", "out", folder), canceled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new FolderCatalog().ListAsync(folder, FolderLookup.NearestExisting, canceled.Token));
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new FolderCatalog().ListAsync(folder, (FolderLookup)10, token); });
            Assert.False(Directory.Exists(folder));
        });

    /// <summary>Failed publication reports the absence of both files when the temporary path is gone.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Failed publication reports the absence of both files when the temporary path is gone")]
    public void MissingTemporaryFileLeavesNoRecoveryLink()
    {
        using var stream = new MemoryStream();
        var missing = Path.Combine(_folder.FullName, "lost.wav.partial");
        var final = Path.Combine(_folder.FullName, "lost.wav");
        using var writer = new WaveDestination(stream, missing, final);
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Complete((EmptyRecordingPolicy)100, null));
        var result = writer.Complete(EmptyRecordingPolicy.Keep, null);
        Assert.False(result.Published);
        Assert.Null(result.Path);
        Assert.Contains("finalized", result.Problem!.Message, StringComparison.Ordinal);
        Assert.Throws<RecordingException>(() => new FolderLauncher().Open(Path.Combine(_folder.FullName, "gone", "lost.wav")));
    }

    /// <summary>Native failure filters distinguish transient device faults from programming errors.</summary>
    [Trait("Category", "Unit")]
    [Fact(DisplayName = "Native failure filters distinguish device faults from unrelated exceptions")]
    public void NativeFailuresRetainTheirCause()
    {
        Assert.True(DeviceErrors.IsDeviceError(new IOException()));
        Assert.True(DeviceErrors.IsDeviceError(new NotSupportedException()));
        Assert.True(DeviceErrors.IsDeviceError(new ArgumentException()));
        Assert.True(DeviceErrors.IsDeviceError(new InvalidOperationException()));
        Assert.True(DeviceErrors.IsDeviceError(new UnauthorizedAccessException()));
        Assert.False(DeviceErrors.IsDeviceError(new ArithmeticException()));
        Assert.Throws<ArgumentException>(() => new InputFormat(16000, 2, 16, false, 0).Decode([1, 2]));
    }

    /// <summary>Single-source capture opens only the selected endpoint on both intervals.</summary>
    [Theory(DisplayName = "Single-source recording reopens only its own device and writes a mode-specific WAV")]
    [Trait("Category", "Integration")]
    [InlineData(RecordingMode.Audio, true)]
    [InlineData(RecordingMode.Dictaphone, false)]
    public Task SingleSourceShouldCaptureAndResume(RecordingMode mode, bool loopback) =>
        TestDeadline.Run(async token =>
        {
            var settings = new RecordingSettings("mic", "out", _folder.FullName, mode);
            var store = new WaveRecordingStore();
            var file = await store.CreateAsync(settings, token);
            var clock = new Mock<IRecordingClock>();
            clock.SetupGet(c => c.Now).Returns(new CaptureTimestamp(0));
            var opened = new List<bool>();
            using var session = new CaptureSession(settings, clock.Object, file, (id, playback) =>
            {
                opened.Add(playback);
                Assert.Equal(loopback ? "out" : "mic", id.Value);
                return new TestEndpoint { NextPacket = MixingTests.Tone(playback, 0, 0, 0.1, 440) };
            });
            for (var interval = 0; interval < 2; interval++)
            {
                await session.BeginIntervalAsync(token);
                await session.EndIntervalAsync(TimeSpan.TicksPerSecond / 10, token);
            }

            var result = await session.CompleteAsync(EmptyRecordingPolicy.Keep, token);
            Assert.Equal(new[] { loopback, loopback }, opened);
            Assert.StartsWith(mode + "-", Path.GetFileName(result.Path!.Value), StringComparison.Ordinal);
            Assert.Equal(3200, result.Frames.Value);
            var bytes = await File.ReadAllBytesAsync(result.Path.Value, token);
            Assert.Contains(bytes.Skip(44), sample => sample != 0);
        });

    /// <summary>Rollover leaves endpoints running and carries the active frame count across a pause.</summary>
    [Fact(DisplayName = "Capture continues across segment boundaries and omits paused clock time")]
    [Trait("Category", "Integration")]
    public Task RolloverShouldPreserveSessionAcrossPause() => TestDeadline.Run(async token =>
    {
        var now = 0L;
        var clock = new Mock<IRecordingClock>();
        clock.SetupGet(c => c.Now).Returns(() => Interlocked.Read(ref now));
        var opens = 0;
        var mode = RecordingMode.Dictaphone;
        var settings = new RecordingSettings("mic", null, _folder.FullName, mode, RecordingLayout.QuarterHourParts);
        using var file = new SegmentedWaveDestination(settings.Folder, mode, DateTime.Now, 8);
        using var session = new CaptureSession(settings, clock.Object, file, (_, _) =>
        {
            Interlocked.Increment(ref opens);
            return new TestEndpoint();
        });
        await session.BeginIntervalAsync(token);
        await session.EndIntervalAsync(12 * RecordingFormat.TicksPerFrame, token);
        Assert.Single(_folder.GetFiles("*.wav"));
        Interlocked.Exchange(ref now, TimeSpan.TicksPerHour);
        await session.BeginIntervalAsync(token);
        await session.EndIntervalAsync(now + (8 * RecordingFormat.TicksPerFrame), token);
        var result = await session.CompleteAsync(EmptyRecordingPolicy.Keep, token);
        Assert.Equal(2, opens);
        Assert.Equal(20, result.Frames.Value);
        Assert.Equal(3, result.PublishedFiles.Count);
    });

    /// <summary>Each preflight mode uses only its selected endpoints and returns five seconds of mixed PCM.</summary>
    [Trait("Category", "Integration")]
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task SoundCheckShouldCaptureSelectedSources(bool hasMic, bool hasOutput) => TestDeadline.Run(async token =>
    {
        var ticks = -TimeSpan.TicksPerMillisecond * 50;
        var clock = new Mock<IRecordingClock>();
        clock.SetupGet(c => c.Now).Returns(() => Interlocked.Add(ref ticks, TimeSpan.TicksPerMillisecond * 50));
        using var mic = new TestEndpoint { NextPacket = MixingTests.Tone(false, 0, 0, 0.1, 440) };
        using var output = new TestEndpoint { NextPacket = MixingTests.Tone(true, 0, 0, 0.1, 880) };
        var tone = new Mock<Func<EndpointId, CancellationToken, Task>>();
        tone.Setup(play => play("headphones", token)).Returns(Task.CompletedTask);
        var check = new SoundCheck(clock.Object, (id, loopback) =>
        {
            Assert.Equal(loopback ? "headphones" : "usb-mic", id.Value);
            return loopback ? output : mic;
        }, tone.Object, 512);
        var levels = new Levels();
        var progress = new Mock<IProgress<Levels>>();
        progress.Setup(p => p.Report(It.IsAny<Levels>())).Callback<Levels>(value => levels = value);
        var sample = await check.CaptureAsync(hasMic ? new EndpointId("usb-mic") : null,
            hasOutput ? new EndpointId("headphones") : null, progress.Object, token);

        Assert.Equal(5 * RecordingFormat.SampleRate * sizeof(short), sample.Length);
        Assert.Contains(sample, value => value != 0);
        Assert.Equal(hasMic, levels.Microphone.Value > 0);
        Assert.Equal(hasOutput, levels.Computer.Value > 0);
        Assert.Equal(hasMic, mic.Disposed);
        Assert.Equal(hasOutput, output.Disposed);
        tone.Verify(play => play("headphones", token), hasOutput ? Times.Once : Times.Never);
        Assert.Empty(_folder.GetFiles());
    });

    /// <summary>Closing during the tone releases both capture devices before returning to the recorder.</summary>
    [Trait("Category", "Integration")]
    [Fact]
    public Task CanceledSoundCheckShouldReleaseDevices() => TestDeadline.Run(async token =>
    {
        using var mic = new TestEndpoint();
        using var output = new TestEndpoint();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var playing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new SoundCheck(_clock, (_, loopback) => loopback ? output : mic, (_, ct) =>
        {
            playing.SetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }, 512);
        var sample = check.CaptureAsync("mic", "out", Mock.Of<IProgress<Levels>>(), cancel.Token);
        await playing.Task.WaitAsync(token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sample);
        Assert.True(mic.Disposed && output.Disposed);
    });

    /// <inheritdoc/>
    public void Dispose() => _folder.Delete(true);

    private static async Task WaitForSignalAsync(Func<bool> present, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (!present())
        {
            await Task.Delay(5, deadline.Token);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "CaptureSession owns and disposes the destination returned here.")]
    private CaptureSession Create(TestEndpoint mic, TestEndpoint output) =>
        new(new("mic", "out", _folder.FullName), _clock,
            WaveDestination.Create(_folder.FullName, DateTime.Now),
            (_, loopback) => loopback ? output : mic);

    private sealed class TestEndpoint : IInputEndpoint
    {
        internal volatile bool FailRead;
        internal AudioPacket? NextPacket;

        public TimeSpan EnginePeriod => TimeSpan.FromMilliseconds(10);

        internal bool FailStart { get; set; }

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
        internal long? FailAt { get; set; }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (FailNext || Position == FailAt)
            {
                FailNext = false;
                FailAt = null;
                base.Write(buffer[..2]);
                throw new IOException("Simulated disk failure");
            }

            base.Write(buffer);
        }
    }
}
