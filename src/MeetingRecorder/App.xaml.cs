using System.Globalization;
using System.Windows;

using MeetingRecorder.Application.Abstractions;
using MeetingRecorder.Application.DependencyInjection;
using MeetingRecorder.DependencyInjection;
using MeetingRecorder.Infrastructure.DependencyInjection;
using MeetingRecorder.Views;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeetingRecorder;

/// <summary>Starts SoundSeal and owns the host until recording cleanup completes.</summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        _ = builder.Services.AddMeetingRecorderApplication()
            .AddMeetingRecorderInfrastructure(builder.Configuration)
            .AddMeetingRecorderPresentation();
        _host = builder.Build();
        await _host.StartAsync();
        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Services.GetRequiredService<IRecorder>().FinishAsync().GetAwaiter().GetResult();
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
