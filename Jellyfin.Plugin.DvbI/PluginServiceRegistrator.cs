using Jellyfin.Plugin.DvbI.Http;
using Jellyfin.Plugin.DvbI.Listings;
using Jellyfin.Plugin.DvbI.LiveTv;
using Jellyfin.Plugin.DvbI.ServiceLists;
using Jellyfin.Plugin.DvbI.Streaming;
using Jellyfin.Plugin.DvbI.Tvheadend;
using MediaBrowser.Controller;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.DvbI;

/// <summary>
/// Registers the plugin services with Jellyfin's dependency injection.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<DvbIHttpClient>();
        serviceCollection.AddSingleton<ServiceListProvider>();
        serviceCollection.AddSingleton<DashManifestProxy>();
        serviceCollection.AddSingleton<TvhClient>();
        serviceCollection.AddSingleton<DvbITunerHost>();
        serviceCollection.AddSingleton<ITunerHost>(s => s.GetRequiredService<DvbITunerHost>());
        serviceCollection.AddSingleton<DvbIListingsProvider>();
        serviceCollection.AddSingleton<IListingsProvider>(s => s.GetRequiredService<DvbIListingsProvider>());
        serviceCollection.AddHostedService<TunerSyncService>();
    }
}
