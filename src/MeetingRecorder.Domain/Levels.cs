namespace MeetingRecorder.Domain;

/// <summary>Pair of normalized post-gain signal peaks.</summary>
public readonly record struct Levels
{
    /// <summary>Creates a validated pair of normalized post-gain signal peaks.</summary>
    /// <param name="microphone">Microphone peak from zero through one.</param>
    /// <param name="computer">Computer audio peak from zero through one.</param>
    public Levels(SignalPeak microphone, SignalPeak computer)
    {
        Microphone = microphone;
        Computer = computer;
    }

    /// <summary>Microphone peak from zero through one.</summary>
    public SignalPeak Microphone { get; }

    /// <summary>Computer audio peak from zero through one.</summary>
    public SignalPeak Computer { get; }
}
