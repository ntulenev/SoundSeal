using System.Windows.Threading;

namespace MeetingRecorder.Threading;

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
