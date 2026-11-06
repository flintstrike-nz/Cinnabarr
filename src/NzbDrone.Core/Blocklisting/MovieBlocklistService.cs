using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Blocklisting
{
    public interface IMovieBlocklistService
    {
        bool Blocklisted(int movieId, ReleaseInfo release);
        bool BlocklistedTorrentHash(int movieId, string hash);
        void Block(RemoteMovie remoteMovie, string message);
        List<MovieBlocklist> GetByMovieId(int movieId);
        void Delete(int id);
        void Clear();
    }

    public class MovieBlocklistService : IMovieBlocklistService,
                                         IHandle<MovieDownloadFailedEvent>,
                                         IHandle<MoviesDeletedEvent>
    {
        private readonly IMovieBlocklistRepository _repository;

        public MovieBlocklistService(IMovieBlocklistRepository repository)
        {
            _repository = repository;
        }

        public bool Blocklisted(int movieId, ReleaseInfo release)
        {
            if (release.DownloadProtocol != DownloadProtocol.Torrent || release is not TorrentInfo torrent)
            {
                return false;
            }

            if (torrent.InfoHash.IsNotNullOrWhiteSpace())
            {
                return _repository.BlocklistedByTorrentInfoHash(movieId, torrent.InfoHash).Any(b => SameTorrent(b, torrent));
            }

            return _repository.BlocklistedByTitle(movieId, release.Title)
                .Where(b => b.Protocol == DownloadProtocol.Torrent)
                .Any(b => SameTorrent(b, torrent));
        }

        public List<MovieBlocklist> GetByMovieId(int movieId)
        {
            return _repository.GetByMovieId(movieId);
        }

        public void Delete(int id)
        {
            _repository.Delete(id);
        }

        public bool BlocklistedTorrentHash(int movieId, string hash)
        {
            return _repository.BlocklistedByTorrentInfoHash(movieId, hash)
                .Any(b => b.TorrentInfoHash.Equals(hash, StringComparison.InvariantCultureIgnoreCase));
        }

        public void Block(RemoteMovie remoteMovie, string message)
        {
            var blocklist = new MovieBlocklist
            {
                MovieId = remoteMovie.Movie.Id,
                SourceTitle = remoteMovie.Release.Title,
                Quality = remoteMovie.ParsedMovieInfo.Quality,
                Date = DateTime.UtcNow,
                PublishedDate = remoteMovie.Release.PublishDate,
                Size = remoteMovie.Release.Size,
                Indexer = remoteMovie.Release.Indexer,
                IndexerFlags = remoteMovie.Release.IndexerFlags,
                Protocol = remoteMovie.Release.DownloadProtocol,
                Message = message,
                Languages = remoteMovie.ParsedMovieInfo.Languages
            };

            if (remoteMovie.Release is TorrentInfo torrentRelease)
            {
                blocklist.TorrentInfoHash = torrentRelease.InfoHash;
            }

            _repository.Insert(blocklist);
        }

        public void Clear()
        {
            _repository.Purge();
        }

        public void Handle(MovieDownloadFailedEvent message)
        {
            var blocklist = new MovieBlocklist
            {
                MovieId = message.MovieId,
                SourceTitle = message.SourceTitle,
                Quality = message.Quality,
                Date = DateTime.UtcNow,
                PublishedDate = DateTime.TryParse(message.Data.GetValueOrDefault("publishedDate"), out var published) ? published : null,
                Size = long.TryParse(message.Data.GetValueOrDefault("size", "0"), out var size) ? size : 0,
                Indexer = message.Data.GetValueOrDefault("indexer"),
                Protocol = Enum.TryParse(message.Data.GetValueOrDefault("protocol"), out DownloadProtocol protocol) ? protocol : DownloadProtocol.Unknown,
                Message = message.Message,
                Languages = message.Languages,
                TorrentInfoHash = message.TrackedDownload?.Protocol == DownloadProtocol.Torrent
                    ? message.TrackedDownload.DownloadItem.DownloadId
                    : message.Data.GetValueOrDefault("torrentInfoHash")
            };

            if (Enum.TryParse(message.Data.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
            {
                blocklist.IndexerFlags = flags;
            }

            _repository.Insert(blocklist);
        }

        public void Handle(MoviesDeletedEvent message)
        {
            _repository.DeleteForMovies(message.Movies.Select(m => m.Id).ToList());
        }

        private static bool SameTorrent(MovieBlocklist item, TorrentInfo release)
        {
            return ReleaseComparer.SameTorrent(new ReleaseComparerModel(item), release);
        }
    }
}
