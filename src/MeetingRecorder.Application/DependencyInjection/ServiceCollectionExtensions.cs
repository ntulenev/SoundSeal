using MeetingRecorder.Application.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace MeetingRecorder.Application.DependencyInjection;

/// <summary>Registers application-lifetime recording orchestration.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds the shared coordinator to the host.</summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The collection for further registration.</returns>
    public static IServiceCollection AddMeetingRecorderApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton<IRecorder, RecordingCoordinator>();
    }
}
