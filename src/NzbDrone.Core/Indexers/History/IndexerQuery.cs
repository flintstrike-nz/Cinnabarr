using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers.History
{
    public enum IndexerQuerySource
    {
        Rss = 0,
        Search = 1
    }

    public class IndexerQuery : ModelBase
    {
        public int IndexerId { get; set; }
        public DateTime Date { get; set; }
        public IndexerQuerySource Source { get; set; }

        // A description of what was asked ("RSS sync", "[Series : S01E02]"); never the request address, which holds API keys.
        public string Query { get; set; }
        public bool Successful { get; set; }
        public int ResultCount { get; set; }
        public int ElapsedMs { get; set; }
        public string Error { get; set; }
    }

    public class IndexerQueryStats
    {
        public int IndexerId { get; set; }
        public int Queries { get; set; }
        public int Failures { get; set; }
        public int AverageElapsedMs { get; set; }
        public DateTime LastQuery { get; set; }
    }
}
