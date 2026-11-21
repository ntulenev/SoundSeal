using System.Buffers.Binary;
using System.Globalization;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Storage;

/// <summary>Streams complete PCM blocks to an exclusive sibling file until publication.</summary>
internal sealed class WaveDestination : IRecordingDestination
{
    private const long MAX_FRAMES = (uint.MaxValue - 36L) / 2;

    private readonly Stream _stream;
    private readonly RecordingFile _temporary;
    private readonly RecordingFile _final;
    private bool _writeFailed;

    /// <summary>Owns the writable WAV stream and its temporary and publication names.</summary>
    internal WaveDestination(Stream stream, RecordingFile temporary, RecordingFile final)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(temporary);
        ArgumentNullException.ThrowIfNull(final);
        _stream = stream;
        _temporary = temporary;
        _final = final;
    }

    /// <summary>Number of frames successfully committed to the stream.</summary>
    internal FrameCount Frames { get; private set; }

    /// <summary>Appends whole little-endian samples. Failed blocks remain uncommitted.</summary>
    public void Append(Pcm16Block block)
    {
        CheckCapacity(Frames, block.Frames);
        if (_writeFailed)
        {
            throw new RecordingException("Writing stopped after a disk error.");
        }

        try
        {
            _stream.Write(block.Bytes);
            Frames = Frames.Value + block.Frames.Value;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _writeFailed = true;
            throw new RecordingException("Writing stopped. The disk may be full " +
                "or the recording folder unavailable.", ex);
        }
    }

    /// <inheritdoc/>
    public Task<RecordingResult> CompleteAsync(EmptyRecordingPolicy emptyPolicy, RecordingProblem? problem, CancellationToken cancellationToken) =>
        Task.Run(() => Complete(emptyPolicy, problem), cancellationToken);

    /// <inheritdoc/>
    public void Dispose() => _stream.Dispose();

    /// <summary>Reserves an exclusive temporary file and writes its initial WAV header.</summary>
    internal static WaveDestination Create(RecordingFolder folder, DateTime localTime, RecordingMode mode = RecordingMode.Meeting)
    {
        ArgumentNullException.ThrowIfNull(folder);
        try
        {
            _ = Directory.CreateDirectory(folder.Value);
            var name = mode + "-" + localTime.ToString("yyyy-MM-dd_HH-mm-ss",
                CultureInfo.InvariantCulture);
            for (var suffix = 0; ; suffix++)
            {
                var stem = suffix == 0 ? name : FormattableString.Invariant($"{name}-{suffix}");
                var final = Path.Combine(folder.Value, stem + ".wav");
                var temp = final + ".partial";
                if (File.Exists(final) || File.Exists(temp))
                {
                    continue;
                }

                FileStream stream;
                try
                {
                    stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None);
                }
                catch (IOException) when (File.Exists(temp))
                {
                    continue;
                }

                var destination = new WaveDestination(stream, temp, final);
                try
                {
                    // Another instance can publish between our name check and CreateNew.
                    if (File.Exists(final))
                    {
                        destination.Dispose();
                        File.Delete(temp);
                        continue;
                    }

                    destination.WriteHeader();
                    return destination;
                }
                catch
                {
                    destination.Dispose();
                    File.Delete(temp);
                    throw;
                }
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            throw new RecordingException("Cannot create the recording file. " +
                "Check the folder, write permission and available disk space.", ex);
        }
    }

    /// <summary>Verifies that both RIFF length fields can represent the next block.</summary>
    internal static void CheckCapacity(FrameCount frames, FrameCount additional)
    {
        if (additional.Value > MAX_FRAMES - frames.Value)
        {
            throw new RecordingException("The WAV file reached the RIFF size limit.");
        }
    }

    /// <summary>Repairs the header and publishes the file or returns the surviving recovery path.</summary>
    internal RecordingResult Complete(EmptyRecordingPolicy emptyPolicy, RecordingProblem? problem)
    {
        if (!Enum.IsDefined(emptyPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(emptyPolicy));
        }

        try
        {
            if (emptyPolicy == EmptyRecordingPolicy.Discard && Frames.Value == 0)
            {
                _stream.Dispose();
                File.Delete(_temporary.Value);
                return new(null, 0, problem, false);
            }

            _stream.SetLength(44 + (Frames.Value * 2));
            _stream.Position = 0;
            WriteHeader();
            _stream.Flush();
            _stream.Dispose();
            File.Move(_temporary.Value, _final.Value);
            return new(_final, Frames, problem, true);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // Dispose can itself fail while flushing a full or disconnected disk.
            try
            {
                _stream.Dispose();
            }
            catch (IOException)
            {
                // Preserve any bytes already on disk for recovery.
            }

            return new(File.Exists(_temporary.Value) ? _temporary : null, Frames,
                (problem is null ? string.Empty : problem + " ") +
                "The WAV could not be finalized or published.", false);
        }
    }

    private void WriteHeader()
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

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException;
}
