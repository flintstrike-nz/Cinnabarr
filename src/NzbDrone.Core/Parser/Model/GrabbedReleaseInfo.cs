using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.History;

namespace NzbDrone.Core.Parser.Model
{
    public class GrabbedReleaseInfo
    {
        public string Title { get; set; }
        public string Indexer { get; set; }
        public long Size { get; set; }
        public IndexerFlags IndexerFlags { get; set; }
        public ReleaseType ReleaseType { get; set; }

        public List<int> EpisodeIds { get; set; }
        public List<int> MovieIds { get; set; }

        public GrabbedReleaseInfo(List<MovieHistory> grabbedHistories)
        {
            var grabbedHistory = grabbedHistories.MaxBy(h => h.Date);

            grabbedHistory.Data.TryGetValue("indexer", out var indexer);
            grabbedHistory.Data.TryGetValue("size", out var sizeString);
            Enum.TryParse(grabbedHistory.Data.GetValueOrDefault("indexerFlags"), out IndexerFlags indexerFlags);
            long.TryParse(sizeString, out var size);

            Title = grabbedHistory.SourceTitle;
            Indexer = indexer;
            Size = size;
            MovieIds = grabbedHistories.Select(h => h.MovieId).Distinct().ToList();
            IndexerFlags = indexerFlags;
        }

        public GrabbedReleaseInfo(List<EpisodeHistory> grabbedHistories)
        {
            var grabbedHistory = grabbedHistories.MaxBy(h => h.Date);
            var episodeIds = grabbedHistories.Select(h => h.EpisodeId).Distinct().ToList();

            grabbedHistory.Data.TryGetValue("indexer", out var indexer);
            grabbedHistory.Data.TryGetValue("size", out var sizeString);
            Enum.TryParse(grabbedHistory.Data.GetValueOrDefault("indexerFlags"), out IndexerFlags indexerFlags);
            Enum.TryParse(grabbedHistory.Data.GetValueOrDefault("releaseType"), out ReleaseType releaseType);
            long.TryParse(sizeString, out var size);

            Title = grabbedHistory.SourceTitle;
            Indexer = indexer;
            Size = size;
            EpisodeIds = episodeIds;
            IndexerFlags = indexerFlags;
            ReleaseType = releaseType;
        }
    }
}
