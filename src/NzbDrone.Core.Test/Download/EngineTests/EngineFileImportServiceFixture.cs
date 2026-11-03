using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineFileImportServiceFixture : CoreTest<EngineFileImportService>
    {
        private const string Hash = "abcdef0123456789abcdef0123456789abcdef01";

        private QBittorrentSettings _settings;
        private Mock<IQBittorrentProxy> _proxy;
        private List<QBittorrentTorrentFile> _files;
        private EngineLink _link;
        private HashSet<string> _existing;
        private string _pending;
        private string _final;
        private string _subtitlePending;
        private bool _approve;

        [SetUp]
        public void Setup()
        {
            _settings = new QBittorrentSettings();
            _proxy = new Mock<IQBittorrentProxy>();
            _approve = true;

            var savePath = @"C:\TV\Show\Season 01".AsOsAgnostic();
            _pending = Path.Combine(savePath, ".Show - S01E01.mkv.pending");
            _final = Path.Combine(savePath, "Show - S01E01.mkv");
            _subtitlePending = Path.Combine(savePath, ".Show - S01E01.en.srt.pending");
            _existing = new HashSet<string> { _pending, _subtitlePending };

            _files = new List<QBittorrentTorrentFile>
            {
                new QBittorrentTorrentFile { Name = ".Show - S01E01.mkv.pending", Progress = 1.0 },
                new QBittorrentTorrentFile { Name = ".Show - S01E01.en.srt.pending", Progress = 1.0 }
            };

            _link = new EngineLink
            {
                SourceTitle = "Show.S01E01.720p.HDTV-Grp",
                SavePath = savePath,
                Files = new List<EngineLinkFile>
                {
                    new EngineLinkFile { Index = 0, FinalPath = "Show - S01E01.mkv", PendingPath = ".Show - S01E01.mkv.pending", Episodes = new List<EngineLinkEpisode> { new EngineLinkEpisode { SeasonNumber = 1, EpisodeNumber = 1 } } },
                    new EngineLinkFile { Index = 1, IsSubtitle = true, VideoIndex = 0, FinalPath = "Show - S01E01.en.srt", PendingPath = ".Show - S01E01.en.srt.pending" }
                }
            };

            _proxy.Setup(p => p.GetTorrents(_settings)).Returns(new List<QBittorrentTorrent> { new QBittorrentTorrent { Hash = Hash, Name = "Show" } });
            _proxy.Setup(p => p.GetTorrentFiles(Hash, _settings)).Returns(() => _files);

            Mocker.GetMock<IQBittorrentProxySelector>().Setup(s => s.GetProxy(_settings, false)).Returns(_proxy.Object);
            Mocker.GetMock<IEngineLinkService>().Setup(s => s.Get(Hash.ToUpperInvariant())).Returns(() => _link);

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(Hash.ToUpperInvariant()))
                .Returns(new List<EpisodeHistory> { new EpisodeHistory { Id = 5, SeriesId = 1, EpisodeId = 11, DownloadId = Hash.ToUpperInvariant(), EventType = EpisodeHistoryEventType.Grabbed } });

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(1)).Returns(new Series { Id = 1, Path = @"C:\TV\Show".AsOsAgnostic() });
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(1)).Returns(new List<Episode> { new Episode { Id = 11, SeasonNumber = 1, EpisodeNumber = 1 } });
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodes(It.IsAny<IEnumerable<int>>())).Returns(new List<Episode>());

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns<string>(p => _existing.Contains(p));
            Mocker.GetMock<IAggregationService>().Setup(s => s.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>())).Returns<LocalEpisode, DownloadClientItem>((l, d) => l);

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(s => s.GetDecision(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalEpisode, DownloadClientItem>((l, d) => _approve ? new ImportDecision(l) : new ImportDecision(l, new ImportRejection(ImportRejectionReason.Sample, "Sample")));

            Mocker.GetMock<IImportApprovedEpisodes>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), true, It.IsAny<DownloadClientItem>(), ImportMode.Move))
                .Returns<List<ImportDecision>, bool, DownloadClientItem, ImportMode>((d, n, i, m) => new List<ImportResult> { new ImportResult(d[0]) });
        }

        private IDownloadClient GivenClient()
        {
            var client = new Mock<IDownloadClient>();
            client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 3, Settings = _settings });

            return client.Object;
        }

        [Test]
        public void should_check_a_completed_file_under_its_pending_name_then_rename_it()
        {
            Subject.Process(GivenClient());

            Mocker.GetMock<IMakeImportDecision>().Verify(s => s.GetDecision(It.Is<LocalEpisode>(l => l.Path == _pending && l.Episodes.Count == 1), It.IsAny<DownloadClientItem>()), Times.Once());
            _proxy.Verify(p => p.RenameFile(Hash, ".Show - S01E01.mkv.pending", "Show - S01E01.mkv", _settings), Times.Once());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Renamed);
            Mocker.GetMock<IEngineLinkService>().Verify(s => s.Save(Hash.ToUpperInvariant(), _link), Times.Once());

            // The subtitle waits for its video to be imported.
            _link.Files[1].State.Should().Be(EngineLinkFileState.Pending);
        }

        [Test]
        public void should_not_touch_files_that_are_still_downloading()
        {
            _files[0].Progress = 0.5;

            Subject.Process(GivenClient());

            Mocker.GetMock<IMakeImportDecision>().Verify(s => s.GetDecision(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Pending);
        }

        [Test]
        public void should_never_rename_over_an_existing_library_file()
        {
            _existing.Add(_final);

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RenameFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Pending);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_delete_a_rejected_file_and_its_subtitles_and_search_again()
        {
            _approve = false;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetFilePriority(Hash, new[] { 0 }, 0, _settings), Times.Once());
            _proxy.Verify(p => p.SetFilePriority(Hash, new[] { 1 }, 0, _settings), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(_pending), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(_subtitlePending), Times.Once());
            Mocker.GetMock<IFailedDownloadService>().Verify(s => s.MarkAsFailed(5, EngineFileImportService.RejectedMessage, "Embarr", false), Times.Once());
            _link.Files.Should().OnlyContain(f => f.State == EngineLinkFileState.Rejected);

            // Nothing was imported, so the download isn't recorded as imported, and the empty torrent is removed
            // without its data.
            _link.Completed.Should().BeTrue();
            _proxy.Verify(p => p.RemoveTorrent(Hash, false, _settings), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_record_a_renamed_file_in_place_then_publish_its_subtitle()
        {
            _link.Files[0].State = EngineLinkFileState.Renamed;
            _existing.Remove(_pending);
            _existing.Add(_final);

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedEpisodes>()
                .Verify(s => s.Import(It.Is<List<ImportDecision>>(d => d[0].LocalEpisode.Path == _final && d[0].LocalEpisode.InPlace), true, It.IsAny<DownloadClientItem>(), ImportMode.Move), Times.Once());

            _link.Files[0].State.Should().Be(EngineLinkFileState.Imported);
            _proxy.Verify(p => p.RenameFile(Hash, ".Show - S01E01.en.srt.pending", "Show - S01E01.en.srt", _settings), Times.Once());
            _link.Files[1].State.Should().Be(EngineLinkFileState.Imported);
            _link.Completed.Should().BeTrue();
            _proxy.Verify(p => p.RemoveTorrent(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_wait_for_the_engine_rename_to_reach_the_disk()
        {
            _link.Files[0].State = EngineLinkFileState.Renamed;

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedEpisodes>().Verify(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Renamed);
        }

        [Test]
        public void should_record_the_download_as_imported_once_every_video_is_done()
        {
            _link.Files[0].State = EngineLinkFileState.Imported;
            _link.Files[1].State = EngineLinkFileState.Imported;

            var trackedDownload = new TrackedDownload { DownloadItem = new DownloadClientItem { DownloadId = Hash.ToUpperInvariant() } };
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find(Hash.ToUpperInvariant())).Returns(trackedDownload);

            Subject.Process(GivenClient());

            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<DownloadCompletedEvent>(c => c.TrackedDownload == trackedDownload)), Times.Once());
            trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
            _link.Completed.Should().BeTrue();
        }

        [Test]
        public void should_skip_torrents_that_are_done_or_not_mapped()
        {
            _link.Completed = true;
            Subject.Process(GivenClient());

            _link = new EngineLink();
            Subject.Process(GivenClient());

            _proxy.Verify(p => p.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }
    }
}
