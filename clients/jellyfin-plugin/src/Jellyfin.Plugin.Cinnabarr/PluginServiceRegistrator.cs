using System;
using Jellyfin.Plugin.Cinnabarr.Client;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Cinnabarr
{
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHttpClient(CinnabarrClient.HttpClientName, client =>
            {
                // Long enough for an interactive search across indexers.
                client.Timeout = TimeSpan.FromSeconds(100);
            });

            serviceCollection.AddSingleton<ICinnabarrClient, CinnabarrClient>();
        }
    }
}
