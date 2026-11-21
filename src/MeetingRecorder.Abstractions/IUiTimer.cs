namespace MeetingRecorder.Abstractions;

/// <summary>A presentation-only pulse. Ticks never advance the recording timeline.</summary>
public interface IUiTimer
{
    /// <summary>Raised on the presentation dispatcher.</summary>
    event EventHandler? Tick;

    /// <summary>Starts UI refresh notifications.</summary>
    void StartTicks();

    /// <summary>Stops UI refresh notifications.</summary>
    void StopTicks();
}
