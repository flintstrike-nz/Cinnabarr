using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Movies.Specifications
{
    public class MovieAlreadyImportedSpecification : IMovieDecisionEngineSpecification
    {
        private readonly IMovieHistoryService _historyService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MovieAlreadyImportedSpecification(IMovieHistoryService historyService, IConfigService configService, Logger logger)
        {
            _historyService = historyService;
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, MovieReleaseDecisionInformation information)
        {
            if (!_configService.EnableCompletedDownloadHandling)
            {
                _logger.Debug("Skipping already imported check because CDH is disabled");
                return DownloadSpecDecision.Accept();
            }

            var movie = subject.Movie;

            if (movie is not { HasFile: true })
            {
                _logger.Debug("Skipping already imported check for movie without file");
                return DownloadSpecDecision.Accept();
            }

            // Newest first.
            var historyForMovie = _historyService.GetByMovieId(movie.Id);
            var lastGrabbed = historyForMovie.FirstOrDefault(h => h.EventType == MovieHistoryEventType.Grabbed);

            if (lastGrabbed == null)
            {
                return DownloadSpecDecision.Accept();
            }

            var imported = historyForMovie.FirstOrDefault(h => h.EventType == MovieHistoryEventType.DownloadFolderImported && h.DownloadId == lastGrabbed.DownloadId);

            if (imported == null)
            {
                return DownloadSpecDecision.Accept();
            }

            // This is really only a guard against redownloading the same release over and over when the grabbed and
            // imported qualities do not match; if they do match skip this check.
            if (lastGrabbed.Quality.Equals(imported.Quality))
            {
                return DownloadSpecDecision.Accept();
            }

            var release = subject.Release;

            if (release.DownloadProtocol == DownloadProtocol.Torrent && release is TorrentInfo { InfoHash: not null } torrentInfo &&
                torrentInfo.InfoHash.Equals(lastGrabbed.DownloadId, StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.Debug("Has same torrent hash as a grabbed and imported release");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.AlreadyImportedSameHash, "Has same torrent hash as a grabbed and imported release");
            }

            // Only based on title because a release with the same title on another indexer/released at a different time
            // very likely has the exact same content and we don't need to also try it.
            if (release.Title.Equals(lastGrabbed.SourceTitle, StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.Debug("Has same release name as a grabbed and imported release");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.AlreadyImportedSameName, "Has same release name as a grabbed and imported release");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
