using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

using MeetingRecorder.Domain;
using MeetingRecorder.ViewModels;

namespace MeetingRecorder.Views;

/// <summary>Hosts the recorder and its English folder and close interactions.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001",
    Justification = "WPF owns the window and releases the token source in OnClosed.")]
public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RecorderViewModel _model;
    private readonly FolderBrowserViewModel _browser;
    private bool _finishingExit;

    /// <summary>Binds the recorder and folder chooser to their views.</summary>
    internal MainWindow(RecorderViewModel model, FolderBrowserViewModel browser)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(browser);
        _model = model;
        _browser = browser;
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        VersionLabel.Text = $"Version {version.Split('+')[0]}";
        DataContext = model;
        FolderPanel.DataContext = browser;
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        RunAsync(_model.InitializeAsync, _lifetime.Token);

    private void OnRefresh(object sender, RoutedEventArgs e) =>
        RunAsync(_model.RefreshDevicesAsync, _lifetime.Token);

    private void OnStart(object sender, RoutedEventArgs e) =>
        RunAsync(_model.StartAsync, _lifetime.Token);

    private void OnPause(object sender, RoutedEventArgs e) =>
        RunAsync(_model.TogglePauseAsync, _lifetime.Token);

    private void OnFinish(object sender, RoutedEventArgs e) =>
        RunAsync(_model.FinishAsync, _lifetime.Token);

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
