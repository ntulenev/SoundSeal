using System.IO;
using System.Windows;

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
    private string _notice = string.Empty;
    private RecorderDisplay _display;

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

    /// <summary>Recording mode selected before starting a session.</summary>
    public RecordingMode Mode {
        get;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (CanEdit && SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(UsesMicrophone));
                OnPropertyChanged(nameof(UsesOutput));
                Refresh();
            }
        }
    }

    /// <summary>Whether to publish each fifteen minutes of active audio as a separate WAV.</summary>
    public bool SplitRecording {
        get;
        set
        {
            if (CanEdit)
            {
                _ = SetProperty(ref field, value);
            }
        }
    } = true;

    /// <summary>Whether this mode includes the microphone.</summary>
    public bool UsesMicrophone => Mode != RecordingMode.Audio;

    /// <summary>Whether this mode includes playback.</summary>
    public bool UsesOutput => Mode != RecordingMode.Dictaphone;

    /// <summary>Microphone endpoints available for selection.</summary>
    public IReadOnlyList<Endpoint> Microphones { get; private set; } = [];

    /// <summary>Playback endpoints available for loopback capture.</summary>
    public IReadOnlyList<Endpoint> Outputs { get; private set; } = [];

    /// <summary>The microphone selected for the next recording.</summary>
    public Endpoint? Microphone {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>The playback endpoint selected for the next recording.</summary>
    public Endpoint? Output {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>Editable destination folder text.</summary>
    public string Folder {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    } = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "MeetingRecorder", "Recordings");

    /// <summary>Latest display values for this window.</summary>
    public RecorderDisplay Display => _display;

    /// <summary>Feedback from opening the recording folder, copying a link or dragging a WAV.</summary>
    public string FileActionStatus {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>Whether the recording settings can currently be changed.</summary>
    public bool CanEdit => _display.CanEdit && !IsRefreshing;

    /// <summary>Whether the selected audio sources are ready for a sound check.</summary>
    public bool CanCheckSound => CanEdit && (!UsesMicrophone || Microphone is not null) && (!UsesOutput || Output is not null);

    /// <summary>Whether the selected mode has its required sources and a valid destination folder.</summary>
    public bool CanStart => CanCheckSound && RecordingFolder.TryParse(Folder, out _);

    private bool IsRefreshing { get; set; }

    private bool HasDeviceList { get; set; }

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

        IsRefreshing = true;
        var mic = Microphone?.Id;
        var output = Output?.Id;
        _notice = string.Empty;
        Refresh();
        try
        {
            var devices = await _devices.ReadAsync(cancellationToken);
            ApplyDevices(devices, mic, output);
            HasDeviceList = true;
        }
        catch (RecordingException ex)
        {
            ApplyDevices(new([], []), null, null);
            _notice = ex.Message;
        }
        finally
        {
            IsRefreshing = false;
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
        FileActionStatus = string.Empty;
        return ShowTransitionAsync(_recorder.StartAsync(new(Microphone?.Id, Output?.Id, Folder, Mode,
            SplitRecording ? RecordingLayout.QuarterHourParts : RecordingLayout.SingleFile), cancellationToken), cancellationToken);
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

        CopyRecordingLink(file);
    }

    /// <summary>Copies a specific published part selected in the result list.</summary>
    internal void CopyRecordingLink(RecordingFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!Display.Files.Contains(file))
        {
            return;
        }

        try
        {
            _clipboard.Copy(file);
            FileActionStatus = "Link copied.";
        }
        catch (RecordingException ex)
        {
            FileActionStatus = ex.Message;
        }
    }

    /// <summary>Creates a copy-only shell file list from the current published files, in recording order.</summary>
    internal DataObject? CreateDragData(RecordingFile? part)
    {
        var files = Display.Files;
        if (part is not null)
        {
            if (!files.Contains(part))
            {
                return null;
            }
            files = [part];
        }
        if (files.Count == 0)
        {
            return null;
        }

        var paths = new string[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            if (!File.Exists(file.Value))
            {
                FileActionStatus = $"Cannot drag the recording. File unavailable: {file.Name}";
                return null;
            }
            paths[i] = file.Value;
        }
        FileActionStatus = string.Empty;
        return new DataObject(DataFormats.FileDrop, paths);
    }

    /// <summary>Opens the result folder when available, or the selected destination before publication.</summary>
    internal void OpenFolder()
    {
        try
        {
            if (Display.File is { } file)
            {
                _folders.Open(file);
            }
            else if (RecordingFolder.TryParse(Folder, out var folder))
            {
                _folders.OpenDestination(folder);
            }
            else
            {
                FileActionStatus = "Enter a full recording folder path or choose Browse.";
                return;
            }

            FileActionStatus = string.Empty;
        }
        catch (RecordingException ex)
        {
            FileActionStatus = ex.Message;
        }
    }

    private string MissingDeviceNotice() => (UsesMicrophone && Microphone is null, UsesOutput && Output is null) switch
    {
        (true, true) => "Microphone and computer audio are unavailable. " +
            "Connect devices and choose Refresh devices.",
        (true, _) => "Microphone unavailable. Connect an input and choose Refresh devices.",
        (_, true) => "Computer audio unavailable. Connect a playback device and choose Refresh devices.",
        _ => string.Empty
    };

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

    private RecorderDisplay ReadDisplay()
    {
        var snapshot = _recorder.Snapshot;
        var notice = HasDeviceList && _notice.Length == 0 && snapshot.State is RecorderState.Ready or RecorderState.Saved
            ? MissingDeviceNotice() : _notice;
        return RecorderDisplay.From(snapshot, _recorder.Elapsed, _recorder.Levels, notice);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (SetProperty(ref _display, ReadDisplay(), nameof(Display)))
        {
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanCheckSound));
            OnPropertyChanged(nameof(CanStart));
        }
    }

    private void Refresh()
    {
        _ = SetProperty(ref _display, ReadDisplay(), nameof(Display));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanCheckSound));
        OnPropertyChanged(nameof(CanStart));
    }
}
