using System.IO;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Formatting.Json;

namespace MeetingRecorder.DependencyInjection;

internal static class ErrorLogging
{
    internal static void AddRecorderErrorLog(this IHostApplicationBuilder builder, string directory)
    {
        _ = builder.Logging.ClearProviders();
        _ = builder.Services.AddSerilog((_, configuration) => configuration
            .MinimumLevel.Error()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ApplicationVersion", typeof(App).Assembly.GetName().Version?.ToString(3))
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.File(new JsonFormatter(renderMessage: true), Path.Combine(directory, "errors-.jsonl"),
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true, retainedFileCountLimit: 14, shared: true), preserveStaticLogger: true);
    }
}
