using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using MeetingRecorder.Domain;
using MeetingRecorder.ViewModels;

namespace MeetingRecorder.Views;

/// <summary>Hosts the recorder and its English folder and close interactions.</summary>
public partial class MainWindow : Window
{
    private readonly RecorderViewModel _model;
    private readonly FolderBrowserViewModel _browser;
    private bool _finishingExit;

    internal MainWindow(RecorderViewModel model, FolderBrowserViewModel browser)
    {
        _model = model;
        _browser = browser;
        InitializeComponent();
        DataContext = model;
        FolderPanel.DataContext = browser;
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

    private async void OnBrowseAsync(object sender, RoutedEventArgs e)
    {
        ShowPanel(folder: true);
        _ = BrowserPath.Focus();
        await _browser.OpenAsync(_model.Folder);
    }

    private async void OnGoAsync(object sender, RoutedEventArgs e) => await _browser.GoAsync();
    private async void OnUpAsync(object sender, RoutedEventArgs e) => await _browser.UpAsync();

    private async void OnEnterFolderAsync(object sender, MouseButtonEventArgs e) =>
        await _browser.EnterAsync();

    private async void OnFolderKeyAsync(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _browser.SelectedName is not null)
        {
            e.Handled = true;
            await _browser.EnterAsync();
        }
    }

    private async void OnPathKeyAsync(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await _browser.GoAsync();
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
