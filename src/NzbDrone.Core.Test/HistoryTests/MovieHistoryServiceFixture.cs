using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HistoryTests
{
    [TestFixture]
    public class MovieHistoryServiceFixture : CoreTest<MovieHistoryService>
    {
        [Test]
        public void should_record_a_grab_with_the_release_details()
        {
            var remote = new RemoteMovie
            {
                Movie = new Movie { Id = 3 },
                ParsedMovieInfo = new ParsedMovieInfo { Quality = new QualityModel(Quality.Bluray1080p), ReleaseGroup = "GRP" },
                Languages = new List<Language> { Language.English },
                Release = new TorrentInfo { Title = "The.Matrix.1999.1080p.BluRay.x264-GRP", Indexer = "Site", Size = 1234, InfoHash = "HASH", DownloadProtocol = NzbDrone.Core.Indexers.DownloadProtocol.Torrent },
                CustomFormatScore = 15
            };

            MovieHistory saved = null;
            Mocker.GetMock<IMovieHistoryRepository>().Setup(r => r.Insert(It.IsAny<MovieHistory>())).Callback<MovieHistory>(h => saved = h);

            Subject.Handle(new MovieGrabbedEvent(remote) { DownloadId = "HASH", DownloadClient = "qBittorrent", DownloadClientName = "Engine" });

            saved.Should().NotBeNull();
            saved.EventType.Should().Be(MovieHistoryEventType.Grabbed);
            saved.MovieId.Should().Be(3);
            saved.DownloadId.Should().Be("HASH");
            saved.SourceTitle.Should().Be("The.Matrix.1999.1080p.BluRay.x264-GRP");
            saved.Quality.Quality.Should().Be(Quality.Bluray1080p);
            saved.Data["indexer"].Should().Be("Site");
            saved.Data["size"].Should().Be("1234");
            saved.Data["releaseGroup"].Should().Be("GRP");
            saved.Data["torrentInfoHash"].Should().Be("HASH");
            saved.Data["customFormatScore"].Should().Be("15");
        }

        [Test]
        public void should_remove_the_history_of_deleted_movies()
        {
            Subject.Handle(new MoviesDeletedEvent(new List<Movie> { new Movie { Id = 3 }, new Movie { Id = 4 } }, false, false));

            Mocker.GetMock<IMovieHistoryRepository>().Verify(r => r.DeleteForMovies(It.Is<List<int>>(ids => ids.SequenceEqual(new[] { 3, 4 }))), Times.Once());
        }
    }
}
