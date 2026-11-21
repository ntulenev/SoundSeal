using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Reserves unique WAV destinations and owns the filesystem scheduling boundary.</summary>
public interface IRecordingStore
{
    /// <summary>Creates a temporary WAV under the selected absolute folder.</summary>
    Task<IRecordingDestination> CreateAsync(RecordingFolder folder, CancellationToken cancellationToken);
}
