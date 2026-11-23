using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Reserves unique recording destinations and owns the filesystem scheduling boundary.</summary>
public interface IRecordingStore
{
    /// <summary>Creates a temporary audio file under the selected absolute folder.</summary>
    Task<IRecordingDestination> CreateAsync(RecordingSettings settings, CancellationToken cancellationToken);
}
