using System.Diagnostics;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Converts the system performance counter to the timeline used by WASAPI.</summary>
internal sealed class PerformanceClock : IRecordingClock
{
    /// <inheritdoc/>
    public CaptureTimestamp Now => (long)((Int128)Stopwatch.GetTimestamp() * TimeSpan.TicksPerSecond
        / Stopwatch.Frequency);
}
