using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Tv.Versions
{
    public interface ISeriesVersionSlotRepository : IBasicRepository<SeriesVersionSlot>
    {
        List<SeriesVersionSlot> GetBySeries(int seriesId);
        void DeleteForSeries(List<int> seriesIds);
    }

    public class SeriesVersionSlotRepository : BasicRepository<SeriesVersionSlot>, ISeriesVersionSlotRepository
    {
        public SeriesVersionSlotRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<SeriesVersionSlot> GetBySeries(int seriesId)
        {
            return Query(s => s.SeriesId == seriesId);
        }

        public void DeleteForSeries(List<int> seriesIds)
        {
            Delete(s => seriesIds.Contains(s.SeriesId));
        }
    }

    public interface IEpisodeVersionFileRepository : IBasicRepository<EpisodeVersionFile>
    {
        List<EpisodeVersionFile> GetByEpisodes(List<int> episodeIds);
        List<EpisodeVersionFile> GetByFile(int episodeFileId);
        void DeleteForFile(int episodeFileId);
        void DeleteForSlots(List<int> slotIds);
    }

    public class EpisodeVersionFileRepository : BasicRepository<EpisodeVersionFile>, IEpisodeVersionFileRepository
    {
        public EpisodeVersionFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<EpisodeVersionFile> GetByEpisodes(List<int> episodeIds)
        {
            return episodeIds.Empty() ? new List<EpisodeVersionFile>() : Query(l => episodeIds.Contains(l.EpisodeId));
        }

        public List<EpisodeVersionFile> GetByFile(int episodeFileId)
        {
            return Query(l => l.EpisodeFileId == episodeFileId);
        }

        public void DeleteForFile(int episodeFileId)
        {
            Delete(l => l.EpisodeFileId == episodeFileId);
        }

        public void DeleteForSlots(List<int> slotIds)
        {
            if (slotIds.Any())
            {
                Delete(l => slotIds.Contains(l.VersionSlotId));
            }
        }
    }
}
