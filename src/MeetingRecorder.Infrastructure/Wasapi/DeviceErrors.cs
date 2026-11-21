using System.Runtime.InteropServices;

using MeetingRecorder.Domain;

namespace MeetingRecorder.Infrastructure.Wasapi;

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
