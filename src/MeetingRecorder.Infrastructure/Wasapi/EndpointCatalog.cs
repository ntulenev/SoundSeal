using MeetingRecorder.Application.Abstractions;
using MeetingRecorder.Domain;

using NAudio.CoreAudioApi;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>Enumerates endpoints without enabling capture or microphone monitoring.</summary>
internal sealed class EndpointCatalog : IDeviceCatalog
{
    public Devices Read()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            return new(List(devices, DataFlow.Capture), List(devices, DataFlow.Render));
        }
        catch (Exception ex) when (DeviceErrors.IsDeviceError(ex))
        {
            throw new RecordingException("Windows audio devices could not be listed. " +
                "Check the audio service and refresh devices.", ex);
        }
    }

    private static List<Endpoint> List(MMDeviceEnumerator devices, DataFlow flow)
    {
        using var preferred = devices.HasDefaultAudioEndpoint(flow, Role.Multimedia)
            ? devices.GetDefaultAudioEndpoint(flow, Role.Multimedia) : null;
        var defaultId = preferred?.ID;
        var result = new List<Endpoint>();
        foreach (var device in devices.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                result.Add(new(device.ID, device.FriendlyName, device.ID == defaultId));
            }
        }

        return result;
    }
}
