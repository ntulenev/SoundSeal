using System.Windows.Threading;

using MeetingRecorder.Abstractions;

namespace MeetingRecorder.Threading;

/// <summary>Raises recording display updates on the WPF dispatcher.</summary>
internal sealed class UiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(100)
    };

    /// <inheritdoc/>
    public event EventHandler? Tick {
        add => _timer.Tick += value;
        remove => _timer.Tick -= value;
    }

    /// <inheritdoc/>
    public void StartTicks() => _timer.Start();

    /// <inheritdoc/>
    public void StopTicks() => _timer.Stop();
}
