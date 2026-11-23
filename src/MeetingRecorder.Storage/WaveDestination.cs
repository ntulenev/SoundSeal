using System.Buffers.Binary;

using MeetingRecorder.Domain;

namespace MeetingRecorder.Storage;

/// <summary>Streams mono PCM to a RIFF WAVE file.</summary>
internal sealed class WaveDestination : FileDestination
{
    private const long MAX_FRAMES = (uint.MaxValue - 36L) / 2;
    private readonly Stream _stream;

    internal WaveDestination(Stream stream, RecordingFile temporary, RecordingFile final)
        : base(temporary, final)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
    }

    /// <summary>Verifies that both RIFF length fields can represent the next block.</summary>
    internal static void CheckCapacity(FrameCount frames, FrameCount additional)
    {
        if (additional.Value > MAX_FRAMES - frames.Value)
        {
            throw new RecordingException("The WAV file reached the RIFF size limit.");
        }
    }

    internal void WriteHeader()
    {
        Span<byte> header = stackalloc byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)(36 + (Frames.Value * 2)));
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], RecordingFormat.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], RecordingFormat.SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], (uint)(Frames.Value * 2));
        _stream.Write(header);
    }

    protected override void WriteBlock(Pcm16Block block)
    {
        CheckCapacity(Frames, block.Frames);
        _stream.Write(block.Bytes);
    }

    protected override void FinishFile()
    {
        _stream.SetLength(44 + (Frames.Value * 2));
        _stream.Position = 0;
        WriteHeader();
        if (_stream is FileStream file)
        {
            file.Flush(flushToDisk: true);
        }
        else
        {
            _stream.Flush();
        }
    }

    protected override void CloseFile() => _stream.Dispose();
}
