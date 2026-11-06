using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadHistoryTests
{
    [TestFixture]
    public class DownloadHistoryServiceFixture : CoreTest<DownloadHistoryService>
    {
        private const int ClientId = 3;

        [Test]
        public void should_record_failure_marked_from_history_against_the_grab()
        {
            var grab = new DownloadHistory { DownloadId = "AAA", EventType = DownloadHistoryEventType.DownloadGrabbed, DownloadClientId = ClientId, Protocol = DownloadProtocol.Torrent };

            Mocker.GetMock<IDownloadHistoryRepository>()
                .Setup(r => r.FindByDownloadId("AAA"))
                .Returns([grab]);

            Subject.Handle(new DownloadFailedEvent { DownloadId = "AAA", SeriesId = 5, SourceTitle = "Series.S01E01" });

            Mocker.GetMock<IDownloadHistoryRepository>()
                .Verify(r => r.Insert(It.Is<DownloadHistory>(h => h.EventType == DownloadHistoryEventType.DownloadFailed && h.DownloadId == "AAA" && h.DownloadClientId == ClientId)), Times.Once());
        }

        [Test]
        public void should_not_record_failure_marked_from_history_for_unknown_download()
        {
            Mocker.GetMock<IDownloadHistoryRepository>()
                .Setup(r => r.FindByDownloadId("AAA"))
                .Returns([]);

            Subject.Handle(new DownloadFailedEvent { DownloadId = "AAA", SeriesId = 5 });

            Mocker.GetMock<IDownloadHistoryRepository>()
                .Verify(r => r.Insert(It.IsAny<DownloadHistory>()), Times.Never());
        }

        private static bool IsMovieGrab(DownloadHistory h)
        {
            return h.EventType == DownloadHistoryEventType.DownloadGrabbed &&
                   h.MovieId == 8 &&
                   h.SeriesId == 0 &&
                   h.DownloadId == "AAA" &&
                   h.IndexerId == 4 &&
                   h.DownloadClientId == ClientId &&
                   h.Data["CustomFormatScore"] == "20";
        }

        [Test]
        public void should_record_a_movie_grab_against_the_movie()
        {
            var remote = new NzbDrone.Core.Parser.Model.RemoteMovie
            {
                Movie = new NzbDrone.Core.Movies.Movie { Id = 8 },
                Release = new NzbDrone.Core.Parser.Model.ReleaseInfo { Title = "The.Matrix.1999.1080p", Indexer = "Site", IndexerId = 4, DownloadProtocol = DownloadProtocol.Torrent },
                CustomFormatScore = 20
            };

            Subject.Handle(new MovieGrabbedEvent(remote) { DownloadId = "AAA", DownloadClientId = ClientId, DownloadClient = "qBittorrent", DownloadClientName = "Engine" });

            Mocker.GetMock<IDownloadHistoryRepository>()
                .Verify(r => r.Insert(It.Is<DownloadHistory>(h => IsMovieGrab(h))), Times.Once());
        }

        [Test]
        public void should_not_record_a_movie_grab_without_a_download_id()
        {
            var remote = new NzbDrone.Core.Parser.Model.RemoteMovie { Movie = new NzbDrone.Core.Movies.Movie { Id = 8 }, Release = new NzbDrone.Core.Parser.Model.ReleaseInfo() };

            Subject.Handle(new MovieGrabbedEvent(remote));

            Mocker.GetMock<IDownloadHistoryRepository>().Verify(r => r.Insert(It.IsAny<DownloadHistory>()), Times.Never());
        }

        [Test]
        public void should_delete_the_download_history_of_deleted_movies()
        {
            Subject.Handle(new NzbDrone.Core.Movies.Events.MoviesDeletedEvent(new List<NzbDrone.Core.Movies.Movie> { new() { Id = 8 }, new() { Id = 9 } }, false, false));

            Mocker.GetMock<IDownloadHistoryRepository>().Verify(r => r.DeleteByMovieIds(It.Is<List<int>>(ids => ids.SequenceEqual(new[] { 8, 9 }))), Times.Once());
        }
    }
}
