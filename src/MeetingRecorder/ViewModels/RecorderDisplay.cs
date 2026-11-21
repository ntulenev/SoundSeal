using System.Globalization;

using MeetingRecorder.Domain;

namespace MeetingRecorder.ViewModels;

/// <summary>English feedback and action availability derived from one recorder snapshot.</summary>
internal sealed record RecorderDisplay(RecorderState State, string Elapsed, Levels Peaks,
    string? File, string Detail)
{
    internal static RecorderDisplay From(RecorderSnapshot snapshot, TimeSpan elapsed,
        Levels peaks, string notice)
    {
        var result = snapshot.Result;
        var detail = notice;
        if (notice.Length == 0 && result?.Problem is not null)
        {
            var outcome = result.Path is null ? "No recording file was saved."
                : result.Published ? "Recording interrupted. Available audio saved."
                : "Unfinished file retained.";
            detail = result.Problem + " " + outcome;
        }

        return new(snapshot.State, string.Create(CultureInfo.InvariantCulture,
            $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"),
            snapshot.State == RecorderState.Recording ? peaks : default, result?.Path, detail);
    }

    public bool CanEdit => State is RecorderState.Ready or RecorderState.Saved or RecorderState.Error;
    public bool CanPause => State == RecorderState.Recording;
    public bool CanResume => State == RecorderState.Paused;
    public bool CanFinish => CanPause || CanResume;
    public string PauseLabel => CanResume || State == RecorderState.Resuming ? "Resume" : "Pause";
    public string Title => State == RecorderState.Recording ? "Recording · SoundSeal" : "SoundSeal";
    public string Status => State switch
    {
        RecorderState.Starting or RecorderState.Resuming => "Connecting to audio devices…",
        RecorderState.Pausing => "Pausing…",
        RecorderState.Saving => "Saving recording…",
        RecorderState.Ready or RecorderState.Recording or RecorderState.Paused
            or RecorderState.Saved or RecorderState.Error => State.ToString(),
        _ => "Unknown"
    };

    public string StatusColor => State switch
    {
        RecorderState.Recording or RecorderState.Error => "#FF7975",
        RecorderState.Paused or RecorderState.Pausing => "#F5C768",
        RecorderState.Ready or RecorderState.Starting or RecorderState.Resuming
            or RecorderState.Saving or RecorderState.Saved => "#63D9EF",
        _ => "#CBD4D0"
    };

    public string MicrophoneSignal => Signal(Peaks.Microphone);
    public string ComputerSignal => Signal(Peaks.Computer);
    public double MicrophoneLevel => Meter(Peaks.Microphone);
    public double ComputerLevel => Meter(Peaks.Computer);

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
