using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.ViewModels;

using Microsoft.Extensions.Logging;

namespace MeetingRecorder.Views;

/// <summary>Hosts the recorder and its English folder and close interactions.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001",
    Justification = "WPF owns the window and releases the token source in OnClosed.")]
public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ISoundCheck _soundCheck;
    private readonly ILogger<SoundCheckDialog>? _soundCheckLogger;
    private readonly RecorderViewModel _model;
    private readonly FolderBrowserViewModel _browser;
    private readonly RecordingFolder _logFolder;
    private bool _finishingExit;
    private Point _dragOrigin;
    private Rect? _fullBounds;
    private WindowState _fullWindowState;

    /// <summary>Binds the recorder and folder chooser to their views.</summary>
    internal MainWindow(RecorderViewModel model, FolderBrowserViewModel browser, ISoundCheck soundCheck,
        RecordingFolder logFolder, ILogger<SoundCheckDialog>? soundCheckLogger = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(soundCheck);
        _soundCheck = soundCheck;
        _soundCheckLogger = soundCheckLogger;
        _model = model;
        _browser = browser;
        _logFolder = logFolder;
        InitializeComponent();
        PreviewKeyDown += OnLogFolderShortcut;
        var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        VersionLabel.Text = $"Version {version.Split('+')[0]}";
        DataContext = model;
        FolderPanel.DataContext = browser;
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        _model.PropertyChanged -= OnCompactRecorderChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        RunAsync(_model.InitializeAsync, _lifetime.Token);

    private void OnRefresh(object sender, RoutedEventArgs e) =>
        RunAsync(_model.RefreshDevicesAsync, _lifetime.Token);

    private void OnCheckSound(object sender, RoutedEventArgs e)
    {
        if (_model.CanCheckSound)
        {
            var dialog = new SoundCheckDialog(_soundCheck, _model.UsesMicrophone ? _model.Microphone : null,
                _model.UsesOutput ? _model.Output : null, _soundCheckLogger)
            {
                Owner = this
            };
            dialog.PreviewKeyDown += OnLogFolderShortcut;
            _ = dialog.ShowDialog();
        }
    }

    private void OnStart(object sender, RoutedEventArgs e) =>
        RunAsync(_model.StartAsync, _lifetime.Token);

    private void OnPause(object sender, RoutedEventArgs e) =>
        RunAsync(_model.TogglePauseAsync, _lifetime.Token);

    private void OnFinish(object sender, RoutedEventArgs e)
    {
        RunAsync(_model.FinishAsync, _lifetime.Token);
        RestoreFullView();
    }

    private void OnCompactView(object sender, RoutedEventArgs e)
    {
        var anchor = CompactButton.PointToScreen(new Point(CompactButton.ActualWidth, 0));
        var position = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice.Transform(anchor);
        _fullWindowState = WindowState;
        _fullBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.NoResize;
        MinWidth = MinHeight = 0;
        Width = CompactSurface.Width;
        Height = CompactSurface.Height;
        Left = position.X - Width;
        Top = position.Y;
        RecorderSurface.Visibility = Visibility.Collapsed;
        CompactSurface.Visibility = Visibility.Visible;
        Topmost = true;
        _model.PropertyChanged += OnCompactRecorderChanged;
        _ = FullViewButton.Focus();
    }

    private void OnFullView(object sender, RoutedEventArgs e) => RestoreFullView();

    private void OnCompactRecorderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecorderViewModel.Display) && _model.Display.State is RecorderState.Error or RecorderState.Saved)
        {
            RestoreFullView();
        }
    }

    private void RestoreFullView()
    {
        if (_fullBounds is not { } bounds)
        {
            return;
        }

        _fullBounds = null;
        _model.PropertyChanged -= OnCompactRecorderChanged;
        Topmost = false;
        CompactSurface.Visibility = Visibility.Collapsed;
        RecorderSurface.Visibility = Visibility.Visible;
        ClearValue(MinWidthProperty);
        ClearValue(MinHeightProperty);
        ClearValue(ResizeModeProperty);
        Width = bounds.Width;
        Height = bounds.Height;
        Left = bounds.Left;
        Top = bounds.Top;
        WindowState = _fullWindowState;
        _ = CompactButton.Focus();
    }

    private static async void RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing the window cancels pending UI work.
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e) => _model.OpenFolder();

    private void OnFileDragDown(object sender, MouseButtonEventArgs e)
    {
        var handle = (FrameworkElement)sender;
        _dragOrigin = e.GetPosition(handle);
        e.Handled = handle.CaptureMouse();
    }

    private void OnFileDragUp(object sender, MouseButtonEventArgs e)
    {
        var handle = (FrameworkElement)sender;
        if (handle.IsMouseCaptured)
        {
            handle.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnFileDragMove(object sender, MouseEventArgs e)
    {
        var handle = (FrameworkElement)sender;
        if (!handle.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var offset = e.GetPosition(handle) - _dragOrigin;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        handle.ReleaseMouseCapture();
        if (_model.CreateDragData(handle.DataContext as RecordingFile) is { } data)
        {
            _ = DragDrop.DoDragDrop(handle, data, DragDropEffects.Copy);
        }
        e.Handled = true;
    }

    private void OnCopyLink(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecordingFile part })
        {
            _model.CopyRecordingLink(part);
        }
        else
        {
            _model.CopyRecordingLink();
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_model.Display.CanEdit)
        {
            return;
        }

        e.Cancel = true;
        if (!_finishingExit && _model.Display.CanFinish)
        {
            RestoreFullView();
            ShowPanel(folder: false);
            _ = ReturnButton.Focus();
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => RunAsync(FinishAndExitAsync, _lifetime.Token);

    private async Task FinishAndExitAsync(CancellationToken cancellationToken)
    {
        if (_finishingExit)
        {
            return;
        }

        _finishingExit = true;
        HidePanel();
        await _model.FinishAsync(cancellationToken);
        _finishingExit = false;
        if (_model.Display.State == RecorderState.Saved)
        {
            Close();
        }
    }

    private void ShowPanel(bool folder)
    {
        RecorderSurface.IsEnabled = false;
        Overlay.Visibility = Visibility.Visible;
        FolderPanel.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;
        ExitPanel.Visibility = folder ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HidePanel()
    {
        _browser.CancelNavigation();
        Overlay.Visibility = Visibility.Collapsed;
        RecorderSurface.IsEnabled = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => HidePanel();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Overlay.IsVisible)
        {
            HidePanel();
            e.Handled = true;
        }
    }

    private void OnLogFolderShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.L && e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (!e.IsRepeat)
            {
                _model.OpenLogFolder(_logFolder);
            }
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e) => RunAsync(BrowseAsync, _lifetime.Token);

    private async Task BrowseAsync(CancellationToken cancellationToken)
    {
        ShowPanel(folder: true);
        _ = BrowserPath.Focus();
        await _browser.OpenAsync(_model.Folder, cancellationToken);
    }

    private void OnGo(object sender, RoutedEventArgs e) =>
        RunAsync(_browser.GoAsync, _lifetime.Token);

    private void OnUp(object sender, RoutedEventArgs e) =>
        RunAsync(_browser.UpAsync, _lifetime.Token);

    private void OnEnterFolder(object sender, MouseButtonEventArgs e) =>
        RunAsync(_browser.EnterAsync, _lifetime.Token);

    private void OnFolderKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _browser.SelectedName is not null)
        {
            e.Handled = true;
            RunAsync(_browser.EnterAsync, _lifetime.Token);
        }
    }

    private void OnPathKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            RunAsync(_browser.GoAsync, _lifetime.Token);
        }
    }

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        if (_browser.Choose() is { } path)
        {
            _model.Folder = path;
            HidePanel();
        }
    }
}
