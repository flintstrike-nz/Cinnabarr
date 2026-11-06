using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.History
{
    public interface IMovieHistoryRepository : IBasicRepository<MovieHistory>
    {
        MovieHistory MostRecentForMovie(int movieId);
        MovieHistory MostRecentForDownloadId(string downloadId);
        List<MovieHistory> FindByMovieId(int movieId);
        List<MovieHistory> FindByDownloadId(string downloadId);
        List<MovieHistory> Since(DateTime date, MovieHistoryEventType? eventType);
        void DeleteForMovies(List<int> movieIds);
    }

    public class MovieHistoryRepository : BasicRepository<MovieHistory>, IMovieHistoryRepository
    {
        public MovieHistoryRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public MovieHistory MostRecentForMovie(int movieId)
        {
            return Query(x => x.MovieId == movieId).MaxBy(h => h.Date);
        }

        public MovieHistory MostRecentForDownloadId(string downloadId)
        {
            return FindByDownloadId(downloadId).MaxBy(h => h.Date);
        }

        public List<MovieHistory> FindByMovieId(int movieId)
        {
            return Query(h => h.MovieId == movieId).OrderByDescending(h => h.Date).ToList();
        }

        public List<MovieHistory> FindByDownloadId(string downloadId)
        {
            return Query(x => x.DownloadId == downloadId);
        }

        public List<MovieHistory> Since(DateTime date, MovieHistoryEventType? eventType)
        {
            var builder = Builder().Where<MovieHistory>(x => x.Date >= date);

            if (eventType.HasValue)
            {
                builder.Where<MovieHistory>(h => h.EventType == eventType);
            }

            return Query(builder).OrderBy(h => h.Date).ToList();
        }

        public void DeleteForMovies(List<int> movieIds)
        {
            Delete(x => movieIds.Contains(x.MovieId));
        }
    }
}
