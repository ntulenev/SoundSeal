using System.Windows.Threading;

namespace MeetingRecorder.Threading;

/// <summary>A presentation-only pulse. Ticks never advance the recording timeline.</summary>
internal interface IUiTimer
{
    event EventHandler? Tick;
    void Start();
    void Stop();
}

internal sealed class UiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(100)
    };

    public event EventHandler? Tick {
        add => _timer.Tick += value;
        remove => _timer.Tick -= value;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}
