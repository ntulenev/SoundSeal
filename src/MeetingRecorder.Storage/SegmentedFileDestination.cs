using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

using Microsoft.Extensions.Logging;

namespace MeetingRecorder.Storage;

/// <summary>Seals each full part on the mixer worker while native capture continues feeding its queue.</summary>
internal sealed class SegmentedFileDestination : IRecordingDestination
{
    private readonly RecordingFolder _folder;
    private readonly string _stem;
    private readonly FrameCount _limit;
    private readonly RecordingFileFormat _format;
    private readonly ILogger? _logger;
    private readonly List<RecordingResult> _parts = [];
    private FileDestination? _current;
    private RecordingProblem? _failure;
    private bool _closed;

    /// <summary>Reserves the first part so folder and write errors are reported before device startup.</summary>
    internal SegmentedFileDestination(RecordingFolder folder, RecordingMode mode, DateTime start, FrameCount limit,
        RecordingFileFormat format = RecordingFileFormat.Wav, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentOutOfRangeException.ThrowIfZero(limit.Value);
        _folder = folder;
        _limit = limit;
        _format = format;
        _logger = logger;
        var session = Guid.NewGuid().ToString("N")[..10];
        _stem = FormattableString.Invariant($"{mode}-{start:yyyy-MM-dd_HH-mm-ss}-{session}");
        _current = OpenNextPart();
    }

    /// <inheritdoc/>
    public void Append(Pcm16Block block)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_failure is not null)
        {
            throw new RecordingException(_failure.Message);
        }

        var bytes = block.Bytes;
        try
        {
            while (!bytes.IsEmpty)
            {
                _current ??= OpenNextPart();
                var frames = (int)Math.Min(_limit.Value - _current.Frames.Value, bytes.Length / sizeof(short));
                var length = frames * sizeof(short);
                _current.Append(new Pcm16Block(bytes[..length]));
                bytes = bytes[length..];
                if (_current.Frames == _limit)
                {
                    var part = SealCurrent(EmptyRecordingPolicy.Keep);
                    if (part.Problem is { } problem)
                    {
                        throw new RecordingException(problem.Message);
                    }
                }
            }
        }
        catch (RecordingException ex)
        {
            // The mixer can retry a drain after an interruption. Never replay a partially written block.
            _failure = new RecordingProblem(ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task<RecordingResult> CompleteAsync(EmptyRecordingPolicy emptyPolicy, RecordingProblem? problem,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!Enum.IsDefined(emptyPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(emptyPolicy));
        }

        return Task.Run(() =>
        {
            _closed = true;
            if (_current is not null)
            {
                _ = SealCurrent(emptyPolicy);
            }

            var result = RecordingResult.CombineParts(_parts, _failure);
            return problem is null ? result : result.WithCaptureProblem(problem);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _closed = true;
        _current?.Dispose();
    }

    private FileDestination OpenNextPart() => FileDestination.Reserve(_folder,
        FormattableString.Invariant($"{_stem}-Part-{_parts.Count + 1:D4}"), _format, _logger);

    private RecordingResult SealCurrent(EmptyRecordingPolicy policy)
    {
        var part = _current!.Complete(policy, null);
        _parts.Add(part);
        _current = null;
        return part;
    }
}
