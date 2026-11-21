using FluentAssertions;

using MeetingRecorder.Application.Abstractions;
using MeetingRecorder.Domain;

using Moq;

namespace MeetingRecorder.Application.Tests;

[Trait("Category", "Unit")]
public sealed class RecordingSessionTests : IDisposable
{
    private readonly Mock<IRecordingClock> _clock = new(MockBehavior.Strict);
    private readonly Mock<ICaptureFactory> _factory = new(MockBehavior.Strict);
    private readonly Mock<ICaptureSession> _session = new(MockBehavior.Strict);
    private readonly RecordingSettings _settings = new("mic", "out", "folder");
    private readonly RecordingCoordinator _recorder;
    private long _now;
    private long _frames;

    public RecordingSessionTests()
    {
        _clock.SetupGet(c => c.Now).Returns(() => _now);
        _factory.Setup(f => f.Open(_settings)).Returns(_session.Object);
        _session.Setup(s => s.BeginIntervalAsync()).Returns(() => Task.FromResult(_now));
        _session.Setup(s => s.EndIntervalAsync(It.IsAny<long>())).Returns(Task.CompletedTask);
        _session.Setup(s => s.CompleteAsync(It.IsAny<bool>())).Returns(() =>
            Task.FromResult(new RecordingResult("meeting.wav", _frames, null, true)));
        _session.SetupGet(s => s.Problem).Returns((string?)null);
        _session.Setup(s => s.Dispose());
        _recorder = new(_factory.Object, _clock.Object);
    }

    [Fact(DisplayName = "Pauses freeze elapsed time and finish while paused closes once")]
    public async Task PauseShouldFreezeElapsedTime()
    {
        // Arrange
        await _recorder.StartAsync(_settings);
        _recorder.Snapshot.State.Should().Be(RecorderState.Recording);

        // Act
        for (var i = 1; i <= 2; i++)
        {
            _now += TimeSpan.TicksPerSecond * 5;
            await _recorder.PauseAsync();
            _now += TimeSpan.TicksPerSecond * 3;
            _recorder.Elapsed.Should().Be(TimeSpan.FromSeconds(i * 5));
            _recorder.Levels.Should().Be(default(Levels));
            if (i == 1)
            {
                await _recorder.ResumeAsync();
            }
        }

        _frames = 160_000;
        await Task.WhenAll(_recorder.FinishAsync(), _recorder.FinishAsync());

        // Assert
        _recorder.Snapshot.State.Should().Be(RecorderState.Saved);
        _recorder.Elapsed.Should().Be(TimeSpan.FromSeconds(10));
        _session.Verify(s => s.CompleteAsync(false), Times.Once);
        _session.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact(DisplayName = "Repeated starts wait for both devices and create one session")]
    public async Task ConcurrentStartsShouldShareOneSession()
    {
        var connected = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.Setup(s => s.BeginIntervalAsync()).Returns(connected.Task);
        var first = _recorder.StartAsync(_settings);
        var second = _recorder.StartAsync(_settings);

        _recorder.Snapshot.State.Should().Be(RecorderState.Starting);
        connected.SetResult(100);
        await Task.WhenAll(first, second);
        _factory.Verify(f => f.Open(_settings), Times.Once);
        await _recorder.FinishAsync();
    }

    [Fact(DisplayName = "Failed startup cleans up and allows a fresh attempt")]
    public async Task PartialStartupShouldAllowRetry()
    {
        _session.SetupSequence(s => s.BeginIntervalAsync())
            .ThrowsAsync(new RecordingException("Microphone access was denied."))
            .ReturnsAsync(123);
        _session.Setup(s => s.CompleteAsync(true))
            .ReturnsAsync(new RecordingResult(null, 0, null, false));

        await _recorder.StartAsync(_settings);
        _recorder.Snapshot.State.Should().Be(RecorderState.Error);
        _recorder.Snapshot.Result!.Problem.Should().Contain("Microphone");
        _session.Verify(s => s.Dispose(), Times.Once);

        await _recorder.StartAsync(_settings);
        _recorder.Snapshot.State.Should().Be(RecorderState.Recording);
        await _recorder.FinishAsync();
    }

    [Fact(DisplayName = "Shutdown during file creation waits for startup and publication")]
    public async Task FinishDuringStartupShouldReleaseTheNewSession()
    {
        using var release = new ManualResetEventSlim();
        _factory.Setup(f => f.Open(_settings)).Returns(() =>
        {
            release.Wait();
            return _session.Object;
        });

        var startup = _recorder.StartAsync(_settings);
        var finish = _recorder.FinishAsync();
        release.Set();
        await Task.WhenAll(startup, finish);

        _recorder.Snapshot.State.Should().Be(RecorderState.Saved);
        _session.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact(DisplayName = "A device failure and window finish await one publication")]
    public async Task DeviceLossAndFinishShouldFinalizeOnce()
    {
        await _recorder.StartAsync(_settings);
        var publication = new TaskCompletionSource<RecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.SetupGet(s => s.Problem).Returns("Computer audio was disconnected.");
        _session.Setup(s => s.CompleteAsync(false)).Returns(publication.Task);

        RaiseDeviceFailure();
        var close = _recorder.FinishAsync();
        close.IsCompleted.Should().BeFalse();
        publication.SetResult(new("meeting.wav", 8000, null, true));
        await close;

        _recorder.Snapshot.State.Should().Be(RecorderState.Error);
        _recorder.Snapshot.Result!.Path.Should().Be("meeting.wav");
        _session.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact(DisplayName = "Unavailable commands leave the current session untouched")]
    public async Task InvalidTransitionsShouldBeIdempotent()
    {
        await _recorder.PauseAsync();
        await _recorder.ResumeAsync();
        await _recorder.FinishAsync();
        _recorder.Snapshot.State.Should().Be(RecorderState.Ready);
        await _recorder.StartAsync(_settings);
        _session.SetupGet(s => s.Levels).Returns(new Levels(0.1f, 0.25f));
        _recorder.Levels.Should().Be(new Levels(0.1f, 0.25f));
        await _recorder.StartAsync(_settings);
        await _recorder.ResumeAsync();
        await _recorder.PauseAsync();
        await _recorder.PauseAsync();
        await _recorder.StartAsync(_settings);
        _factory.Verify(f => f.Open(_settings), Times.Once);
        _session.Verify(s => s.BeginIntervalAsync(), Times.Once);
        _session.Verify(s => s.EndIntervalAsync(It.IsAny<long>()), Times.Once);
        _recorder.Snapshot.State.Should().Be(RecorderState.Paused);
    }

    [Fact(DisplayName = "An unavailable destination has no audio session to dispose")]
    public async Task FileCreationFailureShouldReportNoOutput()
    {
        _factory.Setup(f => f.Open(_settings)).Throws(new RecordingException("Folder unavailable."));
        await _recorder.StartAsync(_settings);
        _recorder.Snapshot.State.Should().Be(RecorderState.Error);
        _recorder.Snapshot.Result.Should().Be(new RecordingResult(null, 0, "Folder unavailable.", false));
        _session.Verify(s => s.BeginIntervalAsync(), Times.Never);
    }

    [Fact(DisplayName = "A device failure while pausing publishes the available audio")]
    public async Task PauseFailureShouldFinalizeInsteadOfEnteringPaused()
    {
        await _recorder.StartAsync(_settings);
        _session.SetupGet(s => s.Problem).Returns("The microphone was disconnected.");
        await _recorder.PauseAsync();
        _recorder.Snapshot.State.Should().Be(RecorderState.Error);
        _recorder.Snapshot.Result!.Published.Should().BeTrue();
        _recorder.Snapshot.Result.Problem.Should().Contain("microphone");
        _session.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact(DisplayName = "A failed resume and failed publication report both causes and the recovery path")]
    public async Task ResumeAndPublicationFailureShouldKeepBothErrors()
    {
        await _recorder.StartAsync(_settings);
        await _recorder.PauseAsync();
        _session.Setup(s => s.BeginIntervalAsync())
            .ThrowsAsync(new RecordingException("Microphone disconnected."));
        _session.Setup(s => s.CompleteAsync(false)).ReturnsAsync(
            new RecordingResult("meeting.wav.partial", 8000, "WAV could not be published.", false));
        await _recorder.ResumeAsync();
        _recorder.Snapshot.State.Should().Be(RecorderState.Error);
        _recorder.Snapshot.Result!.Problem.Should().Contain("Microphone disconnected.")
            .And.Contain("WAV could not be published.");
        _recorder.Snapshot.Result.Path.Should().Be("meeting.wav.partial");
    }

    // WASAPI signals a synchronous EventHandler from the capture thread.
    private void RaiseDeviceFailure() =>
        _session.Raise(s => s.Interrupted += null, _session.Object, EventArgs.Empty);

    public void Dispose() => _recorder.Dispose();
}
