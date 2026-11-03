using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Exceptions;
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
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IDownloadService _downloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IIgnoredDownloadService _ignoredDownloadService;
        private readonly Logger _logger;

        public EngineReAddService(IDownloadHistoryService downloadHistoryService,
                                  IDownloadService downloadService,
                                  ITrackedDownloadService trackedDownloadService,
                                  IIgnoredDownloadService ignoredDownloadService,
                                  Logger logger)
        {
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
                IndexerFlags = release.IndexerFlags,
                PublishDate = release.PublishDate,
                Languages = release.Languages,
                DownloadProtocol = DownloadProtocol.Torrent,
                InfoHash = downloadId
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
            catch (Exception ex) when (ex is ReleaseDownloadException or DownloadClientException)
            {
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
    }
}
