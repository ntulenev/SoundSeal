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
    }

    /// <summary>Location of the published WAV or a retained partial file.</summary>
    public RecordingFile? Path { get; }

    /// <summary>Number of complete mono frames written.</summary>
    public FrameCount Frames { get; }

    /// <summary>Interruption or storage error to show to the user.</summary>
    public RecordingProblem? Problem { get; }

    /// <summary>Whether the file was published without any interruption.</summary>
    public bool IsSuccessful => Published && Problem is null;

    /// <summary>Whether the final WAV name was published.</summary>
    public bool Published { get; }

    /// <summary>Includes an earlier capture failure while preserving the file and its publication outcome.</summary>
    public RecordingResult WithCaptureProblem(RecordingProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        if (Problem?.Message.StartsWith(problem.Message, StringComparison.Ordinal) == true)
        {
            return this;
        }

        var detail = Problem is null ? problem : new RecordingProblem(problem.Message + " " + Problem.Message);
        return new(Path, Frames, detail, Published);
    }
}
