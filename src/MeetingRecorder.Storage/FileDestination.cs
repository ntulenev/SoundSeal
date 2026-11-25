using System.Globalization;
using System.Runtime.InteropServices;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingRecorder.Storage;

/// <summary>Reserves a unique path, counts captured frames and publishes a completed file.</summary>
internal abstract partial class FileDestination : IRecordingDestination
{
    private readonly RecordingFile _temporary;
    private readonly RecordingFile _final;
    private readonly ILogger _logger;
    private bool _writeFailed;
    private bool _closed;

    protected FileDestination(RecordingFile temporary, RecordingFile final, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(temporary);
        ArgumentNullException.ThrowIfNull(final);
        _temporary = temporary;
        _final = final;
        _logger = logger ?? NullLogger.Instance;
    }

    internal FrameCount Frames { get; private set; }

    public void Append(Pcm16Block block)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_writeFailed)
        {
            throw new RecordingException("Writing stopped after a disk or encoding error.");
        }

        try
        {
            WriteBlock(block);
            Frames = Frames.Value + block.Frames.Value;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _writeFailed = true;
            throw new RecordingException("Writing stopped. Check the disk space and recording folder.", ex);
        }
    }

    public Task<RecordingResult> CompleteAsync(EmptyRecordingPolicy emptyPolicy, RecordingProblem? problem, CancellationToken cancellationToken) =>
        Task.Run(() => Complete(emptyPolicy, problem), cancellationToken);

    public void Dispose()
    {
        _closed = true;
        CloseFile();
        GC.SuppressFinalize(this);
    }

    /// <summary>Reserves the file for a single recording.</summary>
    internal static FileDestination Create(RecordingFolder folder, DateTime localTime, RecordingMode mode = RecordingMode.Meeting,
        RecordingFileFormat format = RecordingFileFormat.Wav, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var name = mode + "-" + localTime.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        return Reserve(folder, name, format, logger);
    }

    /// <summary>Reserves a unique sibling for a storage-generated filename stem.</summary>
    internal static FileDestination Reserve(RecordingFolder folder, string name, RecordingFileFormat format, ILogger? logger = null)
    {
        try
        {
            _ = Directory.CreateDirectory(folder.Value);
            for (var suffix = 0; ; suffix++)
            {
                var stem = suffix == 0 ? name : FormattableString.Invariant($"{name}-{suffix}");
                var final = Path.Combine(folder.Value, stem + (format == RecordingFileFormat.Mp3 ? ".mp3" : ".wav"));
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

                try
                {
                    // Another instance can publish between our name check and CreateNew.
                    if (File.Exists(final))
                    {
                        stream.Dispose();
                        File.Delete(temp);
                        continue;
                    }

                    if (format == RecordingFileFormat.Mp3)
                    {
                        if (!OperatingSystem.IsWindows())
                        {
                            throw new RecordingException("MP3 recording requires Windows media features.");
                        }

                        // Media Foundation opens the reserved path using its own file handle.
                        stream.Dispose();
                        return new Mp3Destination(temp, final, logger);
                    }

                    var destination = new WaveDestination(stream, temp, final, logger);
                    destination.WriteHeader();
                    return destination;
                }
                catch
                {
                    stream.Dispose();
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

    /// <summary>Finalizes and publishes the recording or returns the surviving recovery path.</summary>
    internal RecordingResult Complete(EmptyRecordingPolicy emptyPolicy, RecordingProblem? problem)
    {
        if (!Enum.IsDefined(emptyPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(emptyPolicy));
        }

        ObjectDisposedException.ThrowIf(_closed, this);
        _closed = true;
        try
        {
            if (emptyPolicy == EmptyRecordingPolicy.Discard && Frames.Value == 0)
            {
                CloseFile();
                File.Delete(_temporary.Value);
                return new(null, 0, problem, false);
            }

            FinishFile();
            CloseFile();
            File.Move(_temporary.Value, _final.Value);
            return new(_final, Frames, problem, true);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // Dispose can itself fail while flushing a full or disconnected disk.
            LogFileFailure(_logger, ex, "Finalize", _temporary.Value, _final.Value, Frames.Value);
            try
            {
                CloseFile();
            }
            catch (Exception closeError) when (IsFileError(closeError))
            {
                LogFileFailure(_logger, closeError, "Close", _temporary.Value, _final.Value, Frames.Value);
            }

            return new(File.Exists(_temporary.Value) ? _temporary : null, Frames,
                (problem is null ? string.Empty : problem + " ") +
                "The recording could not be finalized or published.", false);
        }
    }

    [LoggerMessage(1, LogLevel.Error, "File operation {Operation} failed. Temporary file={TemporaryPath}, target={FinalPath}, accepted frames={Frames}.")]
    private static partial void LogFileFailure(ILogger logger, Exception exception, string operation,
        string temporaryPath, string finalPath, long frames);

    protected abstract void WriteBlock(Pcm16Block block);

    protected abstract void FinishFile();

    protected abstract void CloseFile();

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException or COMException;
}
