using System.IO;
using System.Windows;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Platform;
using MeetingRecorder.Storage;
using MeetingRecorder.TestSupport;
using MeetingRecorder.ViewModels;

using Moq;

namespace MeetingRecorder.Tests;

public sealed partial class RecorderViewModelTests
{
    /// <summary>The Windows drop payload preserves each absolute path and excludes a retained partial.</summary>
    [Trait("Category", "Integration")]
    [Fact]
    public Task DragShouldOfferOnePartOrTheEntireOrderedMeeting() => TestDeadline.Run(token => OnDispatcherAsync(ct =>
    {
        ct.ThrowIfCancellationRequested();
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var first = new RecordingFile(Path.Combine(folder.FullName, "Meeting #1 🎙 Part-0001.wav"));
            var second = new RecordingFile(Path.Combine(folder.FullName, "Meeting #1 🎙 Part-0002.wav"));
            var partial = new RecordingFile(Path.Combine(folder.FullName, "Part-0003.wav.partial"));
            File.WriteAllBytes(first.Value, []);
            File.WriteAllBytes(second.Value, []);
            File.WriteAllBytes(partial.Value, []);
            var recorder = ReadyRecorder();
            var timer = new TestTimer();
            using var model = new RecorderViewModel(recorder.Object, Mock.Of<IDeviceCatalog>(), timer,
                new FolderLauncher(), new RecordingLinkClipboard(Clipboard.SetText));
            Assert.Null(model.CreateDragData(null));

            void Show(RecorderState state, RecordingResult result)
            {
                recorder.SetupGet(r => r.Snapshot).Returns(new RecorderSnapshot(state, result: result));
                timer.Pulse();
            }

            var saved = new RecordingResult(first, 12, null, true);
            Show(RecorderState.Saved, saved);
            Assert.Equal([first.Value], model.CreateDragData(null)!.GetFileDropList().Cast<string>());
            var incomplete = new RecordingResult(partial, 8, "Disk full.", false);
            Show(RecorderState.Error, RecordingResult.CombineParts([saved, new(second, 10, null, true), incomplete], null));
            var group = model.CreateDragData(null)!;
            Assert.True(group.GetDataPresent(DataFormats.FileDrop, autoConvert: false));
            Assert.Equal([first.Value, second.Value], group.GetFileDropList().Cast<string>());
            Assert.Equal([second.Value], model.CreateDragData(second)!.GetFileDropList().Cast<string>());
            Assert.Null(model.CreateDragData(partial));

            File.Delete(second.Value);
            Assert.Null(model.CreateDragData(null));
            Assert.Contains(second.Name, model.FileActionStatus, StringComparison.Ordinal);
            Assert.NotNull(model.CreateDragData(first));
            Assert.Empty(model.FileActionStatus);
            Show(RecorderState.Error, incomplete);
            Assert.Null(model.CreateDragData(null));
        }
        finally
        {
            folder.Delete(true);
        }
        return Task.CompletedTask;
    }, token));
}
