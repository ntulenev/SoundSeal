using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Storage;

/// <summary>Reserves the WAV and its temporary sibling on a filesystem worker.</summary>
public sealed class WaveRecordingStore : IRecordingStore
{
    /// <inheritdoc/>
    public Task<IRecordingDestination> CreateAsync(RecordingFolder folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return Task.Run<IRecordingDestination>(() => WaveDestination.Create(folder, DateTime.Now), cancellationToken);
    }
}
