using System.IO;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Platform;

namespace MeetingRecorder.ViewModels;

/// <summary>Binds the window to the coordinator. Mutations run on the UI dispatcher.</summary>
internal sealed class RecorderViewModel : ObservableObject, IDisposable
{
    private readonly IRecorder _recorder;
    private readonly IDeviceCatalog _devices;
    private readonly IUiTimer _timer;
    private readonly IFolderLauncher _folders;
    private readonly RecordingLinkClipboard _clipboard;
    private string _folder = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "MeetingRecorder", "Recordings");
    private Endpoint? _microphone;
    private Endpoint? _output;
    private string _notice = string.Empty;
    private string _copyLinkStatus = string.Empty;
    private RecorderDisplay _display;
    private bool _refreshing;

    /// <summary>Connects the recorder, device catalog, dispatcher timer, shell and clipboard to the window.</summary>
    public RecorderViewModel(IRecorder recorder, IDeviceCatalog devices, IUiTimer timer,
        IFolderLauncher folders, RecordingLinkClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(clipboard);
        _recorder = recorder;
        _devices = devices;
        _timer = timer;
        _folders = folders;
        _clipboard = clipboard;
        _display = ReadDisplay();
        _timer.Tick += OnTick;
    }

    /// <summary>Microphone endpoints available for selection.</summary>
    public IReadOnlyList<Endpoint> Microphones { get; private set; } = [];

    /// <summary>Playback endpoints available for loopback capture.</summary>
    public IReadOnlyList<Endpoint> Outputs { get; private set; } = [];

    /// <summary>The microphone selected for the next recording.</summary>
    public Endpoint? Microphone {
        get => _microphone;
        set
        {
            if (SetProperty(ref _microphone, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    /// <summary>The playback endpoint selected for the next recording.</summary>
    public Endpoint? Output {
        get => _output;
        set
        {
            if (SetProperty(ref _output, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    /// <summary>Editable destination folder text.</summary>
    public string Folder {
        get => _folder;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _folder, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    /// <summary>Latest display values for this window.</summary>
    public RecorderDisplay Display => _display;

    /// <summary>Feedback from the last attempt to copy a recording link.</summary>
    public string CopyLinkStatus {
        get => _copyLinkStatus;
        private set => SetProperty(ref _copyLinkStatus, value);
    }

    /// <summary>Whether the recording settings can currently be changed.</summary>
    public bool CanEdit => _display.CanEdit && !_refreshing;

    /// <summary>Whether both sources and an absolute recording folder are available.</summary>
    public bool CanStart => CanEdit && Microphone is not null && Output is not null
        && RecordingFolder.TryParse(Folder, out _);

    /// <inheritdoc/>
    public void Dispose()
    {
        _timer.StopTicks();
        _timer.Tick -= OnTick;
    }

    /// <summary>Starts display updates and loads the available audio devices.</summary>
    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _timer.StartTicks();
        await RefreshDevicesAsync(cancellationToken);
    }

    /// <summary>Refreshes both endpoint lists while preserving available selections.</summary>
    internal async Task RefreshDevicesAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit)
        {
            return;
        }

        _refreshing = true;
        var mic = Microphone?.Id;
        var output = Output?.Id;
        _notice = string.Empty;
        Refresh();
        try
        {
            var devices = await _devices.ReadAsync(cancellationToken);
            ApplyDevices(devices, mic, output);
            _notice = (Microphone, Output) switch
            {
                (null, null) => "Microphone and computer audio are unavailable. " +
                    "Connect devices and choose Refresh devices.",
                (null, _) => "Microphone unavailable. Connect an input and choose Refresh devices.",
                (_, null) => "Computer audio unavailable. Connect a playback device and choose Refresh devices.",
                _ => string.Empty
            };
        }
        catch (RecordingException ex)
        {
            ApplyDevices(new([], []), null, null);
            _notice = ex.Message;
        }
        finally
        {
            _refreshing = false;
            Refresh();
        }
    }

    /// <summary>Starts recording with the currently selected endpoints and folder.</summary>
    internal Task StartAsync(CancellationToken cancellationToken)
    {
        if (!CanStart)
        {
            return Task.CompletedTask;
        }

        _notice = string.Empty;
        CopyLinkStatus = string.Empty;
        return ShowTransitionAsync(_recorder.StartAsync(new(Microphone!.Id, Output!.Id, Folder), cancellationToken), cancellationToken);
    }

    /// <summary>Pauses or resumes the active session.</summary>
    internal Task TogglePauseAsync(CancellationToken cancellationToken) => ShowTransitionAsync(Display.CanResume
        ? _recorder.ResumeAsync(cancellationToken) : _recorder.PauseAsync(cancellationToken), cancellationToken);

    /// <summary>Waits for publication and refreshes the saved recording details.</summary>
    internal Task FinishAsync(CancellationToken cancellationToken) => ShowTransitionAsync(_recorder.FinishAsync(cancellationToken), cancellationToken);

    /// <summary>Copies a link to the published recording and updates the clipboard status.</summary>
    internal void CopyRecordingLink()
    {
        if (Display is not { CanCopyLink: true, File: { } file })
        {
            return;
        }

        try
        {
            _clipboard.Copy(file);
            CopyLinkStatus = "Link copied.";
        }
        catch (RecordingException ex)
        {
            CopyLinkStatus = ex.Message;
        }
    }

    /// <summary>Opens the folder containing the published or recoverable file.</summary>
    internal void OpenFolder()
    {
        if (Display.File is not { } path)
        {
            return;
        }

        try
        {
            _folders.Open(path);
        }
        catch (RecordingException ex)
        {
            _notice = ex.Message;
            Refresh();
        }
    }

    private void ApplyDevices(Devices devices, EndpointId? mic, EndpointId? output)
    {
        Microphones = devices.Microphones;
        Outputs = devices.Outputs;
        OnPropertyChanged(nameof(Microphones));
        OnPropertyChanged(nameof(Outputs));
        Microphone = Select(Microphones, mic);
        Output = Select(Outputs, output);
    }

    private static Endpoint? Select(IReadOnlyList<Endpoint> list, EndpointId? id) =>
        list.FirstOrDefault(device => device.Id == id)
        ?? list.FirstOrDefault(device => device.IsDefault) ?? (list.Count == 0 ? null : list[0]);

    private async Task ShowTransitionAsync(Task operation, CancellationToken cancellationToken)
    {
        Refresh();
        try
        {
            await operation;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            Refresh();
        }
    }

    private RecorderDisplay ReadDisplay() => RecorderDisplay.From(_recorder.Snapshot,
        _recorder.Elapsed, _recorder.Levels, _notice);

    private void OnTick(object? sender, EventArgs e)
    {
        if (SetProperty(ref _display, ReadDisplay(), nameof(Display)))
        {
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanStart));
        }
    }

    private void Refresh()
    {
        _ = SetProperty(ref _display, ReadDisplay(), nameof(Display));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanStart));
    }
}
