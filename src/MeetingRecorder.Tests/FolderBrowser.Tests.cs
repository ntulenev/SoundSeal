using System.IO;

using FluentAssertions;

using MeetingRecorder.Domain;
using MeetingRecorder.Platform;
using MeetingRecorder.ViewModels;

namespace MeetingRecorder.Tests;

public sealed class FolderBrowserTests
{
    [Trait("Category", "Integration")]
    [Fact(DisplayName = "Folder browsing starts at the existing ancestor and navigates real Unicode paths")]
    public async Task FolderBrowserShouldNavigateAndSelect()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var child = folder.CreateSubdirectory("January – planning");
            var model = new FolderBrowserViewModel(new FolderCatalog());
            await model.OpenAsync(Path.Combine(folder.FullName, "missing", "nested"));
            model.PathText.Should().Be(folder.FullName);
            model.Entries.Should().Equal(child.Name);

            model.SelectedName = child.Name;
            model.Choose().Should().Be(child.FullName);
            await model.EnterAsync();
            model.PathText.Should().Be(child.FullName);
            model.SelectedName.Should().BeNull();
            await model.UpAsync();
            model.PathText.Should().Be(folder.FullName);

            model.PathText = Path.Combine(folder.FullName, "new recording folder");
            await model.GoAsync();
            model.Notice.Should().Contain("could not be opened");
            model.Choose().Should().Be(model.PathText);
            model.PathText = "relative";
            model.Choose().Should().BeNull();
            model.Notice.Should().Contain("full folder path");

            await model.OpenAsync(string.Empty);
            model.Entries.Should().Contain(Path.GetPathRoot(folder.FullName)!);
            model.SelectedName = Path.GetPathRoot(folder.FullName);
            model.Choose().Should().Be(model.SelectedName);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Trait("Category", "Unit")]
    [Theory(DisplayName = "Out-of-order folder reads cannot overwrite later navigation or edits")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SupersededReadShouldBeIgnored(bool fail, bool edit)
    {
        var slow = new TaskCompletionSource<FolderListing>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fast = Task.FromResult(new FolderListing(@"C:\new", ["folder"]));
        var model = new FolderBrowserViewModel(new QueuedCatalog([slow.Task, fast]));
        var first = model.OpenAsync(@"C:\old");
        if (edit)
        {
            model.PathText = @"C:\typed";
        }
        else
        {
            await model.OpenAsync(@"C:\new");
        }

        if (fail)
        {
            slow.SetException(new RecordingException("Old directory is unavailable."));
        }
        else
        {
            slow.SetResult(new(@"C:\old", ["stale entry"]));
        }

        await first;
        model.PathText.Should().Be(edit ? @"C:\typed" : @"C:\new");
        model.Entries.Should().NotContain("stale entry");
        model.Notice.Should().BeEmpty();
    }

    private sealed class QueuedCatalog : IFolderCatalog
    {
        private readonly Queue<Task<FolderListing>> _results;

        internal QueuedCatalog(IEnumerable<Task<FolderListing>> results)
        {
            _results = new(results);
        }

        public Task<FolderListing> ListAsync(string? path, bool nearestExisting) =>
            _results.Dequeue();
    }
}
