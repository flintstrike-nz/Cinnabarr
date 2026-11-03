using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Download.Engine
{
    // Link intent stored next to the engine's own state (roadmap §4.4 rule 5), one file per
    // torrent at <AppData>/engine/embarr/<info-hash>.json. It holds external ids only, never
    // database ids, so a database restored from an older backup can rebuild the link.
    public class EngineLink
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public string MediaType { get; set; }
        public int TvdbId { get; set; }
        public List<EngineLinkEpisode> Episodes { get; set; } = new();
        public string SourceTitle { get; set; }
        public string Indexer { get; set; }
        public DateTime Grabbed { get; set; }
    }

    public class EngineLinkEpisode
    {
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
    }
}
