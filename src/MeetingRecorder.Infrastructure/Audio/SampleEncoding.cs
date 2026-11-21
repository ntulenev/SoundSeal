namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Native little-endian layouts supported by the sample decoder.</summary>
internal enum SampleEncoding
{
    Pcm8,
    Pcm16,
    Pcm24,
    Pcm32,
    Float32,
    Float64
}
