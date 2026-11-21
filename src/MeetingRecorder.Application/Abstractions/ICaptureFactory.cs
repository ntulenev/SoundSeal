using MeetingRecorder.Domain;

namespace MeetingRecorder.Application.Abstractions;

/// <summary>Creates an isolated, file-owning capture session.</summary>
public interface ICaptureFactory
{
    /// <summary>Reserves an output file for a session without starting either device.</summary>
    /// <param name="settings">Pinned endpoint IDs and the destination folder.</param>
    /// <returns>A session owned by the caller until finalization and disposal.</returns>
    /// <exception cref="RecordingException">The destination could not be opened.</exception>
    ICaptureSession Open(RecordingSettings settings);
}
