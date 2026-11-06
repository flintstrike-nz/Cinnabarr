using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Blocklisting
{
    public interface IMovieBlocklistRepository : IBasicRepository<MovieBlocklist>
    {
        List<MovieBlocklist> BlocklistedByTitle(int movieId, string sourceTitle);
        List<MovieBlocklist> BlocklistedByTorrentInfoHash(int movieId, string torrentInfoHash);
        void DeleteForMovies(List<int> movieIds);
    }

    public class MovieBlocklistRepository : BasicRepository<MovieBlocklist>, IMovieBlocklistRepository
    {
        public MovieBlocklistRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<MovieBlocklist> BlocklistedByTitle(int movieId, string sourceTitle)
        {
            return Query(x => x.MovieId == movieId && x.SourceTitle.Contains(sourceTitle));
        }

        public List<MovieBlocklist> BlocklistedByTorrentInfoHash(int movieId, string torrentInfoHash)
        {
            return Query(x => x.MovieId == movieId && x.TorrentInfoHash.Contains(torrentInfoHash));
        }

        public void DeleteForMovies(List<int> movieIds)
        {
            Delete(x => movieIds.Contains(x.MovieId));
        }
    }
}
