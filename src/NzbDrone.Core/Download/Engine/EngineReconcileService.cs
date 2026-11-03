using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineReconcileService
    {
        // Compares Embarr's unfinished grabs for the built-in engine with the torrents the engine
        // reported, and returns the grabs whose torrent is gone (roadmap §4.4 rule 3).
        List<TrackedDownload> FindMissing(DownloadClientDefinition downloadClient, IEnumerable<string> presentDownloadIds);
    }

    public class EngineReconcileService : IEngineReconcileService
    {
        // A grab has to be absent from this many successful polls in a row before it is flagged, so a
        // torrent list that is briefly incomplete (for example while the engine starts) flags nothing.
        public const int PollsBeforeMissing = 2;

        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly object _lock = new();

        private Dictionary<string, int> _missedPolls = new(StringComparer.OrdinalIgnoreCase);

        public EngineReconcileService(IDownloadHistoryService downloadHistoryService, ITrackedDownloadService trackedDownloadService)
        {
            _downloadHistoryService = downloadHistoryService;
            _trackedDownloadService = trackedDownloadService;
        }

        public List<TrackedDownload> FindMissing(DownloadClientDefinition downloadClient, IEnumerable<string> presentDownloadIds)
        {
            var present = new HashSet<string>(presentDownloadIds, StringComparer.OrdinalIgnoreCase);

            var absent = _downloadHistoryService.GetUnfinishedGrabs(downloadClient.Id)
                .Where(g => !present.Contains(g.DownloadId))
                .ToList();

            var flagged = new List<DownloadHistory>();

            lock (_lock)
            {
                // Forget grabs that are back in the engine or finished, so a later absence starts counting again.
                var missedPolls = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var grab in absent)
                {
                    var count = _missedPolls.GetValueOrDefault(grab.DownloadId) + 1;
                    missedPolls[grab.DownloadId] = count;

                    if (count >= PollsBeforeMissing)
                    {
                        flagged.Add(grab);
                    }
                }

                _missedPolls = missedPolls;
            }

            // Reconcile only flags: nothing is deleted from disk, the engine or Embarr's records (rule 4).
            return flagged.Select(g => _trackedDownloadService.TrackMissingFromEngine(downloadClient, g)).ToList();
        }
    }
}
