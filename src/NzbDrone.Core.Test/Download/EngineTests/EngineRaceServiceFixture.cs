using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineRaceServiceFixture : CoreTest<EngineRaceService>
    {
        private const string Winner = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        private const string Loser = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        private Mock<IQBittorrentProxy> _proxy;
        private QBittorrentSettings _settings;
        private EngineLink _winner;
        private EngineLink _loser;
        private Dictionary<string, EngineLink> _links;

        [SetUp]
        public void Setup()
        {
            _proxy = new Mock<IQBittorrentProxy>();
            _settings = new QBittorrentSettings();
            _winner = Link(EngineLinkFileState.Imported);
            _loser = Link(EngineLinkFileState.Pending);
            _links = new Dictionary<string, EngineLink> { [Winner] = _winner, [Loser] = _loser };

            Mocker.GetMock<IEngineLinkService>().Setup(s => s.GetAll()).Returns(() => _links);
        }

        private static EngineLink Link(EngineLinkFileState state, int tmdbId = 603, int slotId = 0)
        {
            return new EngineLink
            {
                MediaType = EngineLinkService.MovieMediaType,
                TmdbId = tmdbId,
                VersionSlotId = slotId,
                SourceTitle = "Movie",
                SavePath = "/movies/Movie",
                Files = new List<EngineLinkFile>
                {
                    new EngineLinkFile { Index = 0, PendingPath = ".a.pending", FinalPath = "a.mkv", State = state },
                    new EngineLinkFile { Index = 1, IsSubtitle = true, VideoIndex = 0, PendingPath = ".a.srt.pending", FinalPath = "a.srt", State = state }
                }
            };
        }

        [Test]
        public void should_stop_clean_up_and_untrack_a_download_that_lost_the_race()
        {
            Subject.CancelLosers(_proxy.Object, _settings, Winner, _winner);

            _proxy.Verify(p => p.SetFilePriority(Loser.ToLowerInvariant(), It.Is<int[]>(i => i.SequenceEqual(new[] { 0, 1 })), 0, _settings), Times.Once());
            Mocker.GetMock<IEngineLinkService>().Verify(s => s.DeletePendingFiles(_loser), Times.Once());
            _proxy.Verify(p => p.RemoveTorrent(Loser.ToLowerInvariant(), false, _settings), Times.Once());
            Mocker.GetMock<ITrackedDownloadService>().Verify(s => s.StopTracking(Loser), Times.Once());

            Assert.That(_loser.Completed, Is.True);
            Assert.That(_loser.Files.All(f => f.State == EngineLinkFileState.Rejected), Is.True);
            Mocker.GetMock<IEngineLinkService>().Verify(s => s.Save(Loser, _loser), Times.Once());
        }

        [Test]
        public void should_not_touch_the_winner()
        {
            Subject.CancelLosers(_proxy.Object, _settings, Winner, _winner);

            _proxy.Verify(p => p.RemoveTorrent(Winner.ToLowerInvariant(), It.IsAny<bool>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            Assert.That(_winner.Files.All(f => f.State == EngineLinkFileState.Imported), Is.True);
        }

        [Test]
        public void should_leave_downloads_of_another_movie_or_version_alone()
        {
            _links[Loser] = Link(EngineLinkFileState.Pending, tmdbId: 604);
            _links["CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC"] = Link(EngineLinkFileState.Pending, slotId: 5);

            Subject.CancelLosers(_proxy.Object, _settings, Winner, _winner);

            _proxy.Verify(p => p.RemoveTorrent(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_leave_a_download_that_already_put_a_file_in_the_library_alone()
        {
            _links[Loser] = Link(EngineLinkFileState.Renamed);

            Subject.CancelLosers(_proxy.Object, _settings, Winner, _winner);

            _proxy.Verify(p => p.RemoveTorrent(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_leave_a_completed_download_alone()
        {
            _loser.Completed = true;

            Subject.CancelLosers(_proxy.Object, _settings, Winner, _winner);

            _proxy.Verify(p => p.RemoveTorrent(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_ignore_a_series_link()
        {
            _winner.MediaType = EngineLinkService.SeriesMediaType;

            Subject.CancelLosers(_proxy.Object, _settings, Winner, _winner);

            _proxy.Verify(p => p.RemoveTorrent(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }
    }
}
