using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Checks the selected capture sources using a short in-memory mono PCM16 sample.</summary>
public interface ISoundCheck
{
    /// <summary>Captures five seconds, playing a test tone on the selected loopback device and reporting peak levels.</summary>
    Task<byte[]> CaptureAsync(EndpointId? microphone, EndpointId? output, IProgress<Levels> peaks,
        CancellationToken cancellationToken);

    /// <summary>Plays the captured mix on the selected output, or the Windows default for a microphone-only check.</summary>
    Task PlayAsync(EndpointId? output, byte[] sample, CancellationToken cancellationToken);
}
