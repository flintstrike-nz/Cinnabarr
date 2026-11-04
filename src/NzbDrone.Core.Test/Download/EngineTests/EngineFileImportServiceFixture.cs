using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private List<Episode> _seriesEpisodes;

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
            _seriesEpisodes = new List<Episode> { new Episode { Id = 11, SeasonNumber = 1, EpisodeNumber = 1 } };
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(1)).Returns(() => _seriesEpisodes);
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodes(It.IsAny<IEnumerable<int>>())).Returns(() => _seriesEpisodes);

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns<string>(p => _existing.Contains(p));
            Mocker.GetMock<IAggregationService>().Setup(s => s.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>())).Returns<LocalEpisode, DownloadClientItem>((l, d) => l);

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(s => s.GetDecision(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalEpisode, DownloadClientItem>((l, d) => _approve ? new ImportDecision(l) : new ImportDecision(l, new ImportRejection(ImportRejectionReason.Sample, "Sample")));

            Mocker.GetMock<IImportApprovedEpisodes>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), true, It.IsAny<DownloadClientItem>(), ImportMode.Move))
                .Returns<List<ImportDecision>, bool, DownloadClientItem, ImportMode>((d, n, i, m) =>
                {
                    GivenVideoRecorded();
                    return new List<ImportResult> { new ImportResult(d[0]) };
                });
        }

        // The video's file is recorded on its episode, at its final path.
        private void GivenVideoRecorded()
        {
            _seriesEpisodes = new List<Episode>
            {
                new Episode { Id = 11, SeasonNumber = 1, EpisodeNumber = 1, EpisodeFileId = 30, EpisodeFile = new NzbDrone.Core.Datastore.LazyLoaded<NzbDrone.Core.MediaFiles.EpisodeFile>(new NzbDrone.Core.MediaFiles.EpisodeFile { Id = 30, RelativePath = Path.Combine("Season 01", "Show - S01E01.mkv") }) }
            };
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

            // The subtitle is published once the engine has renamed it; until then the download isn't completed.
            _link.Files[1].State.Should().Be(EngineLinkFileState.Renamed);
            _link.Completed.Should().BeFalse();

            GivenSubtitleRenamedByEngine();
            Subject.Process(GivenClient());

            _link.Files[1].State.Should().Be(EngineLinkFileState.Imported);
            _link.Completed.Should().BeTrue();
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());
            _proxy.Verify(p => p.RemoveTorrent(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        private void GivenSubtitleRenamedByEngine()
        {
            _files[1].Name = "Show - S01E01.en.srt";
            _existing.Remove(_subtitlePending);
            _existing.Add(Path.Combine(_link.SavePath, "Show - S01E01.en.srt"));
        }

        [Test]
        public void should_record_a_published_subtitle_with_its_video_file_once()
        {
            GivenVideoRecorded();
            _link.Completed = true;
            _link.Files[0].State = EngineLinkFileState.Imported;
            _link.Files[1].State = EngineLinkFileState.Renamed;
            _link.Files[1].LanguageId = NzbDrone.Core.Languages.Language.English.Id;
            _link.Files[1].LanguageTags = new List<string> { "forced" };
            GivenSubtitleRenamedByEngine();

            Subject.Process(GivenClient());

            Mocker.GetMock<NzbDrone.Core.Extras.Subtitles.ISubtitleFileService>()
                .Verify(s => s.Upsert(It.Is<NzbDrone.Core.Extras.Subtitles.SubtitleFile>(f => IsRecordedSubtitle(f))), Times.Once());
            _link.Files[1].State.Should().Be(EngineLinkFileState.Imported);
        }

        private static bool IsRecordedSubtitle(NzbDrone.Core.Extras.Subtitles.SubtitleFile file)
        {
            return file.EpisodeFileId == 30 &&
                   file.SeriesId == 1 &&
                   file.SeasonNumber == 1 &&
                   file.RelativePath == Path.Combine("Season 01", "Show - S01E01.en.srt") &&
                   file.Language == NzbDrone.Core.Languages.Language.English &&
                   file.LanguageTags.Single() == "forced";
        }

        [Test]
        public void should_record_a_subtitle_of_an_older_link_with_the_language_its_name_gives()
        {
            // Mapped before subtitles carried their language: it is read back from the name they were given.
            GivenVideoRecorded();
            _link.Completed = true;
            _link.Files[0].State = EngineLinkFileState.Imported;
            _link.Files[1].State = EngineLinkFileState.Renamed;
            _link.Files[1].FinalPath = "Show - S01E01.en.forced.srt";
            _files[1].Name = "Show - S01E01.en.forced.srt";
            _existing.Remove(_subtitlePending);
            _existing.Add(Path.Combine(_link.SavePath, "Show - S01E01.en.forced.srt"));

            Subject.Process(GivenClient());

            Mocker.GetMock<NzbDrone.Core.Extras.Subtitles.ISubtitleFileService>()
                .Verify(s => s.Upsert(It.Is<NzbDrone.Core.Extras.Subtitles.SubtitleFile>(f => f.Language == NzbDrone.Core.Languages.Language.English && f.LanguageTags.Single() == "forced")), Times.Once());
        }

        [Test]
        public void should_record_again_published_subtitles_the_library_lost()
        {
            GivenVideoRecorded();
            _link.Completed = true;
            _link.Files.ForEach(f => f.State = EngineLinkFileState.Imported);
            _existing.Remove(_pending);
            _existing.Remove(_subtitlePending);
            _existing.Add(_final);
            _existing.Add(Path.Combine(_link.SavePath, "Show - S01E01.en.srt"));

            Subject.Process(GivenClient());

            Mocker.GetMock<NzbDrone.Core.Extras.Subtitles.ISubtitleFileService>()
                .Verify(s => s.Upsert(It.Is<NzbDrone.Core.Extras.Subtitles.SubtitleFile>(f => f.RelativePath == Path.Combine("Season 01", "Show - S01E01.en.srt"))), Times.Once());
        }

        [Test]
        public void should_record_published_subtitles_with_the_file_their_video_was_just_recorded_as()
        {
            // A restore left the episode pointing at the file this one replaced: the subtitle is recorded with the
            // new file, read again after the import.
            GivenExistingFile(7, "Show - S01E01 - Other.mkv");
            _link.Files[0].State = EngineLinkFileState.Renamed;
            _link.Files[0].ReplacesFileIds = new List<int> { 7 };
            _link.Files[1].State = EngineLinkFileState.Imported;
            _existing.Remove(_pending);
            _existing.Remove(_subtitlePending);
            _existing.Add(_final);
            _existing.Add(Path.Combine(_link.SavePath, "Show - S01E01.en.srt"));

            Subject.Process(GivenClient());

            Mocker.GetMock<NzbDrone.Core.Extras.Subtitles.ISubtitleFileService>()
                .Verify(s => s.Upsert(It.Is<NzbDrone.Core.Extras.Subtitles.SubtitleFile>(f => f.EpisodeFileId == 30)), Times.Once());
        }

        [Test]
        public void should_not_record_a_subtitle_twice()
        {
            GivenVideoRecorded();
            _link.Completed = true;
            _link.Files[0].State = EngineLinkFileState.Imported;
            _link.Files[1].State = EngineLinkFileState.Renamed;
            GivenSubtitleRenamedByEngine();

            Mocker.GetMock<NzbDrone.Core.Extras.Subtitles.ISubtitleFileService>()
                .Setup(s => s.FindByPath(1, Path.Combine("Season 01", "Show - S01E01.en.srt")))
                .Returns(new NzbDrone.Core.Extras.Subtitles.SubtitleFile());

            Subject.Process(GivenClient());

            Mocker.GetMock<NzbDrone.Core.Extras.Subtitles.ISubtitleFileService>().Verify(s => s.Upsert(It.IsAny<NzbDrone.Core.Extras.Subtitles.SubtitleFile>()), Times.Never());
            _link.Files[1].State.Should().Be(EngineLinkFileState.Imported);
        }

        [Test]
        public void should_discard_a_subtitle_whose_name_is_taken_so_it_does_not_hold_the_download_back()
        {
            GivenVideoRecorded();
            _link.Completed = true;
            _link.Files[0].State = EngineLinkFileState.Imported;
            _existing.Add(Path.Combine(_link.SavePath, "Show - S01E01.en.srt"));

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RenameFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _proxy.Verify(p => p.SetFilePriority(Hash, new[] { 1 }, 0, _settings), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(_subtitlePending), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(Path.Combine(_link.SavePath, "Show - S01E01.en.srt")), Times.Never());
            _link.Files[1].State.Should().Be(EngineLinkFileState.Rejected);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_discard_only_a_subtitle_that_fails_its_checks()
        {
            GivenVideoRecorded();
            _link.Completed = true;
            _link.Files[0].State = EngineLinkFileState.Imported;

            Mocker.GetMock<IEngineSubtitleValidator>()
                .Setup(s => s.Validate(_subtitlePending, "Show - S01E01.en.srt"))
                .Returns("it is empty");

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetFilePriority(Hash, new[] { 1 }, 0, _settings), Times.Once());
            _proxy.Verify(p => p.RenameFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(_subtitlePending), Times.Once());
            Mocker.GetMock<IFailedDownloadService>().Verify(s => s.MarkAsFailed(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            _link.Files[1].State.Should().Be(EngineLinkFileState.Rejected);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_ask_again_for_a_subtitle_rename_the_engine_did_not_carry_out()
        {
            _link.Completed = true;
            _link.Files[0].State = EngineLinkFileState.Imported;
            _link.Files[1].State = EngineLinkFileState.Renamed;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RenameFile(Hash, ".Show - S01E01.en.srt.pending", "Show - S01E01.en.srt", _settings), Times.Once());
            _link.Files[1].State.Should().Be(EngineLinkFileState.Renamed);
        }

        [Test]
        public void should_search_again_once_when_several_files_are_rejected_in_one_poll()
        {
            var savePath = _link.SavePath;
            _link.Files.Add(new EngineLinkFile { Index = 2, FinalPath = "Show - S01E02.mkv", PendingPath = ".Show - S01E02.mkv.pending", Episodes = new List<EngineLinkEpisode> { new EngineLinkEpisode { SeasonNumber = 1, EpisodeNumber = 2 } } });
            _files.Add(new QBittorrentTorrentFile { Name = ".Show - S01E02.mkv.pending", Progress = 1.0 });
            _existing.Add(Path.Combine(savePath, ".Show - S01E02.mkv.pending"));

            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.GetEpisodeBySeries(1))
                .Returns(new List<Episode> { new Episode { Id = 11, SeasonNumber = 1, EpisodeNumber = 1 }, new Episode { Id = 12, SeasonNumber = 1, EpisodeNumber = 2 } });

            _approve = false;

            Subject.Process(GivenClient());

            _link.Files.Where(f => !f.IsSubtitle).Should().OnlyContain(f => f.State == EngineLinkFileState.Rejected);
            Mocker.GetMock<IFailedDownloadService>().Verify(s => s.MarkAsFailed(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once());
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void should_check_a_file_of_a_pack_with_its_name_in_the_torrent()
        {
            _link.Files[0].OriginalName = "Show.S01E01.REPACK.720p.HDTV-Other.mkv";
            _link.Files.Add(new EngineLinkFile { Index = 2, FinalPath = "Show - S01E02.mkv", PendingPath = ".Show - S01E02.mkv.pending", Episodes = new List<EngineLinkEpisode> { new EngineLinkEpisode { SeasonNumber = 1, EpisodeNumber = 2 } } });

            Subject.Process(GivenClient());

            Mocker.GetMock<IMakeImportDecision>().Verify(s => s.GetDecision(It.Is<LocalEpisode>(l => IsCheckedWithTorrentName(l)), It.IsAny<DownloadClientItem>()), Times.Once());
        }

        private static bool IsCheckedWithTorrentName(LocalEpisode localEpisode)
        {
            return localEpisode.FileEpisodeInfo.ReleaseGroup == "Other" &&
                   localEpisode.FileEpisodeInfo.Quality.Revision.IsRepack &&
                   localEpisode.SceneName == "Show.S01E01.REPACK.720p.HDTV-Other";
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

        private void GivenExistingFile(int id, string name)
        {
            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.GetEpisodeBySeries(1))
                .Returns(new List<Episode>
                {
                    new Episode { Id = 11, SeasonNumber = 1, EpisodeNumber = 1, EpisodeFileId = id, EpisodeFile = new NzbDrone.Core.Datastore.LazyLoaded<NzbDrone.Core.MediaFiles.EpisodeFile>(new NzbDrone.Core.MediaFiles.EpisodeFile { Id = id, RelativePath = Path.Combine("Season 01", name) }) }
                });
        }

        [Test]
        public void should_detach_the_files_an_upgrade_replaces_before_renaming_it()
        {
            GivenExistingFile(7, "Show - S01E01 - Other.mkv");

            Mocker.GetMock<IEngineFileReplacementService>()
                .Setup(s => s.FindHolders(Hash.ToUpperInvariant(), It.IsAny<Series>(), It.Is<List<NzbDrone.Core.MediaFiles.EpisodeFile>>(f => f.Single().Id == 7)))
                .Returns(new List<string> { "OWNER" });

            // The torrents to recheck are saved before anything is detached.
            Mocker.GetMock<IEngineFileReplacementService>()
                .Setup(s => s.Detach(_proxy.Object, _settings, Hash.ToUpperInvariant(), It.IsAny<Series>(), It.IsAny<List<NzbDrone.Core.MediaFiles.EpisodeFile>>(), _final))
                .Callback(() => _link.Files[0].DetachedFrom.Should().Equal("OWNER"));

            Subject.Process(GivenClient());

            Mocker.GetMock<IEngineFileReplacementService>().Verify(s => s.Detach(_proxy.Object, _settings, Hash.ToUpperInvariant(), It.IsAny<Series>(), It.IsAny<List<NzbDrone.Core.MediaFiles.EpisodeFile>>(), _final), Times.Once());
            _proxy.Verify(p => p.RenameFile(Hash, ".Show - S01E01.mkv.pending", "Show - S01E01.mkv", _settings), Times.Once());
            _link.Files[0].ReplacesFileIds.Should().Equal(7);
            _link.Files[0].DetachedFrom.Should().Equal("OWNER");
            _link.Files[0].State.Should().Be(EngineLinkFileState.Renamed);
        }

        [Test]
        public void should_not_detach_anything_while_an_unrelated_file_holds_the_final_path()
        {
            GivenExistingFile(7, "Show - S01E01 - Other.mkv");
            _existing.Add(_final);

            Subject.Process(GivenClient());

            Mocker.GetMock<IEngineFileReplacementService>().Verify(s => s.Detach(It.IsAny<IQBittorrentProxy>(), It.IsAny<QBittorrentSettings>(), It.IsAny<string>(), It.IsAny<Series>(), It.IsAny<List<NzbDrone.Core.MediaFiles.EpisodeFile>>(), It.IsAny<string>()), Times.Never());
            _proxy.Verify(p => p.RenameFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Pending);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_wait_while_another_download_is_replacing_the_same_file()
        {
            GivenExistingFile(7, "Show - S01E01 - Other.mkv");

            Mocker.GetMock<IEngineFileReplacementService>()
                .Setup(s => s.IsReserved(_proxy.Object, _settings, Hash.ToUpperInvariant(), _final, It.IsAny<List<NzbDrone.Core.MediaFiles.EpisodeFile>>()))
                .Returns(true);

            Subject.Process(GivenClient());

            Mocker.GetMock<IEngineFileReplacementService>().Verify(s => s.Detach(It.IsAny<IQBittorrentProxy>(), It.IsAny<QBittorrentSettings>(), It.IsAny<string>(), It.IsAny<Series>(), It.IsAny<List<NzbDrone.Core.MediaFiles.EpisodeFile>>(), It.IsAny<string>()), Times.Never());
            _proxy.Verify(p => p.RenameFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Pending);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_record_again_an_upgrade_whose_record_a_restore_lost()
        {
            // A restored database still has the replaced file's record at the same path.
            GivenExistingFile(3, "Show - S01E01.mkv");
            _link.Completed = true;
            _link.Files.ForEach(f => f.State = EngineLinkFileState.Imported);
            _link.Files[0].ReplacesFileIds = new List<int> { 3 };
            _existing.Remove(_pending);
            _existing.Add(_final);

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedEpisodes>().Verify(s => s.Import(It.Is<List<ImportDecision>>(d => d[0].LocalEpisode.Path == _final), true, It.IsAny<DownloadClientItem>(), ImportMode.Move), Times.Once());
        }

        [Test]
        public void should_retry_a_recheck_that_did_not_happen()
        {
            _link.Completed = true;
            _link.Files.ForEach(f => f.State = EngineLinkFileState.Imported);
            _link.Files[0].DetachedFrom = new List<string> { "OWNER" };

            Subject.Process(GivenClient());

            Mocker.GetMock<IEngineFileReplacementService>().Verify(s => s.Recheck(_proxy.Object, _settings, It.Is<IEnumerable<string>>(h => h.Single() == "OWNER")), Times.Once());
            _link.Files[0].DetachedFrom.Should().BeNull();
        }

        [Test]
        public void should_not_detach_anything_when_the_upgrade_checks_fail()
        {
            GivenExistingFile(7, "Show - S01E01 - Other.mkv");

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(s => s.GetDecision(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalEpisode, DownloadClientItem>((l, d) => new ImportDecision(l, new ImportRejection(ImportRejectionReason.NotQualityUpgrade, "Not an upgrade")));

            Subject.Process(GivenClient());

            Mocker.GetMock<IEngineFileReplacementService>().Verify(s => s.Detach(It.IsAny<IQBittorrentProxy>(), It.IsAny<QBittorrentSettings>(), It.IsAny<string>(), It.IsAny<Series>(), It.IsAny<List<NzbDrone.Core.MediaFiles.EpisodeFile>>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(It.IsAny<string>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Pending);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_record_an_upgrade_that_took_the_name_of_the_file_it_replaces()
        {
            // The replaced file had the same final path: its record must not count as this file being recorded.
            GivenExistingFile(3, "Show - S01E01.mkv");
            _link.Files[0].State = EngineLinkFileState.Renamed;
            _link.Files[0].ReplacesFileIds = new List<int> { 3 };
            _link.Files[0].DetachedFrom = new List<string> { "OWNER" };
            _existing.Remove(_pending);
            _existing.Add(_final);

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedEpisodes>().Verify(s => s.Import(It.Is<List<ImportDecision>>(d => d[0].LocalEpisode.Path == _final), true, It.IsAny<DownloadClientItem>(), ImportMode.Move), Times.Once());
            Mocker.GetMock<IEngineFileReplacementService>().Verify(s => s.Recheck(_proxy.Object, _settings, It.Is<IEnumerable<string>>(h => h.Single() == "OWNER")), Times.Once());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Imported);
            _link.Files[0].DetachedFrom.Should().BeNull();
        }

        [Test]
        public void should_wait_when_another_file_was_recorded_after_the_rename()
        {
            GivenExistingFile(7, "Show - S01E01 - Other.mkv");
            _link.Files[0].State = EngineLinkFileState.Renamed;
            _existing.Remove(_pending);
            _existing.Add(_final);

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedEpisodes>().Verify(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Renamed);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_remove_a_torrent_whose_videos_were_all_replaced()
        {
            _link.Completed = true;
            _link.Files.ForEach(f => f.State = EngineLinkFileState.Replaced);

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RemoveTorrent(Hash, false, _settings), Times.Once());
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
            GivenVideoRecorded();
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
            GivenVideoRecorded();
            _link.Completed = true;
            _link.Files[0].State = EngineLinkFileState.Imported;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RenameFile(Hash, ".Show - S01E01.en.srt.pending", "Show - S01E01.en.srt", _settings), Times.Once());
            _link.Files[1].State.Should().Be(EngineLinkFileState.Renamed);

            GivenSubtitleRenamedByEngine();
            Subject.Process(GivenClient());

            _link.Files[1].State.Should().Be(EngineLinkFileState.Imported);
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());
        }
    }
}
