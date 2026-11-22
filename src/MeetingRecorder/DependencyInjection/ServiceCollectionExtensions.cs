using MeetingRecorder.Abstractions;
using MeetingRecorder.Platform;
using MeetingRecorder.Storage;
using MeetingRecorder.Threading;
using MeetingRecorder.ViewModels;
using MeetingRecorder.Views;

using Microsoft.Extensions.DependencyInjection;

namespace MeetingRecorder.DependencyInjection;

/// <summary>Registers the lifetime window, display timer and recorder view model.</summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>Adds the WPF presentation components to the host.</summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The collection for further registration.</returns>
    internal static IServiceCollection AddMeetingRecorderPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton<IUiTimer, UiTimer>()
            .AddSingleton(_ => new RecordingLinkClipboard(System.Windows.Clipboard.SetText))
            .AddSingleton<IFolderLauncher, FolderLauncher>()
            .AddSingleton<IFolderCatalog, FolderCatalog>()
            .AddSingleton<IRecordingStore, WaveRecordingStore>()
            .AddSingleton<FolderBrowserViewModel>()
            .AddSingleton<RecorderViewModel>()
            .AddSingleton(provider => new MainWindow(provider.GetRequiredService<RecorderViewModel>(),
                provider.GetRequiredService<FolderBrowserViewModel>(), provider.GetRequiredService<ISoundCheck>()));
    }
}
