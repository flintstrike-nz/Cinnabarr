using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public interface IIgnoredDownloadService
    {
        bool IgnoreDownload(TrackedDownload trackedDownload);
    }

    public class IgnoredDownloadService : IIgnoredDownloadService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly Logger _logger;

        public IgnoredDownloadService(IEventAggregator eventAggregator,
                                      IDownloadHistoryService downloadHistoryService,
                                      Logger logger)
        {
            _eventAggregator = eventAggregator;
            _downloadHistoryService = downloadHistoryService;
            _logger = logger;
        }

        public bool IgnoreDownload(TrackedDownload trackedDownload)
        {
            var remoteEpisode = trackedDownload.RemoteEpisode;
            var seriesId = remoteEpisode?.Series?.Id;

            // A row missing from the engine can outlive its episode history (for example when the episodes
            // were removed); its download-level grab still names the series, so it can always be dismissed.
            if (seriesId == null && trackedDownload.IsMissingFromEngine)
            {
                seriesId = _downloadHistoryService.GetLatestGrab(trackedDownload.DownloadItem.DownloadId)?.SeriesId;
            }

            if (seriesId == null)
            {
                _logger.Warn("Unable to ignore download for unknown series");
                return false;
            }

            var downloadIgnoredEvent = new DownloadIgnoredEvent
                                      {
                                          SeriesId = seriesId.Value,
                                          EpisodeIds = remoteEpisode?.Episodes?.Select(e => e.Id).ToList() ?? new List<int>(),
                                          Languages = remoteEpisode?.Languages ?? new(),
                                          Quality = remoteEpisode?.ParsedEpisodeInfo?.Quality,
                                          SourceTitle = trackedDownload.DownloadItem.Title,
                                          DownloadClientInfo = trackedDownload.DownloadItem.DownloadClientInfo,
                                          DownloadId = trackedDownload.DownloadItem.DownloadId,
                                          TrackedDownload = trackedDownload,
                                          Message = "Manually ignored"
                                      };

            _eventAggregator.PublishEvent(downloadIgnoredEvent);
            return true;
        }
    }
}
