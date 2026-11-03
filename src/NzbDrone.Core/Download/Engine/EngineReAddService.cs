using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Engine
{
    public class EngineReAddException : Exception
    {
        public EngineReAddException(string message)
            : base(message)
        {
        }
    }

    public interface IEngineReAddService
    {
        // Sends a grab that is missing from the engine back to it, using the release Embarr stored at grab
        // time (roadmap §4.4 rule 3). Files already at the target are re-checked by the engine, not replaced.
        Task ReAdd(string downloadId);
    }

    public class EngineReAddService : IEngineReAddService
    {
        private readonly IHistoryService _historyService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IDownloadService _downloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IIgnoredDownloadService _ignoredDownloadService;
        private readonly Logger _logger;

        public EngineReAddService(IHistoryService historyService,
                                  IDownloadHistoryService downloadHistoryService,
                                  IDownloadService downloadService,
                                  ITrackedDownloadService trackedDownloadService,
                                  IIgnoredDownloadService ignoredDownloadService,
                                  Logger logger)
        {
            _historyService = historyService;
            _downloadHistoryService = downloadHistoryService;
            _downloadService = downloadService;
            _trackedDownloadService = trackedDownloadService;
            _ignoredDownloadService = ignoredDownloadService;
            _logger = logger;
        }

        public async Task ReAdd(string downloadId)
        {
            var trackedDownload = _trackedDownloadService.Find(downloadId);

            if (trackedDownload is not { IsMissingFromEngine: true })
            {
                throw new EngineReAddException("Only downloads that are missing from the engine can be re-added.");
            }

            var title = trackedDownload.DownloadItem.Title;
            var remoteEpisode = trackedDownload.RemoteEpisode;

            if (remoteEpisode?.Series == null || remoteEpisode.Episodes.Empty() || trackedDownload.HasIncompleteLink)
            {
                throw new EngineReAddException($"The episodes '{title}' was grabbed for are no longer all in the library, so it can't be re-added. Remove it from the queue instead.");
            }

            var grab = _downloadHistoryService.GetLatestGrab(downloadId);
            var release = grab?.Release;

            if (release == null || release.DownloadUrl.IsNullOrWhiteSpace())
            {
                throw new EngineReAddException($"The release link for '{title}' wasn't stored, so it can't be re-added. Search for it again instead.");
            }

            var indexerFlags = GetGrabbedIndexerFlags(downloadId);

            var torrent = new TorrentInfo
            {
                Guid = release.Guid,
                Title = release.Title.IsNullOrWhiteSpace() ? grab.SourceTitle : release.Title,
                Size = release.Size,
                DownloadUrl = release.DownloadUrl,
                MagnetUrl = release.DownloadUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ? release.DownloadUrl : null,
                InfoUrl = release.InfoUrl,
                CommentUrl = release.CommentUrl,
                IndexerId = release.IndexerId,
                Indexer = release.Indexer,
                IndexerFlags = indexerFlags,
                PublishDate = release.PublishDate,
                Languages = release.Languages,
                DownloadProtocol = DownloadProtocol.Torrent

                // InfoHash is left empty on purpose: the client then takes the hash from the torrent it actually
                // fetches, checks it against the hash blocklist and records it, even if the indexer now serves a
                // different torrent.
            };

            var reAdd = new RemoteEpisode
            {
                Series = remoteEpisode.Series,
                Episodes = remoteEpisode.Episodes,
                ParsedEpisodeInfo = remoteEpisode.ParsedEpisodeInfo,
                Languages = remoteEpisode.Languages,
                CustomFormats = remoteEpisode.CustomFormats,
                CustomFormatScore = remoteEpisode.CustomFormatScore,
                Release = torrent
            };

            _logger.Info("Re-adding '{0}' to the engine", title);

            try
            {
                await _downloadService.DownloadReport(reAdd, trackedDownload.DownloadClient);
            }
            catch (Exception ex)
            {
                // Any failure (indexer deleted, link expired, invalid torrent, blocklisted hash, engine down)
                // leaves the Missing row as it was, so it can be retried or dismissed.
                _logger.Warn(ex, "Unable to re-add '{0}' to the engine", title);
                throw new EngineReAddException($"'{title}' couldn't be re-added: {ex.Message}");
            }

            // The engine returned a different torrent (for example the indexer now serves another file): the old
            // grab will never come back, so record it as ignored rather than leave it reported missing.
            var latestGrab = _downloadHistoryService.GetLatestGrab(downloadId);

            if (latestGrab == null || latestGrab.Id == grab.Id)
            {
                _ignoredDownloadService.IgnoreDownload(trackedDownload);
            }

            _trackedDownloadService.StopTracking(downloadId);
        }

        // ReleaseInfo.IndexerFlags isn't stored with the download-level grab, so read them from the episode grab
        // history, which every path that builds a Missing row leaves untouched.
        private IndexerFlags GetGrabbedIndexerFlags(string downloadId)
        {
            var flags = _historyService.FindByDownloadId(downloadId)
                .Where(h => h.EventType == EpisodeHistoryEventType.Grabbed)
                .OrderByDescending(h => h.Date)
                .Select(h => h.Data?.FirstOrDefault(d => string.Equals(d.Key, "IndexerFlags", StringComparison.OrdinalIgnoreCase)).Value)
                .FirstOrDefault(v => v.IsNotNullOrWhiteSpace());

            return Enum.TryParse(flags, true, out IndexerFlags indexerFlags) ? indexerFlags : 0;
        }
    }
}
