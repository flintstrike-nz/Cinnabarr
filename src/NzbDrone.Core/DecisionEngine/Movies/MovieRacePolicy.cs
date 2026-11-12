using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.DecisionEngine.Movies
{
    public interface IMovieRacePolicy
    {
        // Whether a release may download alongside the releases already downloading (or just grabbed) for the same
        // movie version: race mode is on (more than one source allowed), there is room, and every indexer involved
        // allows racing.
        bool MayJoin(RemoteMovie candidate, IReadOnlyCollection<RemoteMovie> others);

        // The same, against the releases queued for the candidate's movie version.
        bool MayRace(RemoteMovie candidate);
    }

    /// <summary>Roadmap §4.9 "Race mode": several releases may download the same movie version at once; the first copy to be imported wins.</summary>
    public class MovieRacePolicy : IMovieRacePolicy
    {
        private readonly IConfigService _configService;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IQueueService _queueService;

        public MovieRacePolicy(IConfigService configService, IIndexerFactory indexerFactory, IQueueService queueService)
        {
            _configService = configService;
            _indexerFactory = indexerFactory;
            _queueService = queueService;
        }

        public bool MayRace(RemoteMovie candidate)
        {
            if (_configService.EngineRaceMaxSources <= 1 || candidate.Movie == null)
            {
                return false;
            }

            var queued = _queueService.GetQueue()
                .Where(q => q.RemoteMovie?.Movie != null &&
                            q.RemoteMovie.Movie.Id == candidate.Movie.Id &&
                            q.RemoteMovie.Movie.VersionSlotId == candidate.Movie.VersionSlotId &&
                            q.TrackedDownloadState != TrackedDownloadState.FailedPending)
                .Select(q => q.RemoteMovie)
                .ToList();

            return MayJoin(candidate, queued);
        }

        public bool MayJoin(RemoteMovie candidate, IReadOnlyCollection<RemoteMovie> others)
        {
            var maxSources = Math.Min(_configService.EngineRaceMaxSources, ConfigService.MaxRaceSources);

            if (maxSources <= 1 || others.Count >= maxSources || others.Count == 0)
            {
                return false;
            }

            // The same release is never raced against itself.
            if (others.Any(o => o.Release.IndexerId == candidate.Release.IndexerId && o.Release.Guid == candidate.Release.Guid))
            {
                return false;
            }

            return AllowsRace(candidate) && others.All(AllowsRace);
        }

        private bool AllowsRace(RemoteMovie remote)
        {
            var indexerId = remote.Release?.IndexerId ?? 0;

            return indexerId > 0 && _indexerFactory.Exists(indexerId) && _indexerFactory.Get(indexerId).AllowRace;
        }
    }
}
