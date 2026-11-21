using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>A session-owned sink for mono PCM16 samples.</summary>
public interface IRecordingDestination : IPcmSink, IDisposable
{
    /// <summary>Closes the file and reports the published or recoverable path.</summary>
    Task<RecordingResult> CompleteAsync(EmptyRecordingPolicy emptyPolicy, RecordingProblem? problem, CancellationToken cancellationToken);
}
