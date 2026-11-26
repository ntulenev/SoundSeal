using System.Globalization;
using System.IO;
using System.Windows;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Application.DependencyInjection;
using MeetingRecorder.DependencyInjection;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.DependencyInjection;
using MeetingRecorder.Views;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeetingRecorder;

/// <summary>Starts SoundSeal and owns the host until recording cleanup completes.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001",
    Justification = "WPF owns the application and releases the token source in OnExit.")]
public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _lifetime = new();
    private IHost? _host;
    private ILogger<App>? _logger;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartHostAsync(_lifetime.Token);
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        _lifetime.Cancel();
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (_host is not null)
        {
            try
            {
                _host.Services.GetRequiredService<IRecorder>().FinishAsync(shutdown.Token).GetAwaiter().GetResult();
                _host.StopAsync(shutdown.Token).GetAwaiter().GetResult();
            }
            finally
            {
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                _host.Dispose();
            }
        }

        _lifetime.Dispose();
        base.OnExit(e);
    }

    private async void StartHostAsync(CancellationToken cancellationToken)
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        var logFolder = new RecordingFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MeetingRecorder", "Logs"));
        builder.AddRecorderErrorLog(logFolder.Value);
        _ = builder.Services.AddMeetingRecorderApplication()
            .AddMeetingRecorderInfrastructure(builder.Configuration)
            .AddMeetingRecorderPresentation(logFolder);
        _host = builder.Build();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        try
        {
            await _host.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (_logger is { } logger && e.ExceptionObject is Exception exception)
        {
            LogUnhandledFailure(logger, exception, e.IsTerminating);
        }
    }

    [LoggerMessage(1, LogLevel.Critical, "Unhandled application exception. Terminating={Terminating}.")]
    private static partial void LogUnhandledFailure(ILogger logger, Exception exception, bool terminating);
}
