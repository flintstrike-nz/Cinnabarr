using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.History
{
    public interface IMovieHistoryService
    {
        MovieHistory MostRecentForMovie(int movieId);
        MovieHistory MostRecentForDownloadId(string downloadId);
        List<MovieHistory> FindByDownloadId(string downloadId);
        List<MovieHistory> GetByMovieId(int movieId);
        List<MovieHistory> Since(DateTime date, MovieHistoryEventType? eventType);
    }

    public class MovieHistoryService : IMovieHistoryService,
                                       IHandle<MovieGrabbedEvent>,
                                       IHandle<MoviesDeletedEvent>
    {
        private readonly IMovieHistoryRepository _historyRepository;

        public MovieHistoryService(IMovieHistoryRepository historyRepository)
        {
            _historyRepository = historyRepository;
        }

        public MovieHistory MostRecentForMovie(int movieId) => _historyRepository.MostRecentForMovie(movieId);

        public MovieHistory MostRecentForDownloadId(string downloadId) => _historyRepository.MostRecentForDownloadId(downloadId);

        public List<MovieHistory> FindByDownloadId(string downloadId) => _historyRepository.FindByDownloadId(downloadId);

        public List<MovieHistory> GetByMovieId(int movieId) => _historyRepository.FindByMovieId(movieId);

        public List<MovieHistory> Since(DateTime date, MovieHistoryEventType? eventType) => _historyRepository.Since(date, eventType);

        public void Handle(MovieGrabbedEvent message)
        {
            var remote = message.Movie;
            var release = remote.Release;

            var history = new MovieHistory
            {
                EventType = MovieHistoryEventType.Grabbed,
                Date = DateTime.UtcNow,
                Quality = remote.ParsedMovieInfo.Quality,
                Languages = remote.Languages,
                SourceTitle = release.Title,
                DownloadId = message.DownloadId,
                MovieId = remote.Movie.Id
            };

            history.Data.Add("Indexer", release.Indexer);
            history.Data.Add("NzbInfoUrl", release.InfoUrl);
            history.Data.Add("ReleaseGroup", remote.ParsedMovieInfo.ReleaseGroup);
            history.Data.Add("Age", release.Age.ToString());
            history.Data.Add("AgeHours", release.AgeHours.ToString());
            history.Data.Add("AgeMinutes", release.AgeMinutes.ToString());
            history.Data.Add("PublishedDate", release.PublishDate.ToUniversalTime().ToString("s") + "Z");
            history.Data.Add("DownloadClient", message.DownloadClient);
            history.Data.Add("DownloadClientName", message.DownloadClientName);
            history.Data.Add("Size", release.Size.ToString());
            history.Data.Add("DownloadUrl", release.DownloadUrl);
            history.Data.Add("Guid", release.Guid);
            history.Data.Add("TmdbId", release.TmdbId.ToString());
            history.Data.Add("ImdbId", release.ImdbId);
            history.Data.Add("Protocol", ((int)release.DownloadProtocol).ToString());
            history.Data.Add("CustomFormatScore", remote.CustomFormatScore.ToString());
            history.Data.Add("MovieMatchType", remote.MovieMatchType.ToString());
            history.Data.Add("ReleaseSource", remote.ReleaseSource.ToString());
            history.Data.Add("IndexerFlags", release.IndexerFlags.ToString());
            history.Data.Add("IndexerId", release.IndexerId.ToString());

            if (remote.ParsedMovieInfo.ReleaseHash.IsNotNullOrWhiteSpace())
            {
                history.Data.Add("ReleaseHash", remote.ParsedMovieInfo.ReleaseHash);
            }

            if (release is TorrentInfo torrent)
            {
                history.Data.Add("TorrentInfoHash", torrent.InfoHash);
            }

            _historyRepository.Insert(history);
        }

        public void Handle(MoviesDeletedEvent message)
        {
            _historyRepository.DeleteForMovies(message.Movies.Select(m => m.Id).ToList());
        }
    }
}
