using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineTorrentPreparationServiceFixture : CoreTest<EngineTorrentPreparationService>
    {
        private const string Hash = "abcdef0123456789abcdef0123456789abcdef01";

        private QBittorrentSettings _settings;
        private Mock<IQBittorrentProxy> _proxy;
        private QBittorrentTorrent _torrent;
        private List<QBittorrentTorrentFile> _files;

        [SetUp]
        public void Setup()
        {
            _settings = new QBittorrentSettings();
            _proxy = new Mock<IQBittorrentProxy>();
            _torrent = new QBittorrentTorrent { Hash = Hash, Name = "Show.S01", State = "stoppedDL", HasMetadata = true, Tags = EngineTorrentTags.Held };
            _files = new List<QBittorrentTorrentFile>
            {
                new QBittorrentTorrentFile { Name = "Show.S01/Show.S01E01.mkv" },
                new QBittorrentTorrentFile { Name = "Show.S01/Show.S01E01.en.srt" },
                new QBittorrentTorrentFile { Name = "Show.S01/Show.S01.nfo" },
                new QBittorrentTorrentFile { Name = "Show.S01/Sample/sample.mkv.exe" }
            };

            _proxy.Setup(p => p.GetTorrents(_settings)).Returns(() => new List<QBittorrentTorrent> { _torrent });
            _proxy.Setup(p => p.GetTorrentFiles(Hash, _settings)).Returns(() => _files);

            Mocker.GetMock<IQBittorrentProxySelector>()
                .Setup(s => s.GetProxy(_settings, false))
                .Returns(_proxy.Object);

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(Hash.ToUpperInvariant()))
                .Returns(new List<EpisodeHistory> { new EpisodeHistory { Id = 12, EventType = EpisodeHistoryEventType.Grabbed, Date = DateTime.UtcNow } });
        }

        private IDownloadClient GivenClient(QBittorrentSettings settings = null)
        {
            var client = new Mock<IDownloadClient>();
            client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 3, Settings = settings ?? _settings });

            return client.Object;
        }

        [Test]
        public void should_skip_unwanted_files_then_start_and_tag()
        {
            Subject.Prepare(GivenClient());

            _proxy.Verify(p => p.SetFilePriority(Hash, It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 2, 3 })), 0, _settings), Times.Once());
            _proxy.Verify(p => p.StartTorrent(Hash, _settings), Times.Once());
            _proxy.Verify(p => p.AddTags(Hash, It.Is<IEnumerable<string>>(t => t.Single() == EngineTorrentTags.Prepared), _settings), Times.Once());
        }

        [Test]
        public void should_force_start_after_preparation_when_client_is_set_to_force_start()
        {
            _settings.InitialState = (int)QBittorrentState.ForceStart;

            Subject.Prepare(GivenClient());

            _proxy.Verify(p => p.SetForceStart(Hash, true, _settings), Times.Once());
        }

        [Test]
        public void should_not_force_start_by_default()
        {
            Subject.Prepare(GivenClient());

            _proxy.Verify(p => p.SetForceStart(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_skip_files_before_starting_the_torrent()
        {
            var order = new List<string>();
            _proxy.Setup(p => p.SetFilePriority(Hash, It.IsAny<IEnumerable<int>>(), 0, _settings)).Callback(() => order.Add("priority"));
            _proxy.Setup(p => p.StartTorrent(Hash, _settings)).Callback(() => order.Add("start"));
            _proxy.Setup(p => p.AddTags(Hash, It.IsAny<IEnumerable<string>>(), _settings)).Callback(() => order.Add("tag"));

            Subject.Prepare(GivenClient());

            order.Should().Equal("priority", "start", "tag");
        }

        [Test]
        public void should_map_files_to_the_library_before_starting()
        {
            var order = new List<string>();
            var link = new EngineLink();
            var map = new EngineFileMap
            {
                SavePath = "/tv/Show/Season 01",
                Files = new List<EngineLinkFile>
                {
                    new EngineLinkFile { Index = 0, FinalPath = "Show - S01E01.mkv", PendingPath = ".Show - S01E01.mkv.pending" },
                    new EngineLinkFile { Index = 1, IsSubtitle = true, VideoIndex = 0, FinalPath = "Show - S01E01.en.srt", PendingPath = ".Show - S01E01.en.srt.pending" }
                }
            };

            Mocker.GetMock<IEngineLinkService>().Setup(s => s.Get(Hash.ToUpperInvariant())).Returns(link);
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(It.IsAny<int>())).Returns(new Series { Id = 1 });
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(1)).Returns(new List<Episode> { new Episode { Id = 0 } });
            Mocker.GetMock<IEngineFileMapper>()
                .Setup(s => s.Map(Hash.ToUpperInvariant(), It.IsAny<Series>(), It.IsAny<List<Episode>>(), It.IsAny<QualityModel>(), It.IsAny<string>(), _files))
                .Returns(map);

            Mocker.GetMock<IEngineLinkService>().Setup(s => s.Save(Hash.ToUpperInvariant(), link)).Callback(() => order.Add("save"));
            _proxy.Setup(p => p.SetLocation(Hash, "/tv/Show/Season 01", _settings)).Callback(() => order.Add("location"));
            _proxy.Setup(p => p.RenameFile(Hash, It.IsAny<string>(), It.IsAny<string>(), _settings)).Callback(() => order.Add("rename"));
            _proxy.Setup(p => p.StartTorrent(Hash, _settings)).Callback(() => order.Add("start"));

            Subject.Prepare(GivenClient());

            order.Should().Equal("save", "location", "rename", "rename", "start");
            link.Files.Should().BeSameAs(map.Files);
            link.SavePath.Should().Be("/tv/Show/Season 01");
            _proxy.Verify(p => p.RenameFile(Hash, "Show.S01/Show.S01E01.mkv", ".Show - S01E01.mkv.pending", _settings), Times.Once());
            _proxy.Verify(p => p.SetFilePriority(Hash, It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 2, 3 })), 0, _settings), Times.Once());
        }

        [Test]
        public void should_reuse_a_saved_mapping_and_skip_renames_already_done()
        {
            var link = new EngineLink
            {
                SavePath = "/tv/Show/Season 01",
                Files = new List<EngineLinkFile> { new EngineLinkFile { Index = 0, FinalPath = "Show - S01E01.mkv", PendingPath = ".Show - S01E01.mkv.pending" } }
            };

            _files[0].Name = ".Show - S01E01.mkv.pending";
            Mocker.GetMock<IEngineLinkService>().Setup(s => s.Get(Hash.ToUpperInvariant())).Returns(link);

            Subject.Prepare(GivenClient());

            Mocker.GetMock<IEngineFileMapper>().Verify(s => s.Map(It.IsAny<string>(), It.IsAny<Series>(), It.IsAny<List<Episode>>(), It.IsAny<QualityModel>(), It.IsAny<string>(), It.IsAny<List<QBittorrentTorrentFile>>()), Times.Never());
            _proxy.Verify(p => p.RenameFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _proxy.Verify(p => p.StartTorrent(Hash, _settings), Times.Once());
        }

        [Test]
        public void should_reject_when_no_video_matches_the_grabbed_episodes()
        {
            Mocker.GetMock<IEngineLinkService>().Setup(s => s.Get(Hash.ToUpperInvariant())).Returns(new EngineLink());
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(It.IsAny<int>())).Returns(new Series { Id = 1 });
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(1)).Returns(new List<Episode> { new Episode { Id = 0 } });

            Subject.Prepare(GivenClient());

            Mocker.GetMock<IFailedDownloadService>()
                .Verify(s => s.MarkAsFailed(12, EngineTorrentPreparationService.NoVideoMessage, "Embarr", false), Times.Once());
            _proxy.Verify(p => p.StartTorrent(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_set_priorities_when_every_file_is_wanted()
        {
            _files.RemoveRange(2, 2);

            Subject.Prepare(GivenClient());

            _proxy.Verify(p => p.SetFilePriority(It.IsAny<string>(), It.IsAny<IEnumerable<int>>(), It.IsAny<int>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _proxy.Verify(p => p.StartTorrent(Hash, _settings), Times.Once());
        }

        [Test]
        public void should_reject_torrent_without_video()
        {
            _files = new List<QBittorrentTorrentFile>
            {
                new QBittorrentTorrentFile { Name = "Show.S01/Show.S01E01.rar" },
                new QBittorrentTorrentFile { Name = "Show.S01/Show.S01E01.r00" }
            };

            Subject.Prepare(GivenClient());

            Mocker.GetMock<IFailedDownloadService>()
                .Verify(s => s.MarkAsFailed(12, EngineTorrentPreparationService.NoVideoMessage, "Embarr", false), Times.Once());
            _proxy.Verify(p => p.RemoveTorrent(Hash, true, _settings), Times.Once());
            _proxy.Verify(p => p.StartTorrent(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("metaDL", true, "embarr-held")]
        [TestCase("stoppedDL", false, "embarr-held")]
        [TestCase("downloading", true, "embarr-held")]
        [TestCase("stoppedDL", true, "embarr-held,embarr-prepared")]
        [TestCase("stoppedDL", true, "")]
        [TestCase("stoppedUP", true, "other")]
        public void should_leave_torrents_that_are_not_held_for_preparation(string state, bool hasMetadata, string tags)
        {
            _torrent.State = state;
            _torrent.HasMetadata = hasMetadata;
            _torrent.Tags = tags;

            Subject.Prepare(GivenClient());

            _proxy.Verify(p => p.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _proxy.Verify(p => p.StartTorrent(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_leave_torrents_embarr_did_not_grab()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(Hash.ToUpperInvariant()))
                .Returns(new List<EpisodeHistory>());

            Subject.Prepare(GivenClient());

            _proxy.Verify(p => p.StartTorrent(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_only_retry_removal_once_no_video_failure_is_recorded()
        {
            _files = new List<QBittorrentTorrentFile> { new QBittorrentTorrentFile { Name = "Show.S01/Show.S01E01.rar" } };

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem(Hash.ToUpperInvariant()))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadFailed });

            Subject.Prepare(GivenClient());

            Mocker.GetMock<IFailedDownloadService>()
                .Verify(s => s.MarkAsFailed(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            _proxy.Verify(p => p.RemoveTorrent(Hash, true, _settings), Times.Once());
        }

        [Test]
        public void should_keep_going_when_one_torrent_fails()
        {
            var second = new QBittorrentTorrent { Hash = "1111111111111111111111111111111111111111", Name = "Other", State = "stoppedDL", HasMetadata = true, Tags = EngineTorrentTags.Held };

            _proxy.Setup(p => p.GetTorrents(_settings)).Returns(new List<QBittorrentTorrent> { second, _torrent });
            _proxy.Setup(p => p.GetTorrentFiles(second.Hash, _settings)).Throws(new InvalidOperationException("engine error"));

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(second.Hash.ToUpperInvariant()))
                .Returns(new List<EpisodeHistory> { new EpisodeHistory { Id = 13, EventType = EpisodeHistoryEventType.Grabbed } });

            Subject.Prepare(GivenClient());

            _proxy.Verify(p => p.StartTorrent(Hash, _settings), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
