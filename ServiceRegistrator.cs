using Jellyfin.Plugin.SpotifyLocalSync.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SpotifyLocalSync;

/// <summary>
/// Registers plugin services with the Jellyfin DI container.
/// Jellyfin discovers this class automatically via <see cref="IPluginServiceRegistrator"/>.
/// Requires a parameterless constructor – no constructor needed here.
/// </summary>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Named HTTP client used by SpotifyApiClient
        serviceCollection.AddHttpClient("SpotifyLocalSync");

        // Scheduled task – discovered automatically by Jellyfin's task manager
        serviceCollection.AddSingleton<IScheduledTask, ScheduledSyncTask>();
    }
}
