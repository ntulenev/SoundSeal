using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

using NAudio.CoreAudioApi;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>Enumerates endpoints without enabling capture or microphone monitoring.</summary>
internal sealed class EndpointCatalog : IDeviceCatalog
{
    /// <inheritdoc/>
    public Task<Devices> ReadAsync(CancellationToken cancellationToken) => Task.Run(() => Read(cancellationToken), cancellationToken);

    private static Devices Read(CancellationToken cancellationToken)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            return new(List(devices, DataFlow.Capture, cancellationToken), List(devices, DataFlow.Render, cancellationToken));
        }
        catch (Exception ex) when (DeviceErrors.IsDeviceError(ex))
        {
            throw new RecordingException("Windows audio devices could not be listed. " +
                "Check the audio service and refresh devices.", ex);
        }
    }

    private static List<Endpoint> List(MMDeviceEnumerator devices, DataFlow flow, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var preferred = devices.HasDefaultAudioEndpoint(flow, Role.Multimedia)
            ? devices.GetDefaultAudioEndpoint(flow, Role.Multimedia) : null;
        var defaultId = preferred?.ID;
        var result = new List<Endpoint>();
        foreach (var device in devices.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Add(new(device.ID, device.FriendlyName, device.ID == defaultId));
            }
        }

        return result;
    }
}
