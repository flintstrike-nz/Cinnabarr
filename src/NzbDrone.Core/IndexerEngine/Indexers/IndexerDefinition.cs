using System;
using System.Collections.Generic;
using System.Text;
using NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.IndexerEngine.Indexers
{
    public class IndexerDefinition : ProviderDefinition
    {
        public string[] IndexerUrls { get; set; }
        public string[] LegacyUrls { get; set; }
        public string Description { get; set; }
        public Encoding Encoding { get; set; }
        public string Language { get; set; }
        public DownloadProtocol Protocol { get; set; }
        public IndexerPrivacy Privacy { get; set; }
        public bool SupportsRss { get; set; }
        public bool SupportsSearch { get; set; }
        public bool SupportsRedirect { get; set; }
        public bool SupportsPagination { get; set; }
        public IndexerCapabilities Capabilities { get; set; }
        public int Priority { get; set; } = 25;
        public bool Redirect { get; set; }
        public int DownloadClientId { get; set; }
        public DateTime Added { get; set; }
        // CINNABARR: Prowlarr's app-sync profile (AppProfileId, AppProfile) is not ported: Cinnabarr has no app sync.

        public List<SettingsField> ExtraFields { get; set; } = new();
    }
}
