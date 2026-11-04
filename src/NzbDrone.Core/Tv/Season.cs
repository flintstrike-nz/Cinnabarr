using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tv
{
    public class Season : IEmbeddedDocument
    {
        public Season()
        {
            Images = new List<MediaCover.MediaCover>();
        }

        public int SeasonNumber { get; set; }
        public bool Monitored { get; set; }
        public string Title { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }

        // Overrides the series' seeding goal for this season's files (roadmap §4.10); null to use the series'.
        public Download.Engine.EngineSeedingRule SeedingRule { get; set; }
    }
}
