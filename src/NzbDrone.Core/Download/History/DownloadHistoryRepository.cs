using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.History
{
    public interface IDownloadHistoryRepository : IBasicRepository<DownloadHistory>
    {
        List<DownloadHistory> FindByDownloadId(string downloadId);
        List<DownloadHistory> FindUnfinishedGrabs(int downloadClientId);
        void DeleteBySeriesIds(List<int> seriesIds);
    }

    public class DownloadHistoryRepository : BasicRepository<DownloadHistory>, IDownloadHistoryRepository
    {
        public DownloadHistoryRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<DownloadHistory> FindByDownloadId(string downloadId)
        {
            return Query(h => h.DownloadId == downloadId).OrderByDescending(h => h.Date).ToList();
        }

        // Grabs sent to this client whose latest outcome is still the grab: no later grab, import, failure or
        // ignore for the same download id (partial FileImported events don't end a grab). Filtered in the
        // database so each poll reads only unfinished grabs, using the DownloadId index. Download ids are
        // written by the same client code each time, so they match exactly.
        public List<DownloadHistory> FindUnfinishedGrabs(int downloadClientId)
        {
            var outcomes = string.Join(", ", new[]
            {
                (int)DownloadHistoryEventType.DownloadGrabbed,
                (int)DownloadHistoryEventType.DownloadImported,
                (int)DownloadHistoryEventType.DownloadFailed,
                (int)DownloadHistoryEventType.DownloadIgnored
            });

            var builder = Builder()
                .Where<DownloadHistory>(h => h.DownloadClientId == downloadClientId && h.EventType == DownloadHistoryEventType.DownloadGrabbed)
                .Where($"NOT EXISTS (SELECT 1 FROM \"DownloadHistory\" AS \"Later\" WHERE \"Later\".\"DownloadId\" = \"DownloadHistory\".\"DownloadId\" AND \"Later\".\"EventType\" IN ({outcomes}) AND \"Later\".\"Date\" > \"DownloadHistory\".\"Date\")");

            return Query(builder).OrderByDescending(h => h.Date).ToList();
        }

        public void DeleteBySeriesIds(List<int> seriesIds)
        {
            Delete(r => seriesIds.Contains(r.SeriesId));
        }
    }
}
