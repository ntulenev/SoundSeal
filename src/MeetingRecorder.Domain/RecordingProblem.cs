namespace MeetingRecorder.Domain;

/// <summary>A nonblank explanation of an interruption or publication failure.</summary>
public sealed record RecordingProblem
{
    /// <summary>Preserves a nonblank message for display and recovery reporting.</summary>
    public RecordingProblem(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Message = value;
    }

    /// <summary>A nonblank explanation of an interruption or publication failure.</summary>
    public string Message { get; }

    /// <summary>Converts an optional value and preserves absence.</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(value))]
    public static RecordingProblem? FromString(string? value) => value is null ? null : new(value);

    /// <inheritdoc/>
    public override string ToString() => Message;

    /// <summary>Converts an optional value and preserves absence.</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(value))]
    public static implicit operator RecordingProblem?(string? value) => FromString(value);
}
