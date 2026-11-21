using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using FluentAssertions;

using MeetingRecorder.Application.Abstractions;
using MeetingRecorder.DependencyInjection;
using MeetingRecorder.Domain;
using MeetingRecorder.Platform;
using MeetingRecorder.Threading;
using MeetingRecorder.ViewModels;
using MeetingRecorder.Views;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace MeetingRecorder.Tests;

[Trait("Category", "Unit")]
public sealed class RecorderViewModelTests
{
    [Fact(DisplayName = "Endpoint selection, actions and timer follow one recording state")]
    public async Task ViewModelShouldBindSelectedDevicesAndFeedback()
    {
        var recorder = new Mock<IRecorder>(MockBehavior.Strict);
        var snapshot = new RecorderSnapshot(RecorderState.Ready);
        var elapsed = TimeSpan.Zero;
        var levels = new Levels();
        recorder.SetupGet(r => r.Snapshot).Returns(() => snapshot);
        recorder.SetupGet(r => r.Elapsed).Returns(() => elapsed);
        recorder.SetupGet(r => r.Levels).Returns(() => levels);
        var mic = new Endpoint("mic", "Микрофон", true);
        var output = new Endpoint("out", "Kopfhörer", false);
        var defaultOut = new Endpoint("other", "Lautsprecher", true);
        var catalog = new Mock<IDeviceCatalog>(MockBehavior.Strict);
        catalog.Setup(c => c.Read()).Returns(new Devices([mic], [output, defaultOut]));
        var timer = new TestTimer();
        using var model = new RecorderViewModel(recorder.Object, catalog.Object, timer,
            new FolderLauncher());
        await model.InitializeAsync();
        model.Output.Should().Be(defaultOut);
        model.Output = output;
        model.Folder = @"C:\Записи";
        await model.RefreshDevicesAsync();
        model.Output.Should().Be(output);
        model.CanStart.Should().BeTrue();
        recorder.Setup(r => r.StartAsync(new RecordingSettings("mic", "out", model.Folder)))
            .Callback(() => snapshot = new(RecorderState.Recording)).Returns(Task.CompletedTask);

        await model.StartAsync();
        model.Display.Title.Should().Contain("Recording");
        model.Display.StatusColor.Should().Be("#FF7975");
        model.CanEdit.Should().BeFalse();
        model.Display.CanFinish.Should().BeTrue();
        levels = new(0.25f, 0);
        elapsed = TimeSpan.FromSeconds(65);
        timer.Pulse();
        model.Display.Elapsed.Should().Be("00:01:05");
        model.Display.MicrophoneSignal.Should().Be("Signal detected");
        model.Display.ComputerLevel.Should().Be(0);
        levels = new(0, 0.5f);
        timer.Pulse();
        model.Display.ComputerSignal.Should().Be("Signal detected");
        model.Display.MicrophoneLevel.Should().Be(0);
        recorder.Setup(r => r.PauseAsync()).Callback(() => snapshot = new(RecorderState.Paused))
            .Returns(Task.CompletedTask);
        await model.TogglePauseAsync();
        model.Display.PauseLabel.Should().Be("Resume");
        model.Display.ComputerSignal.Should().Be("Paused");
        model.Display.ComputerLevel.Should().Be(0);
        recorder.Setup(r => r.ResumeAsync()).Callback(() => snapshot = new(RecorderState.Recording))
            .Returns(Task.CompletedTask);
        await model.TogglePauseAsync();
        model.Display.CanPause.Should().BeTrue();
        recorder.Setup(r => r.FinishAsync()).Callback(() => snapshot = new(RecorderState.Saved,
            Result: new(@"C:\Записи\声.wav", 1040000, null, true))).Returns(Task.CompletedTask);
        await model.FinishAsync();

        model.Display.File.Should().Be(@"C:\Записи\声.wav");
        model.Display.Elapsed.Should().Be("00:01:05");
        model.CanStart.Should().BeTrue();
    }

    [Theory(DisplayName = "User-facing status is English under a German display culture")]
    [InlineData(RecorderState.Ready, "Ready")]
    [InlineData(RecorderState.Starting, "Connecting to audio devices…")]
    [InlineData(RecorderState.Recording, "Recording")]
    [InlineData(RecorderState.Pausing, "Pausing…")]
    [InlineData(RecorderState.Paused, "Paused")]
    [InlineData(RecorderState.Resuming, "Connecting to audio devices…")]
    [InlineData(RecorderState.Saving, "Saving recording…")]
    [InlineData(RecorderState.Saved, "Saved")]
    [InlineData(RecorderState.Error, "Error")]
    public void StateShouldHaveEnglishStatus(RecorderState state, string status)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var display = RecorderDisplay.From(new(state), TimeSpan.Zero, default, string.Empty);
            display.Status.Should().Be(status);
            display.CanEdit.Should().Be(state is RecorderState.Ready or RecorderState.Saved or RecorderState.Error);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory(DisplayName = "Errors distinguish saved, unfinished and missing audio")]
    [InlineData(true, "unterwegs.wav", "Available audio saved.")]
    [InlineData(false, "unterwegs.partial", "Unfinished file retained.")]
    [InlineData(false, null, "No recording file was saved.")]
    public void ErrorShouldShowFileOutcome(bool published, string? path, string outcome)
    {
        var result = new RecordingResult(path, 0, "The recording folder is unavailable.", published);
        var display = RecorderDisplay.From(new(RecorderState.Error, Result: result),
            TimeSpan.Zero, default, string.Empty);
        display.Detail.Should().Contain(outcome);
        display.File.Should().Be(path);
    }

    [Fact(DisplayName = "The WPF window, folder chooser and close prompt have English controls at minimum size")]
    public async Task WindowShouldPresentEnglishInteractions()
    {
        var ready = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var check = dispatcher.InvokeAsync(CheckWindowAsync).Task.Unwrap();
            ready.SetResult(check);
            _ = check.ContinueWith(_ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Send),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await ready.Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static async Task CheckWindowAsync()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        var recorder = new Mock<IRecorder>(MockBehavior.Strict);
        var state = RecorderState.Ready;
        recorder.SetupGet(r => r.Snapshot).Returns(() => new RecorderSnapshot(state));
        recorder.SetupGet(r => r.Elapsed).Returns(TimeSpan.Zero);
        recorder.SetupGet(r => r.Levels).Returns(default(Levels));
        var catalog = new Mock<IDeviceCatalog>(MockBehavior.Strict);
        var microphone = new Endpoint("mic", "Mikrofon (USB-Konferenzsystem)", true);
        var output = new Endpoint("out", "Наушники (HP USB-C Dock Audio Headset)", true);
        catalog.Setup(c => c.Read()).Returns(new Devices([microphone], [output]));
        var timer = new TestTimer();
        using var services = new ServiceCollection().AddMeetingRecorderPresentation()
            .AddSingleton(recorder.Object).AddSingleton(catalog.Object)
            .AddSingleton<IUiTimer>(timer).BuildServiceProvider();
        var window = services.GetRequiredService<MainWindow>();
        var model = services.GetRequiredService<RecorderViewModel>();
        window.DataContext.Should().BeSameAs(model);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.ShowActivated = false;
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.PropertyChanged += (_, _) =>
        {
            if (model.CanStart)
            {
                initialized.TrySetResult();
            }
        };
        window.Show();
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var buttons = Descendants(window).OfType<Button>().ToList();
        Button FindButton(string text)
        {
            return buttons.Single(b => Equals(b.Content, text));
        }

        try
        {
            foreach (var label in new[] { "Start recording", "Finish recording", "Pause", "Refresh devices" })
            {
                FindButton(label).IsVisible.Should().BeTrue();
            }

            foreach (var control in Descendants(window).Where(c => c is ProgressBar or ComboBox))
            {
                var bounds = control.TransformToAncestor(window).TransformBounds(
                    new Rect(control.RenderSize));
                bounds.Bottom.Should().BeLessThan(window.ActualHeight);
                bounds.Top.Should().BeGreaterThan(0);
            }

            model.Microphone.Should().Be(microphone);
            model.Output.Should().Be(output);
            FindButton("Browse…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindButton("Select folder").IsVisible.Should().BeTrue();
            var path = (TextBox)window.FindName("BrowserPath");
            path.Text = @"C:\Записи\Gespräche";
            FindButton("Select folder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            model.Folder.Should().Be(path.Text);
            state = RecorderState.Recording;
            timer.Pulse();
            window.Close();
            FindButton("Finish and exit").IsVisible.Should().BeTrue();
            FindButton("Return to recorder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            recorder.Setup(r => r.FinishAsync()).Callback(() => state = RecorderState.Error)
                .Returns(Task.CompletedTask);
            window.Close();
            FindButton("Finish and exit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsVisible.Should().BeTrue();
            model.Display.Status.Should().Be("Error");
        }
        finally
        {
            state = RecorderState.Ready;
            timer.Pulse();
            window.Close();
        }
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    {
        foreach (var element in LogicalTreeHelper.GetChildren(parent).OfType<FrameworkElement>())
        {
            yield return element;
            foreach (var child in Descendants(element))
            {
                yield return child;
            }
        }
    }

    private sealed class TestTimer : IUiTimer
    {
        public event EventHandler? Tick;
        public void Start() { }
        public void Stop() { }
        internal void Pulse() => Tick?.Invoke(this, EventArgs.Empty);
    }
}
