using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.History
{
    public interface IDownloadHistoryRepository : IBasicRepository<DownloadHistory>
    {
        List<DownloadHistory> FindByDownloadId(string downloadId);
        List<DownloadHistory> FindByDownloadClientId(int downloadClientId);
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

        public List<DownloadHistory> FindByDownloadClientId(int downloadClientId)
        {
            return Query(h => h.DownloadClientId == downloadClientId).OrderByDescending(h => h.Date).ToList();
        }

        public void DeleteBySeriesIds(List<int> seriesIds)
        {
            Delete(r => seriesIds.Contains(r.SeriesId));
        }
    }
}
