using System.Collections.Generic;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch
{
    public class EpisodeSearchGroup
    {
        public int SeriesId { get; set; }
        public int SeasonNumber { get; set; }

        // The extra version of the series searched for; 0 for the series' default version. The episodes are views of it.
        public int VersionSlotId { get; set; }
        public List<Episode> Episodes { get; set; }
    }
}
