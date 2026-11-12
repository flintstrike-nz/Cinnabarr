using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Movies.Versions
{
    public interface IVersionSlotRepository : IBasicRepository<VersionSlot>
    {
        List<VersionSlot> GetByMovie(int movieId);
        void DeleteForMovies(List<int> movieIds);
    }

    public class VersionSlotRepository : BasicRepository<VersionSlot>, IVersionSlotRepository
    {
        public VersionSlotRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<VersionSlot> GetByMovie(int movieId)
        {
            return Query(s => s.MovieId == movieId);
        }

        public void DeleteForMovies(List<int> movieIds)
        {
            Delete(s => movieIds.Contains(s.MovieId));
        }
    }
}
