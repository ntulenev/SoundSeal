namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>One native input stream. All operations run on its owning capture thread.</summary>
internal interface IInputEndpoint : IDisposable
{
    TimeSpan EnginePeriod { get; }
    void Start();
    void Drain(Action<AudioPacket> accept);
}
