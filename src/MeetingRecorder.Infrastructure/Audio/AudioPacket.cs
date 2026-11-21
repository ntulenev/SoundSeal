namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Owned copy of one capture packet. Position counts native frames since the
/// device stream started. Qpc identifies its first frame in shared 100 ns ticks.
/// A null timestamp denotes a driver error. Empty data denotes digital silence.
/// The producer transfers the array to the mixer and never reuses it.</summary>
internal sealed record AudioPacket(bool Loopback, InputFormat Format, long Position,
    long? Qpc, int Frames, byte[] Data, bool Discontinuity = false);
