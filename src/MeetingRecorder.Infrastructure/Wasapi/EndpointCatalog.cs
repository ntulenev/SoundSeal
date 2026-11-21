using System.Runtime.InteropServices;

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

internal static class DeviceErrors
{
    internal static bool IsDeviceError(Exception ex) =>
        ex is COMException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or IOException or NotSupportedException;

    internal static RecordingException Translate(bool loopback, Exception ex)
    {
        var source = loopback ? "Computer audio" : "Microphone";
        var cause = ex.HResult switch
        {
            unchecked((int)0x80070005) when !loopback =>
                "access was denied. In Windows Settings, open Privacy & security > " +
                "Microphone and allow desktop apps to access your microphone.",
            unchecked((int)0x80070005) => "access was denied.",
            unchecked((int)0x8889000A) => "is busy. Close the app using the device in exclusive mode.",
            unchecked((int)0x88890008) => "uses an unsupported audio format.",
            _ => "is unavailable or was disconnected. Check its connection and refresh devices."
        };
        return new(source + " " + cause, ex);
    }
}
