using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

using Microsoft.Extensions.Logging;

namespace MeetingRecorder.Storage;

/// <summary>Reserves the selected audio file and its temporary sibling on a filesystem worker.</summary>
public sealed class WaveRecordingStore(ILogger<WaveRecordingStore>? logger = null) : IRecordingStore
{
    /// <inheritdoc/>
    public Task<IRecordingDestination> CreateAsync(RecordingSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Task.Run<IRecordingDestination>(() => settings.Layout == RecordingLayout.QuarterHourParts
            ? new SegmentedFileDestination(settings.Folder, settings.Mode, DateTime.Now,
                new FrameCount(15L * 60 * RecordingFormat.SampleRate), settings.Format, logger)
            : FileDestination.Create(settings.Folder, DateTime.Now, settings.Mode, settings.Format, logger), cancellationToken);
    }
}
