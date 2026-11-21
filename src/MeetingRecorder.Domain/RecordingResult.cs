namespace MeetingRecorder.Domain;

/// <summary>Outcome of WAV publication.</summary>
public sealed record RecordingResult
{
    /// <summary>Creates a validated outcome of WAV publication.</summary>
    /// <param name="path">Location of the published WAV or a retained partial file.</param>
    /// <param name="frames">Number of complete mono frames written.</param>
    /// <param name="problem">Interruption or storage error to show to the user.</param>
    /// <param name="published">Whether the final WAV name was published.</param>
    public RecordingResult(RecordingFile? path, FrameCount frames, RecordingProblem? problem, bool published)
    {
        if (published)
        {
            ArgumentNullException.ThrowIfNull(path);
        }

        if (!published && (path is not null || frames.Value != 0))
        {
            ArgumentNullException.ThrowIfNull(problem);
        }

        Path = path;
        Frames = frames;
        Problem = problem;
        Published = published;
        PublishedFiles = published ? [path!] : [];
    }

    /// <summary>Location of the published WAV or a retained partial file.</summary>
    public RecordingFile? Path { get; }

    /// <summary>Number of complete mono frames written.</summary>
    public FrameCount Frames { get; }

    /// <summary>Interruption or storage error to show to the user.</summary>
    public RecordingProblem? Problem { get; private init; }

    /// <summary>Whether the file was published without any interruption.</summary>
    public bool IsSuccessful => Published && Problem is null;

    /// <summary>Whether the final WAV name was published.</summary>
    public bool Published { get; }

    /// <summary>All independently playable WAV files, in recording order.</summary>
    public IReadOnlyList<RecordingFile> PublishedFiles { get; private init; }

    /// <summary>Compares the session outcome and ordered file links by value.</summary>
    public bool Equals(RecordingResult? other) => other is not null && Path == other.Path
        && Frames == other.Frames && Problem == other.Problem && Published == other.Published
        && PublishedFiles.SequenceEqual(other.PublishedFiles);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Path);
        hash.Add(Frames);
        hash.Add(Problem);
        hash.Add(Published);
        foreach (var file in PublishedFiles)
        {
            hash.Add(file);
        }

        return hash.ToHashCode();
    }

    /// <summary>Combines the committed frames, playable files and any recovery path of successive parts.</summary>
    public static RecordingResult CombineParts(IReadOnlyList<RecordingResult> parts, RecordingProblem? problem)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var frames = 0L;
        var files = new List<RecordingFile>();
        RecordingResult? lastFile = null;
        RecordingProblem? failure = null;
        foreach (var part in parts)
        {
            ArgumentNullException.ThrowIfNull(part);
            frames = checked(frames + part.Frames.Value);
            files.AddRange(part.PublishedFiles);
            if (part.Path is not null)
            {
                lastFile = part;
            }

            failure ??= part.Problem;
        }

        var result = new RecordingResult(lastFile?.Path, frames, failure ?? problem, lastFile?.Published ?? false)
        {
            PublishedFiles = files.AsReadOnly()
        };
        return problem is null ? result : result.WithCaptureProblem(problem);
    }

    /// <summary>Includes an earlier capture failure while preserving the file and its publication outcome.</summary>
    public RecordingResult WithCaptureProblem(RecordingProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        if (Problem?.Message.StartsWith(problem.Message, StringComparison.Ordinal) == true)
        {
            return this;
        }

        var detail = Problem is null ? problem : new RecordingProblem(problem.Message + " " + Problem.Message);
        return this with { Problem = detail };
    }
}
