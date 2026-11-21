namespace MeetingRecorder.Domain.Tests;

/// <summary>Tests recording value invariants.</summary>
[Trait("Category", "Unit")]
public sealed class RecordingValuesTests
{
    /// <summary>Endpoint identity and interruption details reject missing text.</summary>
    [Theory(DisplayName = "Endpoint identity and interruption details reject missing text")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingTextCannotEnterARecording(string? text)
    {
        Assert.ThrowsAny<ArgumentException>(() => new EndpointId(text!));
        Assert.ThrowsAny<ArgumentException>(() => new EndpointName(text!));
        Assert.ThrowsAny<ArgumentException>(() => new RecordingProblem(text!));
        Assert.ThrowsAny<ArgumentException>(() => new RecordingException(text!));
    }

    /// <summary>Native values survive conversion without changing device IDs or error messages.</summary>
    [Fact(DisplayName = "Native values survive conversion without changing device IDs or error messages")]
    public void TextValuesPreserveTheirContents()
    {
        Assert.Equal("  mic-2", EndpointId.FromString("  mic-2").ToString());
        Assert.Equal("USB Headset", EndpointName.FromString("USB Headset").ToString());
        Assert.Equal("Disk full.", RecordingProblem.FromString("Disk full.")!.ToString());
        Assert.Null(RecordingProblem.FromString(null));
        Assert.Null(RecordingFile.FromString(null));
        Assert.NotEmpty(new RecordingException().Message);
        Assert.Throws<ArgumentNullException>(() => new RecordingException("Failure", null!));
    }

    /// <summary>Peak meters accept only finite normalized samples.</summary>
    [Theory(DisplayName = "Peak meters accept only finite normalized samples")]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    public void PeaksRejectInvalidValues(float value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignalPeak(value));

    /// <summary>Timeline values include zero and reject times outside the representable recording.</summary>
    [Fact(DisplayName = "Timeline values include zero and reject times outside the representable recording")]
    public void ClockFramesAndDurationsHaveValidBoundaries()
    {
        Assert.Equal(new Levels(0, 1).Computer, SignalPeak.FromSingle(1));
        Assert.Equal(default, CaptureTimestamp.FromInt64(0));
        Assert.Equal(default, RecordingDuration.FromTimeSpan(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureTimestamp(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingDuration(TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameCount(-1));
        var last = long.MaxValue / RecordingFormat.TicksPerFrame;
        Assert.Equal(last * RecordingFormat.TicksPerFrame, RecordingFormat.Duration(last).Value.Ticks);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameCount(last + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActiveInterval(10, 9));
        var running = new RecorderSnapshot(RecorderState.Recording, TimeSpan.MaxValue, 0);
        Assert.Throws<OverflowException>(() => running.Elapsed(1));
    }

    /// <summary>Only whole PCM16 samples reach the recording sink.</summary>
    [Fact(DisplayName = "Only whole PCM16 samples reach the recording sink")]
    public void PcmBlocksPreserveWholeFrames()
    {
        Assert.Equal(0L, default(Pcm16Block).Frames.Value);
        var block = new Pcm16Block([1, 2, 3, 4]);
        Assert.Equal(2L, block.Frames.Value);
        Assert.True(block.Bytes.SequenceEqual(new byte[] { 1, 2, 3, 4 }));
        Assert.Throws<ArgumentException>(() => { _ = new Pcm16Block([1]); });
    }

    /// <summary>Recording file and folder paths are fully qualified and usable without filesystem access.</summary>
    [Fact(DisplayName = "Recording file and folder paths are fully qualified and usable without filesystem access")]
    public void PathsDistinguishFilesFromFolders()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var path = Path.Combine(root, "not created", "Meeting.wav");
        Assert.Equal(path, RecordingFile.FromString(path)!.ToString());
        Assert.Equal(root, RecordingFolder.FromString(root).ToString());
        Assert.True(RecordingFolder.TryParse(root, out var folder));
        Assert.Equal(root, folder!.Value);
        Assert.False(RecordingFolder.TryParse(" ", out _));
        Assert.False(RecordingFolder.TryParse("relative", out _));
        Assert.False(RecordingFolder.TryParse(path + '\0', out _));
        Assert.Throws<ArgumentException>(() => new RecordingFile(root));
        Assert.Throws<ArgumentException>(() => new RecordingFile(Path.Combine(root, "..")));
        Assert.Throws<ArgumentException>(() => new RecordingFile("Meeting.wav"));
        Assert.ThrowsAny<ArgumentException>(() => new RecordingFolder(null!));
    }

    /// <summary>A device or directory snapshot owns its entries and rejects missing members.</summary>
    [Fact(DisplayName = "A device or directory snapshot owns its entries and rejects missing members")]
    public void CollectionsCannotChangeAfterConstruction()
    {
        var mic = new Endpoint("mic", "Microphone", true);
        var original = new List<Endpoint> { mic };
        var devices = new Devices(original, []);
        original.Clear();
        Assert.Same(mic, Assert.Single(devices.Microphones));
        Assert.Empty(devices.Outputs);
        Assert.Throws<ArgumentNullException>(() => new Devices(null!, []));
        Assert.Throws<ArgumentException>(() => new Devices([], [null!]));
        Assert.Throws<ArgumentNullException>(() => new Endpoint(null!, "Microphone", true));
        Assert.Throws<ArgumentNullException>(() => new Endpoint("mic", null!, true));
        var names = new List<FolderEntryName> { "January" };
        var listing = new FolderListing(Path.GetTempPath(), names);
        names.Clear();
        Assert.Equal("January", Assert.Single(listing.Names).ToString());
        Assert.Equal(Path.GetTempPath(), listing.Directory!.Value);
        var drive = new FolderEntryName(Path.GetPathRoot(Path.GetTempPath())!);
        Assert.Null(new FolderListing(null, [drive]).Directory);
        Assert.Throws<ArgumentException>(() => new FolderListing(Path.GetTempPath(), [drive]));
        Assert.Throws<ArgumentException>(() => new FolderListing(null, ["January"]));
        Assert.Throws<ArgumentException>(() => new Devices([mic, mic], []));
        Assert.Throws<ArgumentException>(() => new Devices([mic, new Endpoint("other", "USB", true)], []));
        Assert.Equal(2, new Devices([mic, new Endpoint("other", "USB", false)], []).Microphones.Count);
        Assert.Throws<ArgumentException>(() => new FolderListing(null, [" "]));
        Assert.Throws<ArgumentException>(() => new FolderEntryName(".."));
        Assert.Throws<ArgumentException>(() => new FolderEntryName(Path.Combine("nested", "child")));
        Assert.Equal(Path.GetPathRoot(Path.GetTempPath()), new FolderEntryName(Path.GetPathRoot(Path.GetTempPath())!).Value);
    }

    /// <summary>Settings require both devices and a destination.</summary>
    [Fact(DisplayName = "Settings require both devices and a destination")]
    public void RecordingSelectionsRequireAllThreeValues()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordingSettings(null!, "out", Path.GetTempPath()));
        Assert.Throws<ArgumentNullException>(() => new RecordingSettings("mic", null!, Path.GetTempPath()));
        Assert.Throws<ArgumentNullException>(() => new RecordingSettings("mic", "out", null!));
    }

    /// <summary>Each capture mode accepts exactly its required selection.</summary>
    [Fact(DisplayName = "Single-source settings accept a missing inactive device")]
    public void RequiredSourcesFollowTheMode()
    {
        var folder = Path.GetTempPath();
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingSettings("mic", "out", folder, RecordingMode.Meeting, (RecordingLayout)11));
        Assert.Null(new RecordingSettings(null, "out", folder, RecordingMode.Audio).MicrophoneId);
        Assert.Null(new RecordingSettings("mic", null, folder, RecordingMode.Dictaphone).OutputId);
        Assert.Throws<ArgumentNullException>(() => new RecordingSettings(null, null, folder, RecordingMode.Audio));
        Assert.Throws<ArgumentNullException>(() => new RecordingSettings(null, null, folder, RecordingMode.Dictaphone));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingSettings("mic", "out", folder, (RecordingMode)10));
    }

    /// <summary>Session results retain value equality for both single and multiple published files.</summary>
    [Fact(DisplayName = "Recording result equality includes the complete ordered file list")]
    public void EquivalentPartListsHaveEqualResults()
    {
        static RecordingResult Part(string name)
        {
            return new(Path.Combine(Path.GetTempPath(), name), 1, null, true);
        }

        var result = RecordingResult.CombineParts([Part("first.wav"), Part("last.wav")], null);
        var same = RecordingResult.CombineParts([Part("first.wav"), Part("last.wav")], null);
        Assert.Equal(result, same);
        Assert.Equal(result.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(result, RecordingResult.CombineParts([Part("other.wav"), Part("last.wav")], null));
        Assert.NotEqual(result, Part("other.wav"));
        Assert.NotEqual(result, Part("last.wav"));
        Assert.NotEqual(result, result.WithCaptureProblem(new RecordingProblem("interrupted")));
        Assert.False(result.Equals(null));
        Assert.NotEqual(Part("last.wav").WithCaptureProblem(new RecordingProblem("failure")),
            new RecordingResult(Part("last.wav").Path, 1, "failure", false));
        Assert.Throws<ArgumentNullException>(() => RecordingResult.CombineParts(null!, null));
        Assert.Throws<ArgumentNullException>(() => RecordingResult.CombineParts([null!], null));
    }

    /// <summary>Only terminal snapshots contain results and only a running interval has a start time.</summary>
    [Fact(DisplayName = "Only terminal snapshots contain results and only a running interval has a start time")]
    public void SnapshotRequiresConsistentStateAndOutcome()
    {
        var saved = new RecordingResult(Path.Combine(Path.GetTempPath(), "ok.wav"), 10, null, true);
        var failed = new RecordingResult(null, 0, "Cannot start.", false);
        Assert.Throws<ArgumentNullException>(() => new RecordingResult(null, 1, null, true));
        Assert.Throws<ArgumentNullException>(() => new RecordingResult(saved.Path, 1, null, false));
        Assert.Throws<ArgumentNullException>(() => new RecordingResult(null, 1, null, false));
        Assert.Same(failed, failed.WithCaptureProblem(new RecordingProblem("Cannot start.")));
        Assert.Equal("Disk full. Cannot start.", failed.WithCaptureProblem(new RecordingProblem("Disk full.")).Problem!.Message);
        Assert.Equal("Device lost.", saved.WithCaptureProblem(new RecordingProblem("Device lost.")).Problem!.Message);
        Assert.Throws<ArgumentNullException>(() => saved.WithCaptureProblem(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecorderSnapshot((RecorderState)99));
        Assert.Throws<ArgumentException>(() => new RecorderSnapshot(RecorderState.Recording));
        Assert.Throws<ArgumentException>(() => new RecorderSnapshot(RecorderState.Paused, started: 10));
        Assert.Throws<ArgumentException>(() => new RecorderSnapshot(RecorderState.Ready, result: failed));
        Assert.Throws<ArgumentException>(() => new RecorderSnapshot(RecorderState.Saved));
        Assert.Throws<ArgumentException>(() => new RecorderSnapshot(RecorderState.Saved, result: failed));
        Assert.Throws<ArgumentException>(() => new RecorderSnapshot(RecorderState.Error, result: saved));
        Assert.Throws<ArgumentException>(() => new RecorderSnapshot(RecorderState.Error, result: new(null, 0, null, false)));
        Assert.Throws<ArgumentException>(() => new RecorderSnapshot(RecorderState.Saved, TimeSpan.Zero, result: saved));
        Assert.Throws<ArgumentException>(() => new RecorderSnapshot(RecorderState.Ready, TimeSpan.FromSeconds(1)));
        Assert.Equal(RecordingFormat.Duration(10), new RecorderSnapshot(RecorderState.Saved, result: saved).ActiveTime);
        Assert.Same(failed, new RecorderSnapshot(RecorderState.Error, result: failed).Result);
    }
}
