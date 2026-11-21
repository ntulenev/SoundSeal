namespace MeetingRecorder.Threading;

/// <summary>A presentation-only pulse. Ticks never advance the recording timeline.</summary>
internal interface IUiTimer
{
    event EventHandler? Tick;
    void Start();
    void Stop();
}
