using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Storage;

/// <summary>Reserves the WAV and its temporary sibling on a filesystem worker.</summary>
public sealed class WaveRecordingStore : IRecordingStore
{
    /// <inheritdoc/>
    public Task<IRecordingDestination> CreateAsync(RecordingSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Task.Run<IRecordingDestination>(() => settings.Layout == RecordingLayout.QuarterHourParts
            ? new SegmentedWaveDestination(settings.Folder, settings.Mode, DateTime.Now,
                new FrameCount(15L * 60 * RecordingFormat.SampleRate))
            : WaveDestination.Create(settings.Folder, DateTime.Now, settings.Mode), cancellationToken);
    }
}
