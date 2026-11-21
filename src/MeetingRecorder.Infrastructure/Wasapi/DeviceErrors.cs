using System.Runtime.InteropServices;

using MeetingRecorder.Domain;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>Translates native audio errors into actionable recording failures.</summary>
internal static class DeviceErrors
{
    /// <summary>Identifies failures reported by Windows audio APIs.</summary>
    internal static bool IsDeviceError(Exception ex) =>
        ex is COMException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or IOException or NotSupportedException;

    /// <summary>Identifies the failing device role and preserves the native exception.</summary>
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
