using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

using FluentAssertions;

using MeetingRecorder.Abstractions;
using MeetingRecorder.DependencyInjection;
using MeetingRecorder.Domain;
using MeetingRecorder.Platform;
using MeetingRecorder.Storage;
using MeetingRecorder.TestSupport;
using MeetingRecorder.ViewModels;
using MeetingRecorder.Views;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace MeetingRecorder.Tests;

/// <summary>Tests the recorder window and presentation state.</summary>
public sealed partial class RecorderViewModelTests
{
    /// <summary>Endpoint selection, actions and timer follow one recording state.</summary>
    [Trait("Category", "Unit")]
    [Fact(DisplayName = "Endpoint selection, actions and timer follow one recording state")]
    public Task ViewModelShouldBindSelectedDevicesAndFeedback() =>
        TestDeadline.Run(async cancellationToken =>
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
            catalog.Setup(c => c.ReadAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).ReturnsAsync(new Devices([mic], [output, defaultOut]));
            var timer = new TestTimer();
            using var model = new RecorderViewModel(recorder.Object, catalog.Object, timer,
                new FolderLauncher(), new RecordingLinkClipboard(Clipboard.SetText));
            await model.InitializeAsync(cancellationToken);
            model.Output.Should().Be(defaultOut);
            model.Output = output;
            model.Folder = @"C:\Recordings";
            await model.RefreshDevicesAsync(cancellationToken);
            model.Output.Should().Be(output);
            model.CanStart.Should().BeTrue();
            model.SplitRecording = true;
            recorder.Setup(r => r.StartAsync(new RecordingSettings("mic", "out", model.Folder, RecordingMode.Meeting, RecordingLayout.QuarterHourParts), It.Is<CancellationToken>(t => t.CanBeCanceled)))
                .Callback(() => snapshot = new(RecorderState.Recording, started: 0)).Returns(Task.CompletedTask);

            await model.StartAsync(cancellationToken);
            model.Mode = RecordingMode.Audio;
            model.SplitRecording = false;
            Assert.Equal(RecordingMode.Meeting, model.Mode);
            Assert.True(model.SplitRecording);
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
            recorder.Setup(r => r.PauseAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).Callback(() => snapshot = new(RecorderState.Paused))
                .Returns(Task.CompletedTask);
            await model.TogglePauseAsync(cancellationToken);
            model.Display.PauseLabel.Should().Be("Resume");
            model.Display.ComputerSignal.Should().Be("Paused");
            model.Display.ComputerLevel.Should().Be(0);
            recorder.Setup(r => r.ResumeAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).Callback(() => snapshot = new(RecorderState.Recording, started: 0))
                .Returns(Task.CompletedTask);
            await model.TogglePauseAsync(cancellationToken);
            model.Display.CanPause.Should().BeTrue();
            recorder.Setup(r => r.FinishAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).Callback(() => snapshot = new(RecorderState.Saved,
                result: new(@"C:\Recordings\Voice 🎙.wav", 1040000, null, true))).Returns(Task.CompletedTask);
            await model.FinishAsync(cancellationToken);

            (model.Display.File?.Value).Should().Be(@"C:\Recordings\Voice 🎙.wav");
            model.Display.Elapsed.Should().Be("00:01:05");
            model.CanStart.Should().BeTrue();
        });

    /// <summary>User-facing status is English under a German display culture.</summary>
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
            var display = RecorderDisplay.From(TestSnapshot(state), TimeSpan.Zero, default, string.Empty);
            display.Status.Should().Be(status);
            display.CanEdit.Should().Be(state is RecorderState.Ready or RecorderState.Saved or RecorderState.Error);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>Errors distinguish saved, unfinished and missing audio.</summary>
    [Trait("Category", "Unit")]
    [Theory(DisplayName = "Errors distinguish saved, unfinished and missing audio")]
    [InlineData(true, @"C:\Recordings\meeting.wav", "Available audio saved.")]
    [InlineData(false, @"C:\Recordings\meeting.wav.partial", "Unfinished file retained.")]
    [InlineData(false, null, "No recording file was saved.")]
    public void ErrorShouldShowFileOutcome(bool published, string? path, string outcome)
    {
        var result = new RecordingResult(path, 0, "The recording folder is unavailable.", published);
        var display = RecorderDisplay.From(new(RecorderState.Error, result: result),
            TimeSpan.Zero, default, string.Empty);
        display.Detail.Should().Contain(outcome);
        (display.File?.Value).Should().Be(path);
        display.CanCopyLink.Should().Be(published && path is not null);
    }

    /// <summary>The WPF window, folder chooser and close prompt have English controls at minimum size.</summary>
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "The WPF window, folder chooser and close prompt have English controls at minimum size")]
    public Task WindowShouldPresentEnglishInteractions() =>
        TestDeadline.Run(token => OnDispatcherAsync(CheckWindowAsync, token));

    /// <summary>A missing source is identified and prevents recording.</summary>
    [Trait("Category", "Unit")]
    [Theory(DisplayName = "A missing source is identified and prevents recording")]
    [InlineData(false, false, "Microphone and computer audio")]
    [InlineData(false, true, "Microphone unavailable")]
    [InlineData(true, false, "Computer audio unavailable")]
    public Task MissingDeviceShouldDisableStart(bool hasMic, bool hasOutput, string notice) =>
        TestDeadline.Run(async cancellationToken =>
        {
            var recorder = ReadyRecorder();
            var device = new Endpoint("device", "USB", false);
            var catalog = new Mock<IDeviceCatalog>(MockBehavior.Strict);
            catalog.Setup(c => c.ReadAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).ReturnsAsync(new Devices(hasMic ? [device] : [],
                hasOutput ? [device] : []));
            using var model = new RecorderViewModel(recorder.Object, catalog.Object,
                new TestTimer(), new FolderLauncher(), new RecordingLinkClipboard(Clipboard.SetText));
            await model.InitializeAsync(cancellationToken);
            model.CanStart.Should().BeFalse();
            model.Display.Detail.Should().Contain(notice);
            await model.StartAsync(cancellationToken);
            recorder.Verify(r => r.StartAsync(It.IsAny<RecordingSettings>(), It.Is<CancellationToken>(t => t.CanBeCanceled)), Times.Never);
        });

    /// <summary>A failed enumeration clears old endpoints and a refresh recovers.</summary>
    [Trait("Category", "Unit")]
    [Fact(DisplayName = "A failed enumeration clears old endpoints and a refresh recovers")]
    public Task EnumerationFailureShouldAllowRefresh() =>
        TestDeadline.Run(async cancellationToken =>
        {
            var recorder = ReadyRecorder();
            var endpoint = new Endpoint("first", "First available", false);
            var catalog = new Mock<IDeviceCatalog>(MockBehavior.Strict);
            catalog.SetupSequence(c => c.ReadAsync(It.Is<CancellationToken>(t => t.CanBeCanceled)))
                .ReturnsAsync(new Devices([endpoint], [endpoint]))
                .Throws(new RecordingException("Windows audio is unavailable."))
                .ReturnsAsync(new Devices([endpoint], [endpoint]));
            using var model = new RecorderViewModel(recorder.Object, catalog.Object,
                new TestTimer(), new FolderLauncher(), new RecordingLinkClipboard(Clipboard.SetText));
            await model.InitializeAsync(cancellationToken);
            model.CanStart.Should().BeTrue();
            await model.RefreshDevicesAsync(cancellationToken);
            model.Microphones.Should().BeEmpty();
            model.Output.Should().BeNull();
            model.Display.Detail.Should().Be("Windows audio is unavailable.");
            await model.RefreshDevicesAsync(cancellationToken);
            model.Display.Detail.Should().BeEmpty();
            model.CanStart.Should().BeTrue();
            model.Folder = " ";
            model.CanStart.Should().BeFalse();
        });

    /// <summary>Open folder uses the saved file and explains a missing directory.</summary>
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
        Assert.Equal(model.Folder, launcher.Path);
        model.Folder = "relative";
        model.OpenFolder();
        Assert.Contains("full recording folder path", model.FileActionStatus, StringComparison.Ordinal);
        model.Folder = @"C:\Next meeting";
        model.OpenFolder();
        Assert.Equal(model.Folder, launcher.Path);
        Assert.Empty(model.FileActionStatus);

        const string FILE = @"C:\Recordings\meeting.wav";
        recorder.SetupGet(r => r.Snapshot).Returns(new RecorderSnapshot(RecorderState.Saved,
            result: new(FILE, 10, null, true)));
        timer.Pulse();
        model.OpenFolder();
        launcher.Path.Should().Be(FILE);
        launcher.Unavailable = true;
        model.OpenFolder();
        model.FileActionStatus.Should().Contain("no longer available");
        (model.Display.File?.Value).Should().Be(FILE);
    }

    private static async Task OnDispatcherAsync(Func<CancellationToken, Task> checkWindow, CancellationToken cancellationToken)
    {
        var finished = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            using var stop = cancellationToken.Register(() => dispatcher.BeginInvokeShutdown(DispatcherPriority.Send));
            var check = dispatcher.InvokeAsync(() => checkWindow(cancellationToken),
                DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
            _ = check.ContinueWith(_ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Send),
                cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);
            Dispatcher.Run();
            finished.SetResult(check);
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
    }

    private static void SaveScreenshot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("SOUNDSEAL_SCREENSHOTS") is not { Length: > 0 } folder)
        {
            return;
        }

        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,
            (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(System.IO.Path.Combine(folder, name + ".png"));
        encoder.Save(output);
    }

    private static async Task CheckWindowAsync(CancellationToken cancellationToken)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        var recorder = new Mock<IRecorder>(MockBehavior.Strict);
        var state = RecorderState.Ready;
        var saved = new RecordingResult(@"C:\Team meetings #1\Meeting-2026-11-21_10-00-00.wav", 65 * 16000, null, true);
        recorder.SetupGet(r => r.Snapshot).Returns(() => TestSnapshot(state, saved));
        recorder.SetupGet(r => r.Elapsed).Returns(() => state == RecorderState.Saved ? RecordingFormat.Duration(saved.Frames).Value :
            state == RecorderState.Recording ? TimeSpan.FromSeconds(38) : TimeSpan.Zero);
        recorder.SetupGet(r => r.Levels).Returns(() => state == RecorderState.Recording ? new Levels(0.14f, 0.25f) : default);
        var catalog = new Mock<IDeviceCatalog>(MockBehavior.Strict);
        var microphone = new Endpoint("mic", "Microphone (USB Conference System)", true);
        var output = new Endpoint("out", "Headphones – HP USB-C Dock Audio Headset", true);
        var alternate = new Endpoint("alt", "Speakers (USB Audio)", false);
        catalog.Setup(c => c.ReadAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).ReturnsAsync(new Devices([microphone], [output, alternate]));
        var timer = new TestTimer();
        string? copied = null;
        var soundCheck = new Mock<ISoundCheck>(MockBehavior.Strict);
        using var services = new ServiceCollection().AddMeetingRecorderPresentation()
            .AddSingleton(soundCheck.Object)
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
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
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
            var title = Descendants(window).OfType<TextBlock>().Single(text => text.Text == "SoundSeal");
            var version = (TextBlock)window.FindName("VersionLabel");
            var build = typeof(MainWindow).Assembly.GetName().Version!;
            version.Text.Should().MatchRegex($@"^Version {build.Major}\.{build.Minor}\.{build.Build}(?:-[0-9A-Za-z.-]+)?$");
            version.IsVisible.Should().BeTrue();
            version.TranslatePoint(new Point(), window).Y.Should().BeGreaterThanOrEqualTo(
                title.TranslatePoint(new Point(0, title.ActualHeight), window).Y);
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
            model.Folder = @"C:\Recordings";
            var modes = (ComboBox)window.FindName("ModeSelector");
            modes.SelectedIndex = 1;
            model.Mode.Should().Be(RecordingMode.Audio);
            model.UsesMicrophone.Should().BeFalse();
            SaveScreenshot(window, "computer-audio");
            model.Microphone = null;
            model.CanStart.Should().BeTrue();
            modes.SelectedIndex = 2;
            model.CanStart.Should().BeFalse();
            model.Display.Detail.Should().Contain("Microphone unavailable");
            model.Microphone = microphone;
            model.Display.Detail.Should().BeEmpty();
            model.CanStart.Should().BeTrue();
            model.UsesOutput.Should().BeFalse();
            SaveScreenshot(window, "dictaphone");
            modes.SelectedIndex = 0;
            SaveScreenshot(window, "ready");
            var copy = FindButton("Copy link");
            copy.IsVisible.Should().BeFalse();
            state = RecorderState.Saved;
            timer.Pulse();
            window.UpdateLayout();
            logo.Source.Should().BeSameAs(idleLogo);
            copy.IsVisible.Should().BeTrue();
            SaveScreenshot(window, "saved");
            var resultArea = (ScrollViewer)window.FindName("SingleResult");
            var copyBottom = copy.TranslatePoint(new Point(0, copy.ActualHeight), resultArea).Y;
            copyBottom.Should().BeLessThanOrEqualTo(resultArea.ViewportHeight);
            var single = saved;
            var parts = Enumerable.Range(1, 20).Select(index => new RecordingResult(
                FormattableString.Invariant($@"C:\Recordings\Meeting-2026-11-21_10-00-00-c81a07b77e-Part-{index:D4}.wav"),
                15L * 60 * RecordingFormat.SampleRate, null, true)).ToArray();
            saved = RecordingResult.CombineParts(parts, null);
            model.SplitRecording = true;
            timer.Pulse();
            window.UpdateLayout();
            var list = (ListBox)window.FindName("SavedParts");
            Assert.True(list.IsVisible);
            Assert.True(window.AllPartsHandle.IsVisible);
            Assert.False(window.SingleFileHandle.IsVisible);
            Assert.Equal(20, list.Items.Count);
            SaveScreenshot(window, "parts");
            list.ScrollIntoView(list.Items[^1]);
            window.UpdateLayout();
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1);
            var lastCopy = FindVisualButton(item);
            Assert.NotNull(lastCopy);
            lastCopy.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.EndsWith("Part-0020.wav", copied, StringComparison.Ordinal);
            model.CopyRecordingLink(new RecordingFile(@"C:\Recordings\other.wav"));
            Assert.EndsWith("Part-0020.wav", copied, StringComparison.Ordinal);
            saved = single;
            model.SplitRecording = false;
            timer.Pulse();
            Assert.True(window.SingleFileHandle.IsVisible && window.SingleFileHandle.IsEnabled);
            Assert.False(window.AllPartsHandle.IsVisible);
            copy.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            copied.Should().Be("file:///C:/Team%20meetings%20%231/Meeting-2026-11-21_10-00-00.wav");
            model.FileActionStatus.Should().Be("Link copied.");
            var selector = Descendants(window).OfType<ComboBox>().Single(c => c.Items.Count == 2);
            selector.IsDropDownOpen = true;
            selector.UpdateLayout();
            selector.ItemContainerGenerator.ContainerFromIndex(1).Should().BeOfType<ComboBoxItem>();
            selector.SelectedIndex = 1;
            selector.IsDropDownOpen = false;
            model.Output.Should().Be(alternate);
            await CheckSoundDialogAsync(window, FindButton("Check sound"), soundCheck, cancellationToken);
            FindButton("Browse…").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            FindButton("Select folder").IsVisible.Should().BeTrue();
            var path = (TextBox)window.FindName("BrowserPath");
            path.Text = @"C:\Recordings\Team’s meetings";
            FindButton("Select folder").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            model.Folder.Should().Be(path.Text);
            recorder.Setup(r => r.StartAsync(new RecordingSettings("mic", "alt", model.Folder), It.Is<CancellationToken>(t => t.CanBeCanceled)))
                .Callback(() => state = RecorderState.Recording).Returns(Task.CompletedTask);
            recorder.Setup(r => r.PauseAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).Callback(() => state = RecorderState.Paused)
                .Returns(Task.CompletedTask);
            recorder.Setup(r => r.ResumeAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).Callback(() => state = RecorderState.Recording)
                .Returns(Task.CompletedTask);
            FindButton("Start recording").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.Title.Should().Contain("Recording");
            SaveScreenshot(window, "recording");
            var recordingLogo = logo.Source;
            recordingLogo.Should().NotBeNull().And.NotBeSameAs(idleLogo);
            model.FileActionStatus.Should().BeEmpty();
            copy.IsVisible.Should().BeFalse();
            FindButton("Pause").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            logo.Source.Should().BeSameAs(idleLogo);
            FindButton("Resume").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            logo.Source.Should().BeSameAs(recordingLogo);
            model.Display.CanPause.Should().BeTrue();
            window.Close();
            FindButton("Finish and exit").IsVisible.Should().BeTrue();
            FindButton("Return to recorder").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            recorder.Setup(r => r.FinishAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).Callback(() => state = RecorderState.Error)
                .Returns(Task.CompletedTask);
            window.Close();
            FindButton("Finish and exit").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.IsVisible.Should().BeTrue();
            model.Display.Status.Should().Be("Error");
            logo.Source.Should().BeSameAs(idleLogo);
            recorder.Setup(r => r.FinishAsync(It.Is<CancellationToken>(t => t.CanBeCanceled))).Callback(() => state = RecorderState.Saved)
                .Returns(Task.CompletedTask);
            FindButton("Start recording").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            FindButton("Pause").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.Close();
            FindButton("Finish and exit").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
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

    private static async Task CheckSoundDialogAsync(MainWindow window, Button open, Mock<ISoundCheck> check,
        CancellationToken cancellationToken)
    {
        var sample = new byte[] { 1, 2 };
        var captured = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        check.Setup(c => c.CaptureAsync(new EndpointId("mic"), new EndpointId("alt"), It.IsAny<IProgress<Levels>>(),
            It.IsAny<CancellationToken>())).Returns((EndpointId? _, EndpointId? _, IProgress<Levels> progress, CancellationToken _) =>
            {
                progress.Report(new Levels(0.1f, 0.2f));
                return captured.Task;
            });
        check.Setup(c => c.PlayAsync(new EndpointId("alt"), sample, It.IsAny<CancellationToken>()))
            .Returns((EndpointId? _, byte[] _, CancellationToken token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        var modal = window.Dispatcher.InvokeAsync(() => open.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var dialog = Assert.IsType<SoundCheckDialog>(Assert.Single(window.OwnedWindows.Cast<Window>()));
        try
        {
            Assert.Contains("Speakers (USB Audio)", dialog.OutputName.Text, StringComparison.Ordinal);
            dialog.CaptureButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("Signal detected", dialog.MicrophoneSignal.Text);
            Assert.False(dialog.ListenButton.IsEnabled);
            captured.SetResult(sample);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(dialog.ListenButton.IsEnabled);
            SaveScreenshot(dialog, "sound-check");
            dialog.ListenButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.False(dialog.CaptureButton.IsEnabled);
        }
        finally
        {
            captured.TrySetResult(sample);
            dialog.Close();
        }
        await modal.Task.WaitAsync(cancellationToken);
        check.VerifyAll();
    }

    private static Button? FindVisualButton(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is Button button)
            {
                return button;
            }

            if (FindVisualButton(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
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

    private static RecorderSnapshot TestSnapshot(RecorderState state, RecordingResult? saved = null) =>
        state switch
        {
            RecorderState.Recording => new(state, started: 0),
            RecorderState.Saved => new(state, result: saved ?? new(@"C:\Recordings\meeting.wav", 0, null, true)),
            RecorderState.Error => new(state, result: new(null, 0, "Device disconnected.", false)),
            RecorderState.Ready or RecorderState.Starting or RecorderState.Pausing
                or RecorderState.Paused or RecorderState.Resuming or RecorderState.Saving => new(state),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

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

        public void OpenDestination(RecordingFolder folder) => Path = folder.Value;

        public void Open(RecordingFile file)
        {
            Path = file.Value;
            if (Unavailable)
            {
                throw new RecordingException("The recording folder is no longer available.");
            }
        }
    }

    private sealed class TestTimer : IUiTimer
    {
        public event EventHandler? Tick;
        public void StartTicks() { }
        public void StopTicks() { }
        internal void Pulse() => Tick?.Invoke(this, EventArgs.Empty);
    }
}
