using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Enumerates active devices without opening a recording stream.</summary>
public interface IDeviceCatalog
{
    /// <summary>Reads active endpoints and identifies the Windows multimedia defaults.</summary>
    /// <returns>Snapshots of capture and playback identities and driver-supplied names.</returns>
    /// <exception cref="RecordingException">Windows audio enumeration failed.</exception>
    Task<Devices> ReadAsync(CancellationToken cancellationToken);
}
