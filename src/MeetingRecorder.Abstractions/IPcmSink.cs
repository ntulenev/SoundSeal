using MeetingRecorder.Domain;

namespace MeetingRecorder.Abstractions;

/// <summary>Receives mixed mono samples in chronological order.</summary>
public interface IPcmSink
{
    /// <summary>Consumes the entire block before returning to the mixer.</summary>
    void Append(Pcm16Block block);
}
