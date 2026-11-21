using System.Buffers.Binary;
using System.Globalization;

using MeetingRecorder.Domain;

namespace MeetingRecorder.Infrastructure.Files;

/// <summary>Streams complete PCM blocks to an exclusive sibling file until publication.</summary>
internal sealed class WaveDestination : IDisposable
{
    private readonly Stream _stream;
    private readonly string _temporary;
    private readonly string _final;
    private bool _writeFailed;
    private const long MAX_FRAMES = (uint.MaxValue - 36L) / 2;

    internal WaveDestination(Stream stream, string temporary, string final)
    {
        _stream = stream;
        _temporary = temporary;
        _final = final;
    }

    internal long Frames { get; private set; }

    internal static WaveDestination Create(string folder, DateTime localTime)
    {
        try
        {
            if (!Path.IsPathFullyQualified(folder))
            {
                throw new RecordingException("Choose a recording folder with a full path.");
            }

            _ = Directory.CreateDirectory(folder);
            var name = localTime.ToString("'Meeting-'yyyy-MM-dd_HH-mm-ss",
                CultureInfo.InvariantCulture);
            for (var suffix = 0; ; suffix++)
            {
                var stem = suffix == 0 ? name : FormattableString.Invariant($"{name}-{suffix}");
                var final = Path.Combine(folder, stem + ".wav");
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

    /// <summary>Appends whole little-endian samples. Failed blocks remain uncommitted.</summary>
    internal void Append(ReadOnlySpan<byte> pcm)
    {
        CheckCapacity(Frames, pcm.Length);
        if (_writeFailed)
        {
            throw new RecordingException("Writing stopped after a disk error.");
        }

        try
        {
            _stream.Write(pcm);
            Frames += pcm.Length / 2;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _writeFailed = true;
            throw new RecordingException("Writing stopped. The disk may be full " +
                "or the recording folder unavailable.", ex);
        }
    }

    internal static void CheckCapacity(long frames, int bytes)
    {
        if (frames + (bytes / 2) > MAX_FRAMES)
        {
            throw new RecordingException("The WAV file reached the RIFF size limit.");
        }
    }

    internal RecordingResult Complete(bool discardEmpty, string? problem)
    {
        try
        {
            if (discardEmpty && Frames == 0)
            {
                _stream.Dispose();
                File.Delete(_temporary);
                return new(null, 0, problem, false);
            }

            _stream.SetLength(44 + (Frames * 2));
            _stream.Position = 0;
            WriteHeader();
            _stream.Flush();
            _stream.Dispose();
            File.Move(_temporary, _final);
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

            return new(File.Exists(_temporary) ? _temporary : null, Frames,
                (problem is null ? string.Empty : problem + " ") +
                "The WAV could not be finalized or published.", false);
        }
    }

    private void WriteHeader()
    {
        Span<byte> header = stackalloc byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)(36 + (Frames * 2)));
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], RecordingFormat.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], RecordingFormat.SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], (uint)(Frames * 2));
        _stream.Write(header);
    }

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException;

    public void Dispose() => _stream.Dispose();
}
