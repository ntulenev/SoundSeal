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

public sealed class RecorderViewModelTests
{
    [Trait("Category", "Unit")]
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
        var mic = new Endpoint("mic", "Microphone", true);
        var output = new Endpoint("out", "Headphones", false);
        var defaultOut = new Endpoint("other", "Speakers", true);
        var catalog = new Mock<IDeviceCatalog>(MockBehavior.Strict);
        catalog.Setup(c => c.Read()).Returns(new Devices([mic], [output, defaultOut]));
        var timer = new TestTimer();
        using var model = new RecorderViewModel(recorder.Object, catalog.Object, timer,
            new FolderLauncher(), new RecordingLinkClipboard(Clipboard.SetText));
        await model.InitializeAsync();
        model.Output.Should().Be(defaultOut);
        model.Output = output;
        model.Folder = @"C:\Recordings";
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
            Result: new(@"C:\Recordings\Voice 🎙.wav", 1040000, null, true))).Returns(Task.CompletedTask);
        await model.FinishAsync();

        model.Display.File.Should().Be(@"C:\Recordings\Voice 🎙.wav");
        model.Display.Elapsed.Should().Be("00:01:05");
        model.CanStart.Should().BeTrue();
    }

    [Trait("Category", "Unit")]
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

    [Trait("Category", "Unit")]
    [Theory(DisplayName = "Errors distinguish saved, unfinished and missing audio")]
    [InlineData(true, "meeting.wav", "Available audio saved.")]
    [InlineData(false, "meeting.wav.partial", "Unfinished file retained.")]
    [InlineData(false, null, "No recording file was saved.")]
    public void ErrorShouldShowFileOutcome(bool published, string? path, string outcome)
    {
        var result = new RecordingResult(path, 0, "The recording folder is unavailable.", published);
        var display = RecorderDisplay.From(new(RecorderState.Error, Result: result),
            TimeSpan.Zero, default, string.Empty);
        display.Detail.Should().Contain(outcome);
        display.File.Should().Be(path);
        display.CanCopyLink.Should().Be(published && path is not null);
    }

    [Trait("Category", "Integration")]
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
        var saved = new RecordingResult(@"C:\Team meetings\June #1.wav", 1600, null, true);
        recorder.SetupGet(r => r.Snapshot).Returns(() => new RecorderSnapshot(state,
            Result: state == RecorderState.Saved ? saved : null));
        recorder.SetupGet(r => r.Elapsed).Returns(TimeSpan.Zero);
        recorder.SetupGet(r => r.Levels).Returns(default(Levels));
        var catalog = new Mock<IDeviceCatalog>(MockBehavior.Strict);
        var microphone = new Endpoint("mic", "Microphone (USB Conference System)", true);
        var output = new Endpoint("out", "Headphones – HP USB-C Dock Audio Headset", true);
        var alternate = new Endpoint("alt", "Alternate output", false);
        catalog.Setup(c => c.Read()).Returns(new Devices([microphone], [output, alternate]));
        var timer = new TestTimer();
        string? copied = null;
        using var services = new ServiceCollection().AddMeetingRecorderPresentation()
            .AddSingleton(new RecordingLinkClipboard(text => copied = text))
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
            window.Icon.Should().NotBeNull();
            var logo = Descendants(window).OfType<Image>().Single();
            var idleLogo = logo.Source;
            idleLogo.Should().NotBeNull();
            logo.Width.Should().Be(105.6);
            logo.Height.Should().Be(105.6);
            var minimize = FindButton("─");
            var close = FindButton("✕");
            var minimizeRight = minimize.TranslatePoint(new Point(minimize.ActualWidth, 0), window).X;
            close.TranslatePoint(new Point(0, 0), window).X.Should().BeGreaterThan(minimizeRight);
            var copy = FindButton("Copy link");
            copy.IsVisible.Should().BeFalse();
            state = RecorderState.Saved;
            timer.Pulse();
            window.UpdateLayout();
            logo.Source.Should().BeSameAs(idleLogo);
            copy.IsVisible.Should().BeTrue();
            var resultArea = Descendants(window).OfType<ScrollViewer>().Single();
            var copyBottom = copy.TranslatePoint(new Point(0, copy.ActualHeight), resultArea).Y;
            copyBottom.Should().BeLessThanOrEqualTo(resultArea.ViewportHeight);
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            copied.Should().Be("file:///C:/Team%20meetings/June%20%231.wav");
            model.CopyLinkStatus.Should().Be("Link copied.");
            var selector = Descendants(window).OfType<ComboBox>().Single(c => c.Items.Count == 2);
            selector.IsDropDownOpen = true;
            selector.UpdateLayout();
            selector.ItemContainerGenerator.ContainerFromIndex(1).Should().BeOfType<ComboBoxItem>();
            selector.SelectedIndex = 1;
            selector.IsDropDownOpen = false;
            model.Output.Should().Be(alternate);
            FindButton("Browse…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindButton("Select folder").IsVisible.Should().BeTrue();
            var path = (TextBox)window.FindName("BrowserPath");
            path.Text = @"C:\Recordings\Team’s meetings";
            FindButton("Select folder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            model.Folder.Should().Be(path.Text);
            recorder.Setup(r => r.StartAsync(new RecordingSettings("mic", "alt", model.Folder)))
                .Callback(() => state = RecorderState.Recording).Returns(Task.CompletedTask);
            recorder.Setup(r => r.PauseAsync()).Callback(() => state = RecorderState.Paused)
                .Returns(Task.CompletedTask);
            recorder.Setup(r => r.ResumeAsync()).Callback(() => state = RecorderState.Recording)
                .Returns(Task.CompletedTask);
            FindButton("Start recording").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Title.Should().Contain("Recording");
            var recordingLogo = logo.Source;
            recordingLogo.Should().NotBeNull().And.NotBeSameAs(idleLogo);
            model.CopyLinkStatus.Should().BeEmpty();
            copy.IsVisible.Should().BeFalse();
            FindButton("Pause").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            logo.Source.Should().BeSameAs(idleLogo);
            FindButton("Resume").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            logo.Source.Should().BeSameAs(recordingLogo);
            model.Display.CanPause.Should().BeTrue();
            window.Close();
            FindButton("Finish and exit").IsVisible.Should().BeTrue();
            FindButton("Return to recorder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            recorder.Setup(r => r.FinishAsync()).Callback(() => state = RecorderState.Error)
                .Returns(Task.CompletedTask);
            window.Close();
            FindButton("Finish and exit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsVisible.Should().BeTrue();
            model.Display.Status.Should().Be("Error");
            logo.Source.Should().BeSameAs(idleLogo);
            recorder.Setup(r => r.FinishAsync()).Callback(() => state = RecorderState.Saved)
                .Returns(Task.CompletedTask);
            FindButton("Start recording").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindButton("Pause").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Close();
            FindButton("Finish and exit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsVisible.Should().BeFalse();
        }
        finally
        {
            state = RecorderState.Ready;
            timer.Pulse();
            if (window.IsVisible)
            {
                window.Close();
            }
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

    [Trait("Category", "Unit")]
    [Theory(DisplayName = "A missing source is identified and prevents recording")]
    [InlineData(false, false, "Microphone and computer audio")]
    [InlineData(false, true, "Microphone unavailable")]
    [InlineData(true, false, "Computer audio unavailable")]
    public async Task MissingDeviceShouldDisableStart(bool hasMic, bool hasOutput, string notice)
    {
        var recorder = ReadyRecorder();
        var device = new Endpoint("device", "USB", false);
        var catalog = new Mock<IDeviceCatalog>(MockBehavior.Strict);
        catalog.Setup(c => c.Read()).Returns(new Devices(hasMic ? [device] : [],
            hasOutput ? [device] : []));
        using var model = new RecorderViewModel(recorder.Object, catalog.Object,
            new TestTimer(), new FolderLauncher(), new RecordingLinkClipboard(Clipboard.SetText));
        await model.InitializeAsync();
        model.CanStart.Should().BeFalse();
        model.Display.Detail.Should().Contain(notice);
        await model.StartAsync();
        recorder.Verify(r => r.StartAsync(It.IsAny<RecordingSettings>()), Times.Never);
    }

    [Trait("Category", "Unit")]
    [Fact(DisplayName = "A failed enumeration clears old endpoints and a refresh recovers")]
    public async Task EnumerationFailureShouldAllowRefresh()
    {
        var recorder = ReadyRecorder();
        var endpoint = new Endpoint("first", "First available", false);
        var catalog = new Mock<IDeviceCatalog>(MockBehavior.Strict);
        catalog.SetupSequence(c => c.Read())
            .Returns(new Devices([endpoint], [endpoint]))
            .Throws(new RecordingException("Windows audio is unavailable."))
            .Returns(new Devices([endpoint], [endpoint]));
        using var model = new RecorderViewModel(recorder.Object, catalog.Object,
            new TestTimer(), new FolderLauncher(), new RecordingLinkClipboard(Clipboard.SetText));
        await model.InitializeAsync();
        model.CanStart.Should().BeTrue();
        await model.RefreshDevicesAsync();
        model.Microphones.Should().BeEmpty();
        model.Output.Should().BeNull();
        model.Display.Detail.Should().Be("Windows audio is unavailable.");
        await model.RefreshDevicesAsync();
        model.Display.Detail.Should().BeEmpty();
        model.CanStart.Should().BeTrue();
        model.Folder = " ";
        model.CanStart.Should().BeFalse();
    }

    [Trait("Category", "Unit")]
    [Fact(DisplayName = "Open folder uses the saved file and explains a missing directory")]
    public void OpenFolderShouldReportShellFailure()
    {
        var recorder = ReadyRecorder();
        var timer = new TestTimer();
        var launcher = new TestLauncher();
        using var model = new RecorderViewModel(recorder.Object,
            new Mock<IDeviceCatalog>(MockBehavior.Strict).Object, timer, launcher,
            new RecordingLinkClipboard(Clipboard.SetText));
        model.OpenFolder();
        launcher.Path.Should().BeNull();

        const string FILE = @"C:\Recordings\meeting.wav";
        recorder.SetupGet(r => r.Snapshot).Returns(new RecorderSnapshot(RecorderState.Saved,
            Result: new(FILE, 10, null, true)));
        timer.Pulse();
        model.OpenFolder();
        launcher.Path.Should().Be(FILE);
        launcher.Unavailable = true;
        model.OpenFolder();
        model.Display.Detail.Should().Contain("no longer available");
        model.Display.File.Should().Be(FILE);
    }

    private static Mock<IRecorder> ReadyRecorder()
    {
        var recorder = new Mock<IRecorder>(MockBehavior.Strict);
        recorder.SetupGet(r => r.Snapshot).Returns(new RecorderSnapshot(RecorderState.Ready));
        recorder.SetupGet(r => r.Elapsed).Returns(TimeSpan.Zero);
        recorder.SetupGet(r => r.Levels).Returns(default(Levels));
        return recorder;
    }

    private sealed class TestLauncher : IFolderLauncher
    {
        internal string? Path { get; private set; }
        internal bool Unavailable { get; set; }

        public void Open(string file)
        {
            Path = file;
            if (Unavailable)
            {
                throw new RecordingException("The recording folder is no longer available.");
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
