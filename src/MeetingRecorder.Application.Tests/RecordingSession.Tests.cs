using FluentAssertions;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.TestSupport;

using Moq;

namespace MeetingRecorder.Application.Tests;

/// <summary>Tests recorder commands and cancellation.</summary>
[Trait("Category", "Unit")]
public sealed class RecordingSessionTests : IDisposable
{
    private readonly Mock<IRecordingClock> _clock = new(MockBehavior.Strict);
    private readonly Mock<ICaptureFactory> _factory = new(MockBehavior.Strict);
    private readonly Mock<ICaptureSession> _session = new(MockBehavior.Strict);
    private readonly RecordingSettings _settings = new("mic", "out", Path.GetTempPath());
    private readonly RecordingCoordinator _recorder;
    private long _now;
    private long _frames;

    /// <summary>Creates a coordinator with a controllable capture session and clock.</summary>
    public RecordingSessionTests()
    {
        _clock.SetupGet(c => c.Now).Returns(() => _now);
        _factory.Setup(f => f.OpenAsync(_settings, It.Is<CancellationToken>(t => t.CanBeCanceled))).ReturnsAsync(_session.Object);
        _session.Setup(s => s.BeginIntervalAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).Returns(() => Task.FromResult(new CaptureTimestamp(_now)));
        _session.Setup(s => s.EndIntervalAsync(It.IsAny<CaptureTimestamp>(), It.Is<CancellationToken>(t => t.CanBeCanceled))).Returns(Task.CompletedTask);
        _session.Setup(s => s.CompleteAsync(It.IsAny<EmptyRecordingPolicy>(), It.Is<CancellationToken>(t => t.CanBeCanceled))).Returns(() =>
            Task.FromResult(new RecordingResult(Path.Combine(Path.GetTempPath(), "meeting.wav"), _frames, null, true)));
        _session.SetupGet(s => s.Problem).Returns((RecordingProblem?)null);
        _session.Setup(s => s.Dispose());
        _recorder = new(_factory.Object, _clock.Object);
    }

    /// <summary>Pauses freeze elapsed time and finish while paused closes once.</summary>
    [Fact(DisplayName = "Pauses freeze elapsed time and finish while paused closes once")]
    public Task PauseShouldFreezeElapsedTime() =>
        TestDeadline.Run(async cancellationToken =>
        {
            // Arrange
            await _recorder.StartAsync(_settings, cancellationToken);
            _recorder.Snapshot.State.Should().Be(RecorderState.Recording);

            // Act
            for (var i = 1; i <= 2; i++)
            {
                _now += TimeSpan.TicksPerSecond * 5;
                await _recorder.PauseAsync(cancellationToken);
                _now += TimeSpan.TicksPerSecond * 3;
                _recorder.Elapsed.Value.Should().Be(TimeSpan.FromSeconds(i * 5));
                _recorder.Levels.Should().Be(default(Levels));
                if (i == 1)
                {
                    await _recorder.ResumeAsync(cancellationToken);
                }
            }

            _frames = 160_000;
            await Task.WhenAll(_recorder.FinishAsync(cancellationToken), _recorder.FinishAsync(cancellationToken));

            // Assert
            _recorder.Snapshot.State.Should().Be(RecorderState.Saved);
            _recorder.Elapsed.Value.Should().Be(TimeSpan.FromSeconds(10));
            _session.Verify(s => s.CompleteAsync(EmptyRecordingPolicy.Keep, It.Is<CancellationToken>(t => t.CanBeCanceled)), Times.Once);
            _session.Verify(s => s.Dispose(), Times.Once);
        });

    /// <summary>Repeated starts wait for both devices and create one session.</summary>
    [Fact(DisplayName = "Repeated starts wait for both devices and create one session")]
    public Task ConcurrentStartsShouldShareOneSession() =>
        TestDeadline.Run(async cancellationToken =>
        {
            var connected = new TaskCompletionSource<CaptureTimestamp>(TaskCreationOptions.RunContinuationsAsynchronously);
            _session.Setup(s => s.BeginIntervalAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).Returns(connected.Task);
            var first = _recorder.StartAsync(_settings, cancellationToken);
            var second = _recorder.StartAsync(_settings, cancellationToken);

            _recorder.Snapshot.State.Should().Be(RecorderState.Starting);
            connected.SetResult(100);
            await Task.WhenAll(first, second);
            _factory.Verify(f => f.OpenAsync(_settings, It.Is<CancellationToken>(t => t.CanBeCanceled)), Times.Once);
            await _recorder.FinishAsync(cancellationToken);
        });

    /// <summary>Failed startup cleans up and allows a fresh attempt.</summary>
    [Fact(DisplayName = "Failed startup cleans up and allows a fresh attempt")]
    public Task PartialStartupShouldAllowRetry() =>
        TestDeadline.Run(async cancellationToken =>
        {
            _session.SetupSequence(s => s.BeginIntervalAsync(It.Is<CancellationToken>(t => t.CanBeCanceled)))
                .ThrowsAsync(new RecordingException("Microphone access was denied."))
                .ReturnsAsync(123);
            _session.Setup(s => s.CompleteAsync(EmptyRecordingPolicy.Discard, It.Is<CancellationToken>(t => t.CanBeCanceled)))
                .ReturnsAsync(new RecordingResult(null, 0, null, false));

            await _recorder.StartAsync(_settings, cancellationToken);
            _recorder.Snapshot.State.Should().Be(RecorderState.Error);
            _recorder.Snapshot.Result!.Problem!.Message.Should().Contain("Microphone");
            _session.Verify(s => s.Dispose(), Times.Once);

            await _recorder.StartAsync(_settings, cancellationToken);
            _recorder.Snapshot.State.Should().Be(RecorderState.Recording);
            await _recorder.FinishAsync(cancellationToken);
        });

    /// <summary>Shutdown during file creation waits for startup and publication.</summary>
    [Fact(DisplayName = "Shutdown during file creation waits for startup and publication")]
    public Task FinishDuringStartupShouldReleaseTheNewSession() =>
        TestDeadline.Run(async cancellationToken =>
        {
            var release = new TaskCompletionSource<ICaptureSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            _factory.Setup(f => f.OpenAsync(_settings, It.Is<CancellationToken>(t => t.CanBeCanceled))).Returns(release.Task);

            var startup = _recorder.StartAsync(_settings, cancellationToken);
            var finish = _recorder.FinishAsync(cancellationToken);
            release.SetResult(_session.Object);
            await Task.WhenAll(startup, finish);

            _recorder.Snapshot.State.Should().Be(RecorderState.Saved);
            _session.Verify(s => s.Dispose(), Times.Once);
        });

    /// <summary>A device failure and window finish await one publication.</summary>
    [Fact(DisplayName = "A device failure and window finish await one publication")]
    public Task DeviceLossAndFinishShouldFinalizeOnce() =>
        TestDeadline.Run(async cancellationToken =>
        {
            await _recorder.StartAsync(_settings, cancellationToken);
            var publication = new TaskCompletionSource<RecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _session.SetupGet(s => s.Problem).Returns("Computer audio was disconnected.");
            _session.Setup(s => s.CompleteAsync(EmptyRecordingPolicy.Keep, It.Is<CancellationToken>(t => t.CanBeCanceled))).Returns(publication.Task);

            RaiseDeviceFailure();
            var close = _recorder.FinishAsync(cancellationToken);
            close.IsCompleted.Should().BeFalse();
            publication.SetResult(new(Path.Combine(Path.GetTempPath(), "meeting.wav"), 8000, null, true));
            await close;

            _recorder.Snapshot.State.Should().Be(RecorderState.Error);
            _recorder.Snapshot.Result!.Path!.Value.Should().Be(Path.Combine(Path.GetTempPath(), "meeting.wav"));
            _session.Verify(s => s.Dispose(), Times.Once);
        });

    /// <summary>Unavailable commands leave the current session untouched.</summary>
    [Fact(DisplayName = "Unavailable commands leave the current session untouched")]
    public Task InvalidTransitionsShouldBeIdempotent() =>
        TestDeadline.Run(async cancellationToken =>
        {
            await _recorder.PauseAsync(cancellationToken);
            await _recorder.ResumeAsync(cancellationToken);
            await _recorder.FinishAsync(cancellationToken);
            _recorder.Snapshot.State.Should().Be(RecorderState.Ready);
            await _recorder.StartAsync(_settings, cancellationToken);
            _session.SetupGet(s => s.Levels).Returns(new Levels(0.1f, 0.25f));
            _recorder.Levels.Should().Be(new Levels(0.1f, 0.25f));
            await _recorder.StartAsync(_settings, cancellationToken);
            await _recorder.ResumeAsync(cancellationToken);
            await _recorder.PauseAsync(cancellationToken);
            await _recorder.PauseAsync(cancellationToken);
            await _recorder.StartAsync(_settings, cancellationToken);
            _factory.Verify(f => f.OpenAsync(_settings, It.Is<CancellationToken>(t => t.CanBeCanceled)), Times.Once);
            _session.Verify(s => s.BeginIntervalAsync(It.Is<CancellationToken>(t => t.CanBeCanceled)), Times.Once);
            _session.Verify(s => s.EndIntervalAsync(It.IsAny<CaptureTimestamp>(), It.Is<CancellationToken>(t => t.CanBeCanceled)), Times.Once);
            _recorder.Snapshot.State.Should().Be(RecorderState.Paused);
        });

    /// <summary>An unavailable destination has no audio session to dispose.</summary>
    [Fact(DisplayName = "An unavailable destination has no audio session to dispose")]
    public Task FileCreationFailureShouldReportNoOutput() =>
        TestDeadline.Run(async cancellationToken =>
        {
            _factory.Setup(f => f.OpenAsync(_settings, It.Is<CancellationToken>(t => t.CanBeCanceled))).Throws(new RecordingException("Folder unavailable."));
            await _recorder.StartAsync(_settings, cancellationToken);
            _recorder.Snapshot.State.Should().Be(RecorderState.Error);
            _recorder.Snapshot.Result.Should().Be(new RecordingResult(null, 0, "Folder unavailable.", false));
            _session.Verify(s => s.BeginIntervalAsync(It.Is<CancellationToken>(t => t.CanBeCanceled)), Times.Never);
        });

    /// <summary>A device failure while pausing publishes the available audio.</summary>
    [Fact(DisplayName = "A device failure while pausing publishes the available audio")]
    public Task PauseFailureShouldFinalizeInsteadOfEnteringPaused() =>
        TestDeadline.Run(async cancellationToken =>
        {
            await _recorder.StartAsync(_settings, cancellationToken);
            _session.SetupGet(s => s.Problem).Returns("The microphone was disconnected.");
            await _recorder.PauseAsync(cancellationToken);
            _recorder.Snapshot.State.Should().Be(RecorderState.Error);
            _recorder.Snapshot.Result!.Published.Should().BeTrue();
            _recorder.Snapshot.Result.Problem!.Message.Should().Contain("microphone");
            _session.Verify(s => s.Dispose(), Times.Once);
        });

    /// <summary>A failed resume and failed publication report both causes and the recovery path.</summary>
    [Fact(DisplayName = "A failed resume and failed publication report both causes and the recovery path")]
    public Task ResumeAndPublicationFailureShouldKeepBothErrors() =>
        TestDeadline.Run(async cancellationToken =>
        {
            await _recorder.StartAsync(_settings, cancellationToken);
            await _recorder.PauseAsync(cancellationToken);
            _session.Setup(s => s.BeginIntervalAsync(It.Is<CancellationToken>(t => t.CanBeCanceled)))
                .ThrowsAsync(new RecordingException("Microphone disconnected."));
            _session.Setup(s => s.CompleteAsync(EmptyRecordingPolicy.Keep, It.Is<CancellationToken>(t => t.CanBeCanceled))).ReturnsAsync(
                new RecordingResult(Path.Combine(Path.GetTempPath(), "meeting.wav.partial"), 8000, "WAV could not be published.", false));
            await _recorder.ResumeAsync(cancellationToken);
            _recorder.Snapshot.State.Should().Be(RecorderState.Error);
            _recorder.Snapshot.Result!.Problem!.Message.Should().Contain("Microphone disconnected.")
                .And.Contain("WAV could not be published.");
            _recorder.Snapshot.Result.Path!.Value.Should().Be(Path.Combine(Path.GetTempPath(), "meeting.wav.partial"));
        });

    /// <summary>Canceling a pending device start finalizes the file and permits a retry.</summary>
    [Theory(DisplayName = "Canceling a pending device start finalizes the file and permits a retry")]
    [InlineData(false)]
    [InlineData(true)]
    public Task CanceledDeviceStartShouldReleaseTheSession(bool resume) =>
        TestDeadline.Run(async deadline =>
        {
            if (resume)
            {
                await _recorder.StartAsync(_settings, deadline);
                await _recorder.PauseAsync(deadline);
            }

            using var command = CancellationTokenSource.CreateLinkedTokenSource(deadline);
            var pending = new TaskCompletionSource<CaptureTimestamp>(TaskCreationOptions.RunContinuationsAsynchronously);
            _session.Setup(s => s.BeginIntervalAsync(command.Token)).Returns(pending.Task.WaitAsync(command.Token));
            var start = resume ? _recorder.ResumeAsync(command.Token) : _recorder.StartAsync(_settings, command.Token);
            await command.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);

            Assert.Equal(RecorderState.Error, _recorder.Snapshot.State);
            Assert.Contains("canceled", _recorder.Snapshot.Result!.Problem!.Message, StringComparison.Ordinal);
            var policy = resume ? EmptyRecordingPolicy.Keep : EmptyRecordingPolicy.Discard;
            _session.Verify(s => s.CompleteAsync(policy,
                It.Is<CancellationToken>(t => t.CanBeCanceled && !t.IsCancellationRequested)), Times.Once);
            _session.Verify(s => s.Dispose(), Times.Once);
            await _recorder.StartAsync(_settings, deadline);
            Assert.Equal(RecorderState.Recording, _recorder.Snapshot.State);
        });

    /// <summary>Canceling a queued command leaves the active startup and its token untouched.</summary>
    [Fact(DisplayName = "Canceling a queued command leaves the active startup and its token untouched")]
    public Task CanceledWaitShouldNotStopTheOwner() =>
        TestDeadline.Run(async deadline =>
        {
            var opened = new TaskCompletionSource<ICaptureSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            _factory.Setup(f => f.OpenAsync(_settings, deadline)).Returns(opened.Task.WaitAsync(deadline));
            var start = _recorder.StartAsync(_settings, deadline);
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(deadline);
            var finish = _recorder.FinishAsync(waiting.Token);
            await waiting.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => finish);
            Assert.Equal(RecorderState.Starting, _recorder.Snapshot.State);
            opened.SetResult(_session.Object);
            await start;
            _session.Verify(s => s.BeginIntervalAsync(deadline), Times.Once);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _recorder.PauseAsync(waiting.Token));
            Assert.Equal(RecorderState.Recording, _recorder.Snapshot.State);
        });

    /// <summary>An accepted finish retains a live publication token after its caller cancels.</summary>
    [Fact(DisplayName = "An accepted finish retains a live publication token after its caller cancels")]
    public Task AcceptedFinishShouldPublishDespiteLateCancellation() =>
        TestDeadline.Run(async deadline =>
        {
            await _recorder.StartAsync(_settings, deadline);
            using var command = CancellationTokenSource.CreateLinkedTokenSource(deadline);
            var published = new TaskCompletionSource<RecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _session.Setup(s => s.CompleteAsync(EmptyRecordingPolicy.Keep, It.Is<CancellationToken>(t => t.CanBeCanceled)))
                .Returns((EmptyRecordingPolicy _, CancellationToken token) => published.Task.WaitAsync(token));
            var finish = _recorder.FinishAsync(command.Token);
            await command.CancelAsync();
            Assert.False(finish.IsCompleted);
            published.SetResult(new(Path.Combine(Path.GetTempPath(), "saved.wav"), 1, null, true));
            await finish.WaitAsync(deadline);
            Assert.Equal(RecorderState.Saved, _recorder.Snapshot.State);
        });

    /// <summary>An independently canceled audio adapter releases the session and reports a terminal outcome.</summary>
    [Theory(DisplayName = "Independent device cancellation finalizes the session and leaves the caller usable")]
    [InlineData(false)]
    [InlineData(true)]
    public Task DeviceCancellationShouldReleaseTheSession(bool resuming) =>
        TestDeadline.Run(async token =>
        {
            if (resuming)
            {
                await _recorder.StartAsync(_settings, token);
                await _recorder.PauseAsync(token);
            }

            using var device = new CancellationTokenSource();
            await device.CancelAsync();
            _session.Setup(s => s.BeginIntervalAsync(token)).Returns(Task.FromCanceled<CaptureTimestamp>(device.Token));
            if (resuming)
            {
                await _recorder.ResumeAsync(token);
            }
            else
            {
                await _recorder.StartAsync(_settings, token);
            }

            Assert.Equal(RecorderState.Error, _recorder.Snapshot.State);
            Assert.Contains("canceled", _recorder.Snapshot.Result!.Problem!.Message, StringComparison.Ordinal);
            _session.Verify(s => s.Dispose(), Times.Once);
        });

    /// <inheritdoc/>
    public void Dispose() => _recorder.Dispose();

    // WASAPI signals a synchronous EventHandler from the capture thread.
    private void RaiseDeviceFailure() =>
        _session.Raise(s => s.Interrupted += null, _session.Object, EventArgs.Empty);
}
