using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Cinnabarr.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Where Jellyfin reaches Cinnabarr, for example http://localhost:8989. Keep it on the home network.
        public string CinnabarrUrl { get; set; } = string.Empty;

        // Cinnabarr's API key (Settings → General). Never sent to the apps.
        public string ApiKey { get; set; } = string.Empty;

        // What each non-administrator may do. Administrators can do everything; users not listed can do nothing.
        // A List, not a Dictionary, because Jellyfin stores the configuration with XmlSerializer.
        public List<UserAccess> Users { get; set; } = new List<UserAccess>();
    }

    public class UserAccess
    {
        public Guid UserId { get; set; }

        // Browse, search and request.
        public bool Requester { get; set; }

        // Downloads, interactive search and deleting; includes everything a requester can do.
        public bool Manager { get; set; }

        // Play games.
        public bool Player { get; set; }
    }
}
