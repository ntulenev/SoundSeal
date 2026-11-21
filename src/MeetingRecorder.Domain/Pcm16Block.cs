namespace MeetingRecorder.Domain;

/// <summary>A borrowed block of whole little-endian mono PCM16 samples.</summary>
public readonly ref struct Pcm16Block
{
    /// <summary>Rejects a trailing byte that would split a sample.</summary>
    public Pcm16Block(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % sizeof(short) != 0)
        {
            throw new ArgumentException("PCM16 blocks must contain complete samples.", nameof(bytes));
        }

        Bytes = bytes;
    }

    /// <summary>Sample data valid for the duration of the synchronous write.</summary>
    public ReadOnlySpan<byte> Bytes { get; }

    /// <summary>The number of whole output frames in this block.</summary>
    public FrameCount Frames => Bytes.Length / sizeof(short);
}
