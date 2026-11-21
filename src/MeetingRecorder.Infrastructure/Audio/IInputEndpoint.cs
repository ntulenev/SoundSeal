namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>One native input stream. All operations run on its owning capture thread.</summary>
internal interface IInputEndpoint : IDisposable
{
    /// <summary>Native device engine period used when draining the final buffered packet.</summary>
    TimeSpan EnginePeriod { get; }

    /// <summary>Starts the pinned audio client on the capture thread.</summary>
    void Start();

    /// <summary>Transfers owned packet copies to the consumer and releases native buffers.</summary>
    void Drain(Action<AudioPacket> accept);
}
