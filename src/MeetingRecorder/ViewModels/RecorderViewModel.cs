using System.IO;

using MeetingRecorder.Application.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Platform;
using MeetingRecorder.Threading;

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

    public RecorderViewModel(IRecorder recorder, IDeviceCatalog devices, IUiTimer timer,
        IFolderLauncher folders, RecordingLinkClipboard clipboard)
    {
        _recorder = recorder;
        _devices = devices;
        _timer = timer;
        _folders = folders;
        _clipboard = clipboard;
        _display = ReadDisplay();
        _timer.Tick += OnTick;
    }

    public IReadOnlyList<Endpoint> Microphones { get; private set; } = [];
    public IReadOnlyList<Endpoint> Outputs { get; private set; } = [];
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
    public string Folder {
        get => _folder;
        set
        {
            if (SetProperty(ref _folder, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public RecorderDisplay Display => _display;
    public string CopyLinkStatus {
        get => _copyLinkStatus;
        private set => SetProperty(ref _copyLinkStatus, value);
    }
    public bool CanEdit => _display.CanEdit && !_refreshing;
    public bool CanStart => CanEdit && Microphone is not null && Output is not null
        && !string.IsNullOrWhiteSpace(Folder);

    internal async Task InitializeAsync()
    {
        _timer.Start();
        await RefreshDevicesAsync();
    }

    internal async Task RefreshDevicesAsync()
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
            var devices = await Task.Run(_devices.Read);
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

    private void ApplyDevices(Devices devices, string? mic, string? output)
    {
        Microphones = devices.Microphones;
        Outputs = devices.Outputs;
        OnPropertyChanged(nameof(Microphones));
        OnPropertyChanged(nameof(Outputs));
        Microphone = Select(Microphones, mic);
        Output = Select(Outputs, output);
    }

    private static Endpoint? Select(IReadOnlyList<Endpoint> list, string? id) =>
        list.FirstOrDefault(device => device.Id == id)
        ?? list.FirstOrDefault(device => device.IsDefault) ?? (list.Count == 0 ? null : list[0]);

    internal Task StartAsync()
    {
        if (!CanStart)
        {
            return Task.CompletedTask;
        }

        _notice = string.Empty;
        CopyLinkStatus = string.Empty;
        return ShowTransitionAsync(_recorder.StartAsync(new(Microphone!.Id, Output!.Id, Folder)));
    }

    internal Task TogglePauseAsync() => ShowTransitionAsync(Display.CanResume
        ? _recorder.ResumeAsync() : _recorder.PauseAsync());

    internal Task FinishAsync() => ShowTransitionAsync(_recorder.FinishAsync());

    private async Task ShowTransitionAsync(Task operation)
    {
        Refresh();
        await operation;
        Refresh();
    }

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

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
