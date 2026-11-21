using MeetingRecorder.Abstractions;
using MeetingRecorder.Infrastructure.Audio;
using MeetingRecorder.Infrastructure.Wasapi;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingRecorder.Infrastructure.DependencyInjection;

/// <summary>Registers the WASAPI adapters and the factory for per-recording resources.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds WASAPI capture and device discovery to the host.</summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configuration">The configuration supplied by the application host.</param>
    /// <returns>The collection for further registration.</returns>
    public static IServiceCollection AddMeetingRecorderInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddSingleton(AudioCaptureOptions.Read(configuration))
            .AddSingleton<IRecordingClock, PerformanceClock>()
            .AddSingleton<IDeviceCatalog, EndpointCatalog>()
            .AddSingleton<ICaptureFactory, CaptureFactory>();
    }
}
