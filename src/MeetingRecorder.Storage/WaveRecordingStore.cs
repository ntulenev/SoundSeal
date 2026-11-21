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

        return Task.Run<IRecordingDestination>(() => WaveDestination.Create(settings.Folder, DateTime.Now, settings.Mode), cancellationToken);
    }
}
