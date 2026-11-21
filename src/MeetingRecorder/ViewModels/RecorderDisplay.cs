using System.Globalization;

using MeetingRecorder.Domain;

namespace MeetingRecorder.ViewModels;

/// <summary>English feedback and action availability derived from one recorder snapshot.</summary>
internal sealed record RecorderDisplay(RecorderState State, string Elapsed, Levels Peaks,
    RecordingFile? File, string Detail, bool CanCopyLink, IReadOnlyList<RecordingFile> Files)
{
    /// <summary>Whether a chronological list is needed for the published files.</summary>
    public bool ShowParts => Files.Count > 1 || (Files.Count > 0 && !CanCopyLink);

    /// <summary>Whether to show a single file or recovery path.</summary>
    public bool ShowSingleFile => File is not null && !ShowParts;

    /// <summary>Whether the user can change recording settings.</summary>
    public bool CanEdit => State is RecorderState.Ready or RecorderState.Saved or RecorderState.Error;

    /// <summary>Whether the current recording can be paused.</summary>
    public bool CanPause => State == RecorderState.Recording;

    /// <summary>Whether the paused session can accept new audio.</summary>
    public bool CanResume => State == RecorderState.Paused;

    /// <summary>Whether the current session can be finalized.</summary>
    public bool CanFinish => CanPause || CanResume;

    /// <summary>Caption for the shared pause and resume control.</summary>
    public string PauseLabel => CanResume || State == RecorderState.Resuming ? "Resume" : "Pause";

    /// <summary>Window title for the current recorder state.</summary>
    public string Title => State == RecorderState.Recording ? "Recording · SoundSeal" : "SoundSeal";

    /// <summary>English status text for the current recorder state.</summary>
    public string Status => State switch
    {
        RecorderState.Starting or RecorderState.Resuming => "Connecting to audio devices…",
        RecorderState.Pausing => "Pausing…",
        RecorderState.Saving => "Saving recording…",
        RecorderState.Ready or RecorderState.Recording or RecorderState.Paused
            or RecorderState.Saved or RecorderState.Error => State.ToString(),
        _ => "Unknown"
    };

    /// <summary>Color used by the status indicator.</summary>
    public string StatusColor => State switch
    {
        RecorderState.Recording or RecorderState.Error => "#FF7975",
        RecorderState.Paused or RecorderState.Pausing => "#F5C768",
        RecorderState.Ready or RecorderState.Starting or RecorderState.Resuming
            or RecorderState.Saving or RecorderState.Saved => "#63D9EF",
        _ => "#CBD4D0"
    };

    /// <summary>Accessible text for the microphone meter.</summary>
    public string MicrophoneSignal => Signal(Peaks.Microphone.Value);

    /// <summary>Accessible text for the playback meter.</summary>
    public string ComputerSignal => Signal(Peaks.Computer.Value);

    /// <summary>Logarithmically scaled microphone meter position.</summary>
    public double MicrophoneLevel => Meter(Peaks.Microphone.Value);

    /// <summary>Logarithmically scaled playback meter position.</summary>
    public double ComputerLevel => Meter(Peaks.Computer.Value);

    /// <summary>Builds one display update from the current timeline, meters and any user notice.</summary>
    internal static RecorderDisplay From(RecorderSnapshot snapshot, RecordingDuration elapsed,
        Levels peaks, string notice)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(notice);
        var result = snapshot.Result;
        var detail = notice;
        if (notice.Length == 0 && result?.Problem is not null)
        {
            var outcome = result.Path is null ? "No recording file was saved."
                : result.Published ? "Recording interrupted. Available audio saved."
                : "Unfinished file retained. " + result.Path.Value;
            detail = result.Problem + " " + outcome;
        }

        return new(snapshot.State, string.Create(CultureInfo.InvariantCulture,
            $"{(int)elapsed.Value.TotalHours:00}:{elapsed.Value.Minutes:00}:{elapsed.Value.Seconds:00}"),
            snapshot.State == RecorderState.Recording ? peaks : default, result?.Path, detail,
            result is { Published: true, Path: not null }, result?.PublishedFiles ?? []);
    }

    private double Meter(float peak) => State == RecorderState.Recording && peak > 0
        ? Math.Clamp(1 + (20 * Math.Log10(peak) / 60), 0, 1) : 0;

    private string Signal(float peak)
    {
        if (State is RecorderState.Paused or RecorderState.Pausing)
        {
            return "Paused";
        }

        if (State == RecorderState.Recording)
        {
            return peak >= 0.001f ? "Signal detected" : "Silent";
        }

        return "Idle";
    }
}
