using System.IO;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingRecorder.ViewModels;

/// <summary>Owns the folder chooser state and ignores superseded directory reads.</summary>
internal sealed partial class FolderBrowserViewModel : ObservableObject
{
    private readonly IFolderCatalog _catalog;
    private readonly ILogger<FolderBrowserViewModel> _logger;
    private string? _directory;
    private int _navigation;
    private string _pathText = string.Empty;
    private string _notice = string.Empty;
    private IReadOnlyList<string> _entries = [];

    /// <summary>Uses the supplied catalog to browse folders and drives.</summary>
    public FolderBrowserViewModel(IFolderCatalog catalog, ILogger<FolderBrowserViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _logger = logger ?? NullLogger<FolderBrowserViewModel>.Instance;
    }

    /// <summary>Editable folder path currently shown in the chooser.</summary>
    public string PathText {
        get => _pathText;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _pathText, value))
            {
                CancelNavigation();
            }
        }
    }

    /// <summary>Validation or navigation feedback for the current request.</summary>
    public string Notice => _notice;

    /// <summary>Child directory names or available drive roots.</summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Entry selected in the current listing.</summary>
    public string? SelectedName {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>Opens the chooser at the closest existing ancestor of the requested path.</summary>
    internal Task OpenAsync(string folder, CancellationToken cancellationToken)
    {
        _directory = null;
        SelectedName = null;
        PathText = folder;
        _ = SetProperty(ref _entries, [], nameof(Entries));
        return NavigateAsync(folder, FolderLookup.NearestExisting, cancellationToken);
    }

    /// <summary>Invalidates pending listings after edits or dismissal.</summary>
    internal void CancelNavigation() => _navigation++;

    /// <summary>Lists the folder entered in the path field.</summary>
    internal Task GoAsync(CancellationToken cancellationToken) => NavigateAsync(PathText, FolderLookup.Exact, cancellationToken);

    /// <summary>Navigates to the parent directory or the drive list.</summary>
    internal Task UpAsync(CancellationToken cancellationToken) =>
        NavigateAsync(_directory is null ? null : Path.GetDirectoryName(_directory), FolderLookup.Exact, cancellationToken);

    /// <summary>Opens the selected child directory.</summary>
    internal Task EnterAsync(CancellationToken cancellationToken) =>
        SelectedPath() is { } path ? NavigateAsync(path, FolderLookup.Exact, cancellationToken) : Task.CompletedTask;

    /// <summary>Returns an absolute folder path or shows validation feedback.</summary>
    internal string? Choose()
    {
        var path = PathText == (_directory ?? string.Empty)
            ? SelectedPath() ?? PathText : PathText;
        if (RecordingFolder.TryParse(path, out _))
        {
            return path;
        }

        _ = SetProperty(ref _notice, "Enter a full folder path or select a folder.",
            nameof(Notice));
        return null;
    }

    private string? SelectedPath() => SelectedName is null ? null
        : Path.Combine(_directory ?? string.Empty, SelectedName);

    private async Task NavigateAsync(string? path, FolderLookup lookup, CancellationToken cancellationToken)
    {
        var request = ++_navigation;
        _ = SetProperty(ref _notice, string.Empty, nameof(Notice));
        try
        {
            var listing = await _catalog.ListAsync(path, lookup, cancellationToken);
            if (request != _navigation)
            {
                return;
            }

            _directory = listing.Directory?.Value;
            SelectedName = null;
            _ = SetProperty(ref _pathText, _directory ?? string.Empty, nameof(PathText));
            _ = SetProperty(ref _entries, [.. listing.Names.Select(name => name.Value)], nameof(Entries));
        }
        catch (RecordingException ex)
        {
            LogBrowseFailure(_logger, ex, path, lookup);
            if (request == _navigation)
            {
                _ = SetProperty(ref _notice, ex.Message, nameof(Notice));
            }
        }
    }

    [LoggerMessage(1, LogLevel.Error, "Folder lookup {Lookup} failed for {Path}.")]
    private static partial void LogBrowseFailure(ILogger logger, Exception exception, string? path, FolderLookup lookup);
}
