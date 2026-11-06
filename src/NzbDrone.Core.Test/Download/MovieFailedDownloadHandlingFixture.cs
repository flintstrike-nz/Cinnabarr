using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class RedownloadFailedMovieServiceFixture : CoreTest<RedownloadFailedMovieService>
    {
        private MovieDownloadFailedEvent _event;

        [SetUp]
        public void SetUp()
        {
            _event = new MovieDownloadFailedEvent { MovieId = 8, ReleaseSource = ReleaseSourceType.Rss };
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AutoRedownloadFailed).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AutoRedownloadFailedFromInteractiveSearch).Returns(true);
        }

        [Test]
        public void should_search_for_the_movie_again()
        {
            Subject.Handle(_event);

            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.Is<MoviesSearchCommand>(c => c.MovieIds.SequenceEqual(new[] { 8 })), CommandPriority.Normal, CommandTrigger.Unspecified), Times.Once());
        }

        [Test]
        public void should_not_search_again_when_the_user_asked_to_skip()
        {
            _event.SkipRedownload = true;

            Subject.Handle(_event);

            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<MoviesSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_not_search_again_when_auto_redownload_is_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AutoRedownloadFailed).Returns(false);

            Subject.Handle(_event);

            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<MoviesSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_not_search_again_for_an_interactive_grab_when_that_is_off()
        {
            _event.ReleaseSource = ReleaseSourceType.InteractiveSearch;
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AutoRedownloadFailedFromInteractiveSearch).Returns(false);

            Subject.Handle(_event);

            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<MoviesSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }
    }

    [TestFixture]
    public class MovieFailureRecordingFixture : CoreTest<MovieBlocklistService>
    {
        [Test]
        public void should_blocklist_the_failed_release_by_info_hash()
        {
            MovieBlocklist saved = null;
            Mocker.GetMock<IMovieBlocklistRepository>().Setup(r => r.Insert(It.IsAny<MovieBlocklist>())).Callback<MovieBlocklist>(b => saved = b);

            var data = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Indexer"] = "Site",
                ["Size"] = "1234",
                ["PublishedDate"] = "2026-01-02T03:04:05Z",
                ["Protocol"] = ((int)DownloadProtocol.Torrent).ToString(),
                ["IndexerFlags"] = "Freeleech"
            };

            Subject.Handle(new MovieDownloadFailedEvent
            {
                MovieId = 8,
                SourceTitle = "The.Matrix.1999",
                Quality = new QualityModel(Quality.Bluray1080p),
                Message = "No video",
                Languages = new List<Language> { Language.English },
                Data = data,
                TrackedDownload = new TrackedDownload
                {
                    Protocol = DownloadProtocol.Torrent,
                    DownloadItem = new DownloadClientItem { DownloadId = "HASH" }
                }
            });

            saved.Should().NotBeNull();
            saved.MovieId.Should().Be(8);
            saved.TorrentInfoHash.Should().Be("HASH");
            saved.Indexer.Should().Be("Site");
            saved.Size.Should().Be(1234);
            saved.Protocol.Should().Be(DownloadProtocol.Torrent);
            saved.IndexerFlags.Should().Be(IndexerFlags.Freeleech);
            saved.Message.Should().Be("No video");
            saved.PublishedDate.Should().NotBeNull();
        }

        [Test]
        public void should_blocklist_without_a_tracked_download_using_the_recorded_hash()
        {
            MovieBlocklist saved = null;
            Mocker.GetMock<IMovieBlocklistRepository>().Setup(r => r.Insert(It.IsAny<MovieBlocklist>())).Callback<MovieBlocklist>(b => saved = b);

            Subject.Handle(new MovieDownloadFailedEvent
            {
                MovieId = 8,
                SourceTitle = "x",
                Data = new Dictionary<string, string> { ["torrentInfoHash"] = "FROMHISTORY" }
            });

            saved.TorrentInfoHash.Should().Be("FROMHISTORY");
        }
    }

    [TestFixture]
    public class MovieFailureHistoryFixture : CoreTest<DownloadHistoryService>
    {
        [Test]
        public void should_record_the_failure_against_the_known_grab()
        {
            Mocker.GetMock<IDownloadHistoryRepository>()
                  .Setup(r => r.FindByDownloadId("AAA"))
                  .Returns(new List<DownloadHistory> { new() { DownloadId = "AAA", EventType = DownloadHistoryEventType.DownloadGrabbed, DownloadClientId = 3, Protocol = DownloadProtocol.Torrent } });

            Subject.Handle(new MovieDownloadFailedEvent { MovieId = 8, DownloadId = "AAA", SourceTitle = "x" });

            Mocker.GetMock<IDownloadHistoryRepository>()
                  .Verify(r => r.Insert(It.Is<DownloadHistory>(h => h.EventType == DownloadHistoryEventType.DownloadFailed && h.MovieId == 8 && h.DownloadClientId == 3)), Times.Once());
        }

        [Test]
        public void should_not_record_a_failure_for_an_unknown_download()
        {
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(r => r.FindByDownloadId("AAA")).Returns(new List<DownloadHistory>());

            Subject.Handle(new MovieDownloadFailedEvent { MovieId = 8, DownloadId = "AAA" });

            Mocker.GetMock<IDownloadHistoryRepository>().Verify(r => r.Insert(It.IsAny<DownloadHistory>()), Times.Never());
        }
    }

    [TestFixture]
    public class MovieFailureMovieHistoryFixture : CoreTest<MovieHistoryService>
    {
        [Test]
        public void should_record_a_failed_download_in_the_movie_history()
        {
            MovieHistory saved = null;
            Mocker.GetMock<IMovieHistoryRepository>().Setup(r => r.Insert(It.IsAny<MovieHistory>())).Callback<MovieHistory>(h => saved = h);

            Subject.Handle(new MovieDownloadFailedEvent { MovieId = 8, DownloadId = "AAA", SourceTitle = "x", Message = "bad", DownloadClient = "qBittorrent" });

            saved.EventType.Should().Be(MovieHistoryEventType.DownloadFailed);
            saved.MovieId.Should().Be(8);
            saved.DownloadId.Should().Be("AAA");
            saved.Data["message"].Should().Be("bad");
        }
    }
}
