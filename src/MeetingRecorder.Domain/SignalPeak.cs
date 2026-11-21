namespace MeetingRecorder.Domain;

/// <summary>A finite normalized peak in the closed interval from zero to one.</summary>
public readonly record struct SignalPeak
{
    /// <summary>Creates a peak from a finite normalized amplitude.</summary>
    public SignalPeak(float value)
    {
        if (!float.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A signal peak must be finite and between zero and one.");
        }

        Value = value;
    }

    /// <summary>A finite normalized peak in the closed interval from zero to one.</summary>
    public float Value { get; }

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static SignalPeak FromSingle(float value) => new(value);

    /// <summary>Validates a raw value at an API boundary.</summary>
    public static implicit operator SignalPeak(float value) => FromSingle(value);
}
