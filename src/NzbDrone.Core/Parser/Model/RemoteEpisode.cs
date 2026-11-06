using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Parser.Model
{
    public class RemoteEpisode : RemoteMedia
    {
        public ParsedEpisodeInfo ParsedEpisodeInfo { get; set; }
        public SceneMapping SceneMapping { get; set; }
        public int? MappedSeasonNumber { get; set; }
        public Series Series { get; set; }
        public List<Episode> Episodes { get; set; }
        public bool EpisodeRequested { get; set; }
        public SeriesMatchType SeriesMatchType { get; set; }

        public RemoteEpisode()
        {
            Episodes = new List<Episode>();
        }

        public override HashSet<int> Tags => Series?.Tags ?? new HashSet<int>();

        public override bool IsRecent => IsRecentEpisode();

        public bool IsRecentEpisode()
        {
            return Episodes.Any(e => e.AirDateUtc >= DateTime.UtcNow.Date.AddDays(-14));
        }
    }

    public enum ReleaseSourceType
    {
        Unknown = 0,
        Rss = 1,
        Search = 2,
        UserInvokedSearch = 3,
        InteractiveSearch = 4,
        ReleasePush = 5
    }
}
