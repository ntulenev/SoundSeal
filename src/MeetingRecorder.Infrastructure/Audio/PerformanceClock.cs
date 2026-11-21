using System.Diagnostics;

using MeetingRecorder.Application.Abstractions;

namespace MeetingRecorder.Infrastructure.Audio;

internal sealed class PerformanceClock : IRecordingClock
{
    public long Now => (long)((Int128)Stopwatch.GetTimestamp() * TimeSpan.TicksPerSecond
        / Stopwatch.Frequency);
}
