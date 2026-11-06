using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Blocklisting
{
    public interface IMovieBlocklistRepository : IBasicRepository<MovieBlocklist>
    {
        List<MovieBlocklist> BlocklistedByTitle(int movieId, string sourceTitle);
        List<MovieBlocklist> BlocklistedByTorrentInfoHash(int movieId, string torrentInfoHash);
        List<MovieBlocklist> GetByMovieId(int movieId);
        void DeleteForMovies(List<int> movieIds);
    }

    public class MovieBlocklistRepository : BasicRepository<MovieBlocklist>, IMovieBlocklistRepository
    {
        public MovieBlocklistRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<MovieBlocklist> GetByMovieId(int movieId)
        {
            return Query(x => x.MovieId == movieId).OrderByDescending(x => x.Date).ToList();
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
