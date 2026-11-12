using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.History
{
    public interface IMovieHistoryService
    {
        MovieHistory Get(int id);
        MovieHistory MostRecentForMovie(int movieId);
        MovieHistory MostRecentForDownloadId(string downloadId);
        List<MovieHistory> FindByDownloadId(string downloadId);
        List<MovieHistory> GetByMovieId(int movieId);
        List<MovieHistory> Since(DateTime date, MovieHistoryEventType? eventType);
    }

    public class MovieHistoryService : IMovieHistoryService,
                                       IHandle<MovieGrabbedEvent>,
                                       IHandle<MovieDownloadFailedEvent>,
                                       IHandle<MovieFileImportedEvent>,
                                       IHandle<MovieFileDeletedEvent>,
                                       IHandle<MoviesDeletedEvent>
    {
        private readonly IMovieHistoryRepository _historyRepository;

        public MovieHistoryService(IMovieHistoryRepository historyRepository)
        {
            _historyRepository = historyRepository;
        }

        public MovieHistory Get(int id) => _historyRepository.Get(id);

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
            AddVersionSlot(history, remote.Movie.VersionSlotId);

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

        public void Handle(MovieDownloadFailedEvent message)
        {
            var history = new MovieHistory
            {
                EventType = MovieHistoryEventType.DownloadFailed,
                Date = DateTime.UtcNow,
                Quality = message.Quality,
                SourceTitle = message.SourceTitle,
                MovieId = message.MovieId,
                DownloadId = message.DownloadId,
                Languages = message.Languages
            };

            history.Data.Add("DownloadClient", message.DownloadClient);
            history.Data.Add("Message", message.Message);

            _historyRepository.Insert(history);
        }

        public void Handle(MovieFileImportedEvent message)
        {
            if (!message.NewDownload)
            {
                return;
            }

            var movie = message.MovieInfo.Movie;

            var history = new MovieHistory
            {
                EventType = MovieHistoryEventType.DownloadFolderImported,
                Date = DateTime.UtcNow,
                Quality = message.MovieInfo.Quality,
                Languages = message.MovieInfo.Languages,
                SourceTitle = message.ImportedMovie.SceneName ?? Path.GetFileNameWithoutExtension(message.MovieInfo.Path),
                DownloadId = message.DownloadId,
                MovieId = movie.Id
            };

            history.Data.Add("FileId", message.ImportedMovie.Id.ToString());
            history.Data.Add("DroppedPath", message.MovieInfo.Path);
            history.Data.Add("ImportedPath", Path.Combine(movie.Path, message.ImportedMovie.RelativePath));
            history.Data.Add("DownloadClient", message.DownloadClientInfo?.Type);
            history.Data.Add("DownloadClientName", message.DownloadClientInfo?.Name);
            history.Data.Add("ReleaseGroup", message.MovieInfo.ReleaseGroup);
            history.Data.Add("CustomFormatScore", message.MovieInfo.CustomFormatScore.ToString());
            history.Data.Add("Size", message.MovieInfo.Size.ToString());
            history.Data.Add("IndexerFlags", message.ImportedMovie.IndexerFlags.ToString());
            AddVersionSlot(history, message.ImportedMovie.VersionSlotId);

            _historyRepository.Insert(history);
        }

        public void Handle(MovieFileDeletedEvent message)
        {
            // Cleaning up, or replacing the file the movie already had, isn't something to show in the history.
            if (message.Reason is DeleteMediaFileReason.NoLinkedEpisodes or DeleteMediaFileReason.ManualOverride)
            {
                return;
            }

            var history = new MovieHistory
            {
                EventType = MovieHistoryEventType.MovieFileDeleted,
                Date = DateTime.UtcNow,
                Quality = message.MovieFile.Quality,
                Languages = message.MovieFile.Languages,
                SourceTitle = message.MovieFile.Path,
                MovieId = message.MovieFile.MovieId
            };

            history.Data.Add("Reason", message.Reason.ToString());
            history.Data.Add("ReleaseGroup", message.MovieFile.ReleaseGroup);
            history.Data.Add("Size", message.MovieFile.Size.ToString());
            history.Data.Add("IndexerFlags", message.MovieFile.IndexerFlags.ToString());
            AddVersionSlot(history, message.MovieFile.VersionSlotId);

            _historyRepository.Insert(history);
        }

        private static void AddVersionSlot(MovieHistory history, int slotId)
        {
            if (slotId != 0)
            {
                history.Data.Add(MovieHistory.VERSION_SLOT_ID, slotId.ToString());
            }
        }

        public void Handle(MoviesDeletedEvent message)
        {
            _historyRepository.DeleteForMovies(message.Movies.Select(m => m.Id).ToList());
        }
    }
}
