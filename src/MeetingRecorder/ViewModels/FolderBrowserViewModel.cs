using System.IO;

using MeetingRecorder.Domain;
using MeetingRecorder.Platform;

namespace MeetingRecorder.ViewModels;

/// <summary>Owns the folder chooser state and ignores superseded directory reads.</summary>
internal sealed class FolderBrowserViewModel : ObservableObject
{
    private readonly IFolderCatalog _catalog;
    private string? _directory;
    private int _navigation;
    private string _pathText = string.Empty;
    private string _notice = string.Empty;
    private IReadOnlyList<string> _entries = [];
    private string? _selectedName;

    public FolderBrowserViewModel(IFolderCatalog catalog)
    {
        _catalog = catalog;
    }

    public string PathText {
        get => _pathText;
        set
        {
            if (SetProperty(ref _pathText, value))
            {
                CancelNavigation();
            }
        }
    }

    public string Notice => _notice;
    public IReadOnlyList<string> Entries => _entries;
    public string? SelectedName {
        get => _selectedName;
        set => SetProperty(ref _selectedName, value);
    }

    internal Task OpenAsync(string folder)
    {
        _directory = null;
        SelectedName = null;
        PathText = folder;
        _ = SetProperty(ref _entries, [], nameof(Entries));
        return NavigateAsync(folder, nearestExisting: true);
    }

    internal void CancelNavigation() => _navigation++;

    internal Task GoAsync() => NavigateAsync(PathText);
    internal Task UpAsync() =>
        NavigateAsync(_directory is null ? null : Path.GetDirectoryName(_directory));

    internal Task EnterAsync() =>
        SelectedPath() is { } path ? NavigateAsync(path) : Task.CompletedTask;

    internal string? Choose()
    {
        var path = PathText == (_directory ?? string.Empty)
            ? SelectedPath() ?? PathText : PathText;
        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }

        _ = SetProperty(ref _notice, "Enter a full folder path or select a folder.",
            nameof(Notice));
        return null;
    }

    private string? SelectedPath() => SelectedName is null ? null
        : Path.Combine(_directory ?? string.Empty, SelectedName);

    private async Task NavigateAsync(string? path, bool nearestExisting = false)
    {
        var request = ++_navigation;
        _ = SetProperty(ref _notice, string.Empty, nameof(Notice));
        try
        {
            var listing = await _catalog.ListAsync(path, nearestExisting);
            if (request != _navigation)
            {
                return;
            }

            _directory = listing.Directory;
            SelectedName = null;
            _ = SetProperty(ref _pathText, _directory ?? string.Empty, nameof(PathText));
            _ = SetProperty(ref _entries, listing.Names, nameof(Entries));
        }
        catch (RecordingException ex)
        {
            if (request == _navigation)
            {
                _ = SetProperty(ref _notice, ex.Message, nameof(Notice));
            }
        }
    }
}
