using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

using MeetingRecorder.Abstractions;
using MeetingRecorder.DependencyInjection;
using MeetingRecorder.Domain;
using MeetingRecorder.TestSupport;
using MeetingRecorder.ViewModels;
using MeetingRecorder.Views;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace MeetingRecorder.Tests;

public sealed partial class RecorderViewModelTests
{
    /// <summary>The floating recorder controls the same session and restores the original window before saving completes.</summary>
    [Trait("Category", "Integration")]
    [Theory(DisplayName = "Compact controls pin the recording and Finish immediately restores its full window")]
    [InlineData(false)]
    [InlineData(true)]
    public Task CompactViewShouldRestoreTheRecorder(bool maximized) =>
        TestDeadline.Run(token => OnDispatcherAsync(ct => CheckCompactViewAsync(maximized, ct), token));

    private static async Task CheckCompactViewAsync(bool maximized, CancellationToken cancellationToken)
    {
        var state = RecorderState.Ready;
        var elapsed = TimeSpan.FromSeconds(38);
        var files = new[] { @"C:\Recordings\Part-0001.wav", @"C:\Recordings\Part-0002.wav" };
        var saved = RecordingResult.CombineParts([.. files.Select(file => new RecordingResult(file, 16000, null, true))], null);
        var recorder = ReadyRecorder();
        recorder.SetupGet(r => r.Snapshot).Returns(() => TestSnapshot(state, saved));
        recorder.SetupGet(r => r.Elapsed).Returns(() => elapsed);
        recorder.Setup(r => r.PauseAsync(It.Is<CancellationToken>(t => t.CanBeCanceled)))
            .Callback(() => state = RecorderState.Paused).Returns(Task.CompletedTask);
        recorder.Setup(r => r.ResumeAsync(It.Is<CancellationToken>(t => t.CanBeCanceled)))
            .Callback(() => state = RecorderState.Recording).Returns(Task.CompletedTask);
        var publication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.Setup(r => r.FinishAsync(It.Is<CancellationToken>(t => t.CanBeCanceled)))
            .Callback(() => state = RecorderState.Saving).Returns(publication.Task);
        var catalog = new Mock<IDeviceCatalog>();
        catalog.Setup(c => c.ReadAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).ReturnsAsync(new Devices([], []));
        var timer = new TestTimer();
        using var services = new ServiceCollection().AddMeetingRecorderPresentation()
            .AddSingleton(Mock.Of<ISoundCheck>())
            .AddSingleton(recorder.Object).AddSingleton(catalog.Object).AddSingleton<IUiTimer>(timer).BuildServiceProvider();
        var window = services.GetRequiredService<MainWindow>();
        var model = services.GetRequiredService<RecorderViewModel>();
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        if (maximized)
        {
            window.Opacity = 0;
        }

        window.Show();
        try
        {
            if (maximized)
            {
                window.WindowState = WindowState.Maximized;
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var originalState = window.WindowState;
            var bounds = window.RestoreBounds;
            var controls = Descendants(window).OfType<Button>().ToArray();
            Button Button(string label)
            {
                return controls.Single(b => Equals(b.Content, label));
            }

            void Click(string label)
            {
                var button = Button(label);
                Assert.True(button.IsVisible && button.IsEnabled, label);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                window.UpdateLayout();
            }

            Assert.False(Button("Compact view").IsVisible);
            state = RecorderState.Recording;
            timer.Pulse();
            Click("Compact view");
            Assert.True(window.Topmost);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.True(window.ActualWidth < 400 && window.ActualHeight < 130);
            Assert.False(window.RecorderSurface.IsVisible);
            Assert.Equal(["Full view", "Start", "Stop", "Finish"], controls.Where(b => b.IsVisible).Select(b => b.Content));
            Assert.False(Button("Start").IsEnabled);
            Assert.Contains(Descendants(window.CompactSurface).OfType<TextBlock>(), text => text.Text == "00:00:38");
            if (!maximized)
            {
                SaveScreenshot(window, "compact");
            }
            elapsed += TimeSpan.FromSeconds(2);
            timer.Pulse();
            Assert.Contains(Descendants(window.CompactSurface).OfType<TextBlock>(), text => text.Text == "00:00:40");
            Click("Stop");
            Assert.Equal(RecorderState.Paused, state);
            Assert.False(Button("Stop").IsEnabled);
            Assert.Contains(Descendants(window.CompactSurface).OfType<TextBlock>(), text => text.Text == "Paused");
            Click("Start");
            Assert.Equal(RecorderState.Recording, state);
            window.Left += 40;
            Click("Full view");
            Assert.False(window.Topmost);
            Assert.Equal(originalState, window.WindowState);
            Assert.Equal(bounds, window.RestoreBounds);
            Assert.True(window.RecorderSurface.IsVisible);
            recorder.Verify(r => r.FinishAsync(It.IsAny<CancellationToken>()), Times.Never);
            recorder.Verify(r => r.StartAsync(It.IsAny<RecordingSettings>(), It.IsAny<CancellationToken>()), Times.Never);

            Click("Compact view");
            window.Close();
            Assert.True(window.ExitPanel.IsVisible && window.RecorderSurface.IsVisible);
            Assert.False(window.Topmost);
            Click("Return to recorder");
            Click("Compact view");
            state = RecorderState.Error;
            timer.Pulse();
            Assert.False(window.Topmost);
            Assert.True(window.RecorderSurface.IsVisible);

            state = maximized ? RecorderState.Paused : RecorderState.Recording;
            timer.Pulse();
            Click("Compact view");
            Click("Finish");
            Assert.Equal(RecorderState.Saving, model.Display.State);
            Assert.False(window.Topmost);
            Assert.True(window.RecorderSurface.IsVisible);
            Assert.Equal(originalState, window.WindowState);
            Assert.Equal(bounds, window.RestoreBounds);
            state = RecorderState.Saved;
            publication.SetResult();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(RecorderState.Saved, model.Display.State);
            Assert.True(window.SavedParts.IsVisible);
            Assert.Equal(2, window.SavedParts.Items.Count);
            recorder.Verify(r => r.FinishAsync(It.Is<CancellationToken>(t => t.CanBeCanceled)), Times.Once);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            state = RecorderState.Ready;
            publication.TrySetResult();
            timer.Pulse();
            window.Close();
        }
    }
}
