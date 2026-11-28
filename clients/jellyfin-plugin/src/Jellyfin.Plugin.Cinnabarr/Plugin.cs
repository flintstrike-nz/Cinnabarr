using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Cinnabarr.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Cinnabarr
{
    /// <summary>
    /// Cinnabarr inside Jellyfin: browse and request shows and movies, manage downloads and play games from the
    /// Jellyfin apps. The Cinnabarr URL and API key stay on the Jellyfin server; the apps only ever call the
    /// /Cinnabarr endpoints of this plugin. See docs/cinnabarr/JELLYFIN.md in the Cinnabarr repository.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static readonly Guid PluginId = new Guid("04afd9e8-511a-4cc3-a5d7-efd4e175eddf");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin? Instance { get; private set; }

        public override string Name => "Cinnabarr";

        public override Guid Id => PluginId;

        public override string Description => "Browse and request shows and movies, manage downloads and play games from Cinnabarr.";

        public IEnumerable<PluginPageInfo> GetPages()
        {
            var ns = GetType().Namespace;

            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = ns + ".Configuration.configPage.html"
                },
                new PluginPageInfo
                {
                    // The TV checklist (docs/cinnabarr/JELLYFIN.md section 6). It is a dashboard page so it can be
                    // opened from inside the Jellyfin app on the TV, which is where the answers matter.
                    Name = "CinnabarrTvCheck",
                    DisplayName = "Cinnabarr TV check",
                    EmbeddedResourcePath = ns + ".Web.tvcheck.html",
                    EnableInMainMenu = true,
                    MenuIcon = "tv"
                }
            };
        }
    }
}
