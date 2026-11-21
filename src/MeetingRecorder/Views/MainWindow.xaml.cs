using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

using MeetingRecorder.Domain;
using MeetingRecorder.ViewModels;

namespace MeetingRecorder.Views;

/// <summary>Hosts the recorder and its English folder and close interactions.</summary>
public partial class MainWindow : Window
{
    private readonly RecorderViewModel _model;
    private string? _location;
    private int _navigation;
    private bool _finishingExit;

    internal MainWindow(RecorderViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e) => await _model.InitializeAsync();
    private async void OnRefreshAsync(object sender, RoutedEventArgs e) => await _model.RefreshDevicesAsync();
    private async void OnStartAsync(object sender, RoutedEventArgs e) => await _model.StartAsync();
    private async void OnPauseAsync(object sender, RoutedEventArgs e) => await _model.TogglePauseAsync();
    private async void OnFinishAsync(object sender, RoutedEventArgs e) => await _model.FinishAsync();
    private void OnOpen(object sender, RoutedEventArgs e) => _model.OpenFolder();
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

    private async void OnExitAsync(object sender, RoutedEventArgs e)
    {
        if (_finishingExit)
        {
            return;
        }

        _finishingExit = true;
        HidePanel();
        await _model.FinishAsync();
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
        _navigation++;
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

    private async void OnBrowseAsync(object sender, RoutedEventArgs e)
    {
        ShowPanel(folder: true);
        _ = BrowserPath.Focus();
        var current = _model.Folder;
        while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
        {
            current = Path.GetDirectoryName(current);
        }

        await NavigateAsync(current);
    }

    private async Task NavigateAsync(string? directory)
    {
        var request = ++_navigation;
        FolderNotice.Text = string.Empty;
        try
        {
            var path = string.IsNullOrWhiteSpace(directory) ? null : Path.GetFullPath(directory);
            var entries = await Task.Run(() => path is null ? Directory.GetLogicalDrives()
                : Directory.GetDirectories(path).Select(Path.GetFileName).Order(
                    StringComparer.OrdinalIgnoreCase).ToArray());
            if (request == _navigation)
            {
                _location = path;
                BrowserPath.Text = path ?? string.Empty;
                Folders.ItemsSource = entries;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (request == _navigation)
            {
                FolderNotice.Text = "This folder could not be opened. Check the path and your access.";
            }
        }
    }

    private async void OnGoAsync(object sender, RoutedEventArgs e) => await NavigateAsync(BrowserPath.Text);
    private async void OnUpAsync(object sender, RoutedEventArgs e) =>
        await NavigateAsync(_location is null ? null : Path.GetDirectoryName(_location));

    private string? SelectedPath() => Folders.SelectedItem is string name
        ? Path.Combine(_location ?? string.Empty, name) : null;

    private async void OnEnterFolderAsync(object sender, MouseButtonEventArgs e)
    {
        if (SelectedPath() is { } path)
        {
            await NavigateAsync(path);
        }
    }

    private async void OnFolderKeyAsync(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedPath() is { } path)
        {
            e.Handled = true;
            await NavigateAsync(path);
        }
    }

    private async void OnPathKeyAsync(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await NavigateAsync(BrowserPath.Text);
        }
    }

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        var path = BrowserPath.Text == (_location ?? string.Empty)
            ? SelectedPath() ?? BrowserPath.Text : BrowserPath.Text;
        if (!Path.IsPathFullyQualified(path))
        {
            FolderNotice.Text = "Enter a full folder path or select a folder.";
            return;
        }

        _model.Folder = path;
        HidePanel();
    }
}
