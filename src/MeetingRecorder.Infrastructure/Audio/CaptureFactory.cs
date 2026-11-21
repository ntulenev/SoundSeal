using MeetingRecorder.Application.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Files;
using MeetingRecorder.Infrastructure.Wasapi;

namespace MeetingRecorder.Infrastructure.Audio;

internal sealed class CaptureFactory : ICaptureFactory
{
    private readonly IRecordingClock _clock;

    public CaptureFactory(IRecordingClock clock)
    {
        _clock = clock;
    }

    public ICaptureSession Open(RecordingSettings settings) =>
        new CaptureSession(settings, _clock, WaveDestination.Create(settings.Folder,
            DateTime.Now), WasapiEndpoint.Open);
}
