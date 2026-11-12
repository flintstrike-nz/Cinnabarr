using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineRaceService
    {
        // Once a movie version's file has been imported, stops every other download still racing for it.
        void CancelLosers(IQBittorrentProxy proxy, QBittorrentSettings settings, string winnerDownloadId, EngineLink winner);
    }

    /// <summary>Roadmap §4.9 "Race mode": the first file to be imported wins; the others are stopped and cleaned up, and are not blocklisted.</summary>
    public class EngineRaceService : IEngineRaceService
    {
        private readonly IEngineLinkService _engineLinkService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly Logger _logger;

        public EngineRaceService(IEngineLinkService engineLinkService, ITrackedDownloadService trackedDownloadService, Logger logger)
        {
            _engineLinkService = engineLinkService;
            _trackedDownloadService = trackedDownloadService;
            _logger = logger;
        }

        public void CancelLosers(IQBittorrentProxy proxy, QBittorrentSettings settings, string winnerDownloadId, EngineLink winner)
        {
            if (winner.MediaType != EngineLinkService.MovieMediaType || winner.TmdbId == 0)
            {
                return;
            }

            foreach (var (downloadId, link) in _engineLinkService.GetAll())
            {
                if (downloadId == winnerDownloadId || !IsLoser(link, winner))
                {
                    continue;
                }

                try
                {
                    Cancel(proxy, settings, downloadId, link);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to stop the race download '{0}'", link.SourceTitle);
                }
            }
        }

        // A download for the same movie version that has put nothing in the library: every file is still under its
        // pending name (or was rejected), and nothing is recorded yet.
        private static bool IsLoser(EngineLink link, EngineLink winner)
        {
            return link.MediaType == EngineLinkService.MovieMediaType &&
                   link.TmdbId == winner.TmdbId &&
                   link.VersionSlotId == winner.VersionSlotId &&
                   !link.Completed &&
                   link.Files != null &&
                   link.Files.Any(f => !f.IsSubtitle && f.State == EngineLinkFileState.Pending) &&
                   link.Files.All(f => f.State is EngineLinkFileState.Pending or EngineLinkFileState.Rejected);
        }

        private void Cancel(IQBittorrentProxy proxy, QBittorrentSettings settings, string downloadId, EngineLink link)
        {
            var hash = downloadId.ToLowerInvariant();

            _logger.Info("'{0}' lost the race for its movie; stopping it", link.SourceTitle);

            proxy.SetFilePriority(hash, link.Files.Select(f => f.Index).ToArray(), 0, settings);
            _engineLinkService.DeletePendingFiles(link);

            foreach (var file in link.Files)
            {
                file.State = EngineLinkFileState.Rejected;
            }

            link.Completed = true;
            _engineLinkService.Save(downloadId, link);

            // The data flag stays off: only hidden pending files were ever written, and they are deleted above.
            proxy.RemoveTorrent(hash, false, settings);
            _trackedDownloadService.StopTracking(downloadId);
        }
    }
}
