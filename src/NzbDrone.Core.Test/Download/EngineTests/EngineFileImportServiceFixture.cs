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
        private TrackedDownload _trackedDownload;

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

            _trackedDownload = new TrackedDownload { State = TrackedDownloadState.Downloading, DownloadItem = new DownloadClientItem { DownloadId = Hash.ToUpperInvariant() } };
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find(Hash.ToUpperInvariant())).Returns(() => _trackedDownload);

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

            Mocker.GetMock<IMakeImportDecision>().Verify(s => s.GetDecision(It.Is<LocalEpisode>(l => l.Path == _pending && l.Episodes.Count == 1 && l.InPlace), It.IsAny<DownloadClientItem>()), Times.Once());
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

            // The subtitle hadn't finished: it still has the engine's incomplete name.
            _existing.Remove(_subtitlePending);
            _existing.Add(_subtitlePending + ".!qB");

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetFilePriority(Hash, new[] { 0 }, 0, _settings), Times.Once());
            _proxy.Verify(p => p.SetFilePriority(Hash, new[] { 1 }, 0, _settings), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(_pending), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(_subtitlePending + ".!qB"), Times.Once());
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

            var trackedDownload = _trackedDownload;

            Subject.Process(GivenClient());

            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<DownloadCompletedEvent>(c => c.TrackedDownload == trackedDownload)), Times.Once());
            trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
            _link.Completed.Should().BeTrue();
        }

        [Test]
        public void should_check_done_torrents_once_then_skip_them_and_unmapped_ones()
        {
            _link.Completed = true;
            _link.Files.ForEach(f => f.State = EngineLinkFileState.Imported);
            Subject.Process(GivenClient());
            Subject.Process(GivenClient());

            _link = new EngineLink();
            Subject.Process(GivenClient());

            _proxy.Verify(p => p.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Once());
            Mocker.GetMock<IImportApprovedEpisodes>().Verify(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()), Times.Never());
        }

        [Test]
        public void should_record_again_a_file_the_library_lost()
        {
            // For example after a database restore: the link says imported, the file is there, the library has no file.
            _link.Completed = true;
            _link.Files.ForEach(f => f.State = EngineLinkFileState.Imported);
            _existing.Remove(_pending);
            _existing.Add(_final);

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedEpisodes>().Verify(s => s.Import(It.Is<List<ImportDecision>>(d => d[0].LocalEpisode.Path == _final), true, It.IsAny<DownloadClientItem>(), ImportMode.Move), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());
            _link.Completed.Should().BeTrue();
        }

        [Test]
        public void should_retry_removing_a_torrent_whose_videos_were_all_rejected()
        {
            _link.Completed = true;
            _link.Files.ForEach(f => f.State = EngineLinkFileState.Rejected);
            _trackedDownload.State = TrackedDownloadState.Failed;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RemoveTorrent(Hash, false, _settings), Times.Once());
            _proxy.Verify(p => p.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        private void GivenFailure(string message)
        {
            _trackedDownload.State = TrackedDownloadState.Failed;

            var failure = new EpisodeHistory { SeriesId = 1, EpisodeId = 11, DownloadId = Hash.ToUpperInvariant(), EventType = EpisodeHistoryEventType.DownloadFailed, Date = System.DateTime.UtcNow };
            failure.Data["Message"] = message;

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(Hash.ToUpperInvariant()))
                .Returns(new List<EpisodeHistory>
                {
                    new EpisodeHistory { Id = 5, SeriesId = 1, EpisodeId = 11, DownloadId = Hash.ToUpperInvariant(), EventType = EpisodeHistoryEventType.Grabbed, Date = System.DateTime.UtcNow.AddHours(-1) },
                    failure
                });
        }

        [Test]
        public void should_keep_importing_the_rest_of_a_torrent_after_one_file_was_rejected()
        {
            GivenFailure(EngineFileImportService.RejectedMessage);

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RenameFile(Hash, ".Show - S01E01.mkv.pending", "Show - S01E01.mkv", _settings), Times.Once());
        }

        [Test]
        public void should_search_again_only_once_per_grab()
        {
            GivenFailure(EngineFileImportService.RejectedMessage);
            _approve = false;

            Subject.Process(GivenClient());

            _link.Files[0].State.Should().Be(EngineLinkFileState.Rejected);
            Mocker.GetMock<IFailedDownloadService>().Verify(s => s.MarkAsFailed(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_leave_a_download_that_failed_for_another_reason()
        {
            GivenFailure("Manually marked as failed");

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_wait_instead_of_deleting_a_file_whose_rejection_can_pass_later()
        {
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(s => s.GetDecision(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalEpisode, DownloadClientItem>((l, d) => new ImportDecision(l, new ImportRejection(ImportRejectionReason.TitleTba, "Episode title is TBA")));

            Subject.Process(GivenClient());
            Subject.Process(GivenClient());

            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IFailedDownloadService>().Verify(s => s.MarkAsFailed(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Pending);

            // Logged once, not on every poll.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_wait_when_the_episodes_already_have_another_file()
        {
            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.GetEpisodeBySeries(1))
                .Returns(new List<Episode>
                {
                    new Episode { Id = 11, SeasonNumber = 1, EpisodeNumber = 1, EpisodeFileId = 7, EpisodeFile = new NzbDrone.Core.Datastore.LazyLoaded<NzbDrone.Core.MediaFiles.EpisodeFile>(new NzbDrone.Core.MediaFiles.EpisodeFile { Id = 7, RelativePath = Path.Combine("Season 01", "Show - S01E01 - Other.mkv") }) }
                });

            Subject.Process(GivenClient());

            Mocker.GetMock<IMakeImportDecision>().Verify(s => s.GetDecision(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _proxy.Verify(p => p.RenameFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Pending);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_check_the_file_with_the_grabbed_indexer_flags()
        {
            _trackedDownload.RemoteEpisode = new RemoteEpisode { Release = new ReleaseInfo { IndexerFlags = IndexerFlags.Freeleech } };

            Subject.Process(GivenClient());

            Mocker.GetMock<IMakeImportDecision>().Verify(s => s.GetDecision(It.Is<LocalEpisode>(l => l.IndexerFlags == IndexerFlags.Freeleech), It.IsAny<DownloadClientItem>()), Times.Once());
        }

        [Test]
        public void should_rename_to_the_final_name_rebuilt_after_the_checks()
        {
            Mocker.GetMock<IEngineFileMapper>()
                .Setup(s => s.UpdateFinalPaths(_link, _link.Files[0], It.IsAny<Series>(), It.IsAny<LocalEpisode>()))
                .Callback<EngineLink, EngineLinkFile, Series, LocalEpisode>((l, f, s, e) => f.FinalPath = "Show - S01E01 [x265].mkv")
                .Returns(true);

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RenameFile(Hash, ".Show - S01E01.mkv.pending", "Show - S01E01 [x265].mkv", _settings), Times.Once());
        }

        [Test]
        public void should_recover_a_subtitle_rename_whose_state_was_not_saved()
        {
            _link.Completed = true;
            _link.Files[0].State = EngineLinkFileState.Imported;
            _files[1].Name = "Show - S01E01.en.srt";
            _existing.Remove(_subtitlePending);
            _existing.Add(Path.Combine(_link.SavePath, "Show - S01E01.en.srt"));

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RenameFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _link.Files[1].State.Should().Be(EngineLinkFileState.Imported);
        }

        [TestCase(TrackedDownloadState.Ignored)]
        [TestCase(TrackedDownloadState.Failed)]
        [TestCase(TrackedDownloadState.FailedPending)]
        public void should_leave_downloads_that_were_ignored_or_failed(TrackedDownloadState state)
        {
            _trackedDownload.State = state;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_wait_until_the_download_is_tracked()
        {
            _trackedDownload = null;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_not_import_again_a_file_already_recorded()
        {
            _link.Files[0].State = EngineLinkFileState.Renamed;
            _existing.Remove(_pending);
            _existing.Add(_final);

            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.GetEpisodeBySeries(1))
                .Returns(new List<Episode>
                {
                    new Episode { Id = 11, SeasonNumber = 1, EpisodeNumber = 1, EpisodeFileId = 3, EpisodeFile = new NzbDrone.Core.Datastore.LazyLoaded<NzbDrone.Core.MediaFiles.EpisodeFile>(new NzbDrone.Core.MediaFiles.EpisodeFile { Id = 3, RelativePath = Path.Combine("Season 01", "Show - S01E01.mkv") }) }
                });

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedEpisodes>().Verify(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Imported);
        }

        [Test]
        public void should_not_force_a_renamed_file_in_when_its_checks_fail()
        {
            _link.Files[0].State = EngineLinkFileState.Renamed;
            _existing.Remove(_pending);
            _existing.Add(_final);
            _approve = false;

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedEpisodes>().Verify(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(It.IsAny<string>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Renamed);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_recover_a_rename_whose_state_was_not_saved()
        {
            _files[0].Name = "Show - S01E01.mkv";
            _existing.Remove(_pending);
            _existing.Add(_final);

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RenameFile(Hash, ".Show - S01E01.mkv.pending", It.IsAny<string>(), _settings), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Imported);
        }

        [Test]
        public void should_wait_while_a_mapped_episode_is_missing_from_the_library()
        {
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(1)).Returns(new List<Episode>());

            Subject.Process(GivenClient());

            Mocker.GetMock<IMakeImportDecision>().Verify(s => s.GetDecision(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Pending);
        }

        [Test]
        public void should_publish_a_subtitle_that_finishes_after_the_download_is_recorded()
        {
            _link.Completed = true;
            _link.Files[0].State = EngineLinkFileState.Imported;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RenameFile(Hash, ".Show - S01E01.en.srt.pending", "Show - S01E01.en.srt", _settings), Times.Once());
            _link.Files[1].State.Should().Be(EngineLinkFileState.Imported);
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());
        }
    }
}
