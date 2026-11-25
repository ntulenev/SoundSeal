using System.IO;
using System.Text.Json;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Application.DependencyInjection;
using MeetingRecorder.DependencyInjection;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.DependencyInjection;
using MeetingRecorder.Storage;
using MeetingRecorder.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeetingRecorder.Tests;

/// <summary>Checks the recorder's real error journal without opening an audio device.</summary>
public sealed partial class ErrorLoggingTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory();

    /// <summary>Only errors reach the file, with the recording settings and original filesystem exception.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public Task FileJournalPreservesRecordingFailureAndFiltersLowerLevels() => TestDeadline.Run(async token =>
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddRecorderErrorLog(_folder.FullName);
        _ = builder.Services.AddMeetingRecorderApplication().AddMeetingRecorderInfrastructure(builder.Configuration)
            .AddSingleton<IRecordingStore, WaveRecordingStore>();
        var blocked = Path.Combine(_folder.FullName, "occupied");
        await File.WriteAllTextAsync(blocked, "existing file", token);
        using (var host = builder.Build())
        {
            await host.StartAsync(token);
            var logger = host.Services.GetRequiredService<ILogger<ErrorLoggingTests>>();
            foreach (var level in new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Critical })
            {
                LogProbe(logger, level);
            }

            var recorder = host.Services.GetRequiredService<IRecorder>();
            await recorder.StartAsync(new("mic", "out", blocked, RecordingMode.Meeting,
                RecordingLayout.QuarterHourParts, RecordingFileFormat.Mp3), token);
            Assert.Equal(RecorderState.Error, recorder.Snapshot.State);
            await host.StopAsync(token);
        }

        var lines = await File.ReadAllLinesAsync(Assert.Single(_folder.GetFiles("errors-*.jsonl")).FullName, token);
        Assert.Equal(2, lines.Length);
        using var critical = JsonDocument.Parse(lines[0]);
        Assert.Equal("Fatal", critical.RootElement.GetProperty("Level").GetString());
        using var error = JsonDocument.Parse(lines[1]);
        var entry = error.RootElement;
        Assert.Equal("Error", entry.GetProperty("Level").GetString());
        Assert.Contains("System.IO.IOException", entry.GetProperty("Exception").GetString(), StringComparison.Ordinal);
        var fields = entry.GetProperty("Properties");
        Assert.True(fields.GetProperty("RecordingId").GetGuid() != Guid.Empty);
        Assert.Equal("Starting", fields.GetProperty("State").GetString());
        Assert.Equal("Meeting", fields.GetProperty("Mode").GetString());
        Assert.Equal("Mp3", fields.GetProperty("FileFormat").GetString());
        Assert.Equal("QuarterHourParts", fields.GetProperty("Layout").GetString());
        Assert.Equal("mic", fields.GetProperty("MicrophoneId").GetString());
        Assert.Equal("out", fields.GetProperty("OutputId").GetString());
        Assert.Equal(blocked, fields.GetProperty("RecordingFolder").GetString());
    });

    /// <inheritdoc/>
    public void Dispose() => _folder.Delete(true);

    [LoggerMessage(EventId = 1, Message = "Filter probe.")]
    private static partial void LogProbe(ILogger logger, LogLevel level);
}
