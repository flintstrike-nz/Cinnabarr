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
using NzbDrone.Core.Extras.MovieExtras;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineMovieFileImportServiceFixture : CoreTest<EngineMovieFileImportService>
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
        private Movie _movie;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IVersionSlotService>().Setup(s => s.GetView(It.IsAny<Movie>(), It.IsAny<int>())).Returns<Movie, int>((m, _) => m);
            _settings = new QBittorrentSettings();
            _proxy = new Mock<IQBittorrentProxy>();
            _approve = true;

            var savePath = @"C:\Movies\Alien (1979)".AsOsAgnostic();
            _pending = Path.Combine(savePath, ".Alien (1979) WEBDL-1080p.mkv.pending");
            _final = Path.Combine(savePath, "Alien (1979) WEBDL-1080p.mkv");
            _subtitlePending = Path.Combine(savePath, ".Alien (1979) WEBDL-1080p.en.srt.pending");
            _existing = new HashSet<string> { _pending, _subtitlePending };

            _files = new List<QBittorrentTorrentFile>
            {
                new QBittorrentTorrentFile { Name = ".Alien (1979) WEBDL-1080p.mkv.pending", Progress = 1.0 },
                new QBittorrentTorrentFile { Name = ".Alien (1979) WEBDL-1080p.en.srt.pending", Progress = 1.0 }
            };

            _link = new EngineLink
            {
                MediaType = EngineLinkService.MovieMediaType,
                SourceTitle = "Alien.1979.1080p.WEB-DL.x264-Grp",
                SavePath = savePath,
                Files = new List<EngineLinkFile>
                {
                    new EngineLinkFile { Index = 0, FinalPath = "Alien (1979) WEBDL-1080p.mkv", PendingPath = ".Alien (1979) WEBDL-1080p.mkv.pending" },
                    new EngineLinkFile { Index = 1, IsSubtitle = true, VideoIndex = 0, FinalPath = "Alien (1979) WEBDL-1080p.en.srt", PendingPath = ".Alien (1979) WEBDL-1080p.en.srt.pending" }
                }
            };

            _movie = new Movie { Id = 1, Path = savePath };

            _trackedDownload = new TrackedDownload { State = TrackedDownloadState.Downloading, DownloadItem = new DownloadClientItem { DownloadId = Hash.ToUpperInvariant() } };
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find(Hash.ToUpperInvariant())).Returns(() => _trackedDownload);

            _proxy.Setup(p => p.GetTorrents(_settings)).Returns(new List<QBittorrentTorrent> { new QBittorrentTorrent { Hash = Hash, Name = "Alien" } });
            _proxy.Setup(p => p.GetTorrentFiles(Hash, _settings)).Returns(() => _files);

            Mocker.GetMock<IQBittorrentProxySelector>().Setup(s => s.GetProxy(_settings, false)).Returns(_proxy.Object);
            Mocker.GetMock<IEngineLinkService>().Setup(s => s.Get(Hash.ToUpperInvariant())).Returns(() => _link);

            Mocker.GetMock<IMovieHistoryService>()
                .Setup(s => s.FindByDownloadId(Hash.ToUpperInvariant()))
                .Returns(new List<MovieHistory> { new MovieHistory { Id = 5, MovieId = 1, DownloadId = Hash.ToUpperInvariant(), EventType = MovieHistoryEventType.Grabbed } });

            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(1)).Returns(() => _movie);
            Mocker.GetMock<IMovieFileService>().Setup(s => s.GetMovie(It.IsAny<int>())).Returns<int>(id => _movie.MovieFile);

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns<string>(p => _existing.Contains(p));
            Mocker.GetMock<IAggregationService>().Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns<LocalMovie, DownloadClientItem>((l, d) => l);

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(s => s.GetDecision(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalMovie, DownloadClientItem>((l, d) => _approve ? new ImportDecision(l) : new ImportDecision(l, new ImportRejection(ImportRejectionReason.Sample, "Sample")));

            Mocker.GetMock<IImportApprovedMovie>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), true, It.IsAny<DownloadClientItem>(), ImportMode.Move))
                .Returns<List<ImportDecision>, bool, DownloadClientItem, ImportMode>((d, n, i, m) =>
                {
                    GivenVideoRecorded();
                    return new List<ImportResult> { new ImportResult(d[0]) };
                });
        }

        // The movie's file is recorded at the video's final path.
        private void GivenVideoRecorded()
        {
            GivenExistingFile(30, "Alien (1979) WEBDL-1080p.mkv");
        }

        private void GivenExistingFile(int id, string relativePath)
        {
            _movie = new Movie { Id = 1, Path = _movie.Path, MovieFileId = id, MovieFile = new MovieFile { Id = id, RelativePath = relativePath } };
        }

        private IDownloadClient GivenClient()
        {
            var client = new Mock<IDownloadClient>();
            client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 3, Settings = _settings });

            return client.Object;
        }

        private void GivenSubtitleRenamedByEngine()
        {
            _files[1].Name = "Alien (1979) WEBDL-1080p.en.srt";
            _existing.Remove(_subtitlePending);
            _existing.Add(Path.Combine(_link.SavePath, "Alien (1979) WEBDL-1080p.en.srt"));
        }

        [Test]
        public void should_check_a_completed_video_under_its_pending_name_then_rename_it()
        {
            Subject.Process(GivenClient());

            Mocker.GetMock<IMakeImportDecision>().Verify(s => s.GetDecision(It.Is<LocalMovie>(l => l.Path == _pending && l.Movie == _movie && l.InPlace), It.IsAny<DownloadClientItem>()), Times.Once());
            _proxy.Verify(p => p.RenameFile(Hash, ".Alien (1979) WEBDL-1080p.mkv.pending", "Alien (1979) WEBDL-1080p.mkv", _settings), Times.Once());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Renamed);

            // The subtitle waits for its video to be imported.
            _link.Files[1].State.Should().Be(EngineLinkFileState.Pending);
        }

        [Test]
        public void should_not_touch_a_video_that_is_still_downloading()
        {
            _files[0].Progress = 0.5;

            Subject.Process(GivenClient());

            Mocker.GetMock<IMakeImportDecision>().Verify(s => s.GetDecision(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Pending);
        }

        [Test]
        public void should_leave_a_link_that_is_not_for_a_movie_to_the_series_import()
        {
            _link.MediaType = EngineLinkService.SeriesMediaType;

            Subject.Process(GivenClient());

            Mocker.GetMock<IMakeImportDecision>().Verify(s => s.GetDecision(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()), Times.Never());
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
        public void should_delete_a_rejected_video_and_its_subtitle_and_search_again()
        {
            _approve = false;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetFilePriority(Hash, new[] { 0 }, 0, _settings), Times.Once());
            _proxy.Verify(p => p.SetFilePriority(Hash, new[] { 1 }, 0, _settings), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(_pending), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(_subtitlePending), Times.Once());
            Mocker.GetMock<IFailedDownloadService>().Verify(s => s.MarkMovieAsFailed(5, EngineFileImportService.RejectedMessage, "Cinnabarr", false), Times.Once());
            _link.Files.Should().OnlyContain(f => f.State == EngineLinkFileState.Rejected);

            // Nothing was imported: the empty torrent is removed without its data.
            _link.Completed.Should().BeTrue();
            _proxy.Verify(p => p.RemoveTorrent(Hash, false, _settings), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<MovieDownloadCompletedEvent>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_record_a_renamed_video_in_place_then_publish_and_record_its_subtitle()
        {
            _link.Files[0].State = EngineLinkFileState.Renamed;
            _existing.Remove(_pending);
            _existing.Add(_final);

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedMovie>()
                .Verify(s => s.Import(It.Is<List<ImportDecision>>(d => d[0].LocalMovie.Path == _final && d[0].LocalMovie.InPlace), true, It.IsAny<DownloadClientItem>(), ImportMode.Move), Times.Once());

            _link.Files[0].State.Should().Be(EngineLinkFileState.Imported);
            _proxy.Verify(p => p.RenameFile(Hash, ".Alien (1979) WEBDL-1080p.en.srt.pending", "Alien (1979) WEBDL-1080p.en.srt", _settings), Times.Once());

            // The subtitle is published once the engine has renamed it; until then the download isn't completed.
            _link.Files[1].State.Should().Be(EngineLinkFileState.Renamed);
            _link.Completed.Should().BeFalse();

            GivenSubtitleRenamedByEngine();
            Subject.Process(GivenClient());

            _link.Files[1].State.Should().Be(EngineLinkFileState.Imported);
            _link.Completed.Should().BeTrue();

            Mocker.GetMock<IMovieSubtitleFileService>()
                .Verify(s => s.Upsert(It.Is<MovieSubtitleFile>(f => f.MovieId == 1 && f.MovieFileId == 30 && f.RelativePath == "Alien (1979) WEBDL-1080p.en.srt" && f.Language == Language.English)), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<MovieDownloadCompletedEvent>(c => c.Movie == _movie && c.MovieFile.Id == 30)), Times.Once());
            _proxy.Verify(p => p.RemoveTorrent(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_not_record_a_video_the_library_already_has_as_its_file_again()
        {
            // The import finished but its state wasn't saved.
            GivenVideoRecorded();
            _link.Files[0].State = EngineLinkFileState.Renamed;
            _existing.Remove(_pending);
            _existing.Add(_final);

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedMovie>().Verify(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Imported);
        }

        [Test]
        public void should_not_replace_a_file_recorded_since_the_video_was_renamed()
        {
            GivenExistingFile(7, "Alien (1979) - Other.mkv");
            _link.Files[0].State = EngineLinkFileState.Renamed;
            _existing.Remove(_pending);
            _existing.Add(_final);

            Subject.Process(GivenClient());

            Mocker.GetMock<IImportApprovedMovie>().Verify(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Renamed);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_detach_the_file_an_upgrade_replaces_before_renaming()
        {
            GivenExistingFile(7, "Alien (1979) HDTV-720p.mkv");
            var holders = new List<string> { "OTHERHASH" };
            Mocker.GetMock<IEngineFileReplacementService>().Setup(s => s.FindHolders(Hash.ToUpperInvariant(), _movie, _movie.MovieFile)).Returns(holders);

            Subject.Process(GivenClient());

            Mocker.GetMock<IEngineFileReplacementService>().Verify(s => s.Detach(_proxy.Object, _settings, Hash.ToUpperInvariant(), _movie, _movie.MovieFile, _final), Times.Once());
            _link.Files[0].ReplacesFileIds.Should().BeEquivalentTo(new[] { 7 });
            _link.Files[0].DetachedFrom.Should().BeEquivalentTo(holders);
            _link.Files[0].State.Should().Be(EngineLinkFileState.Renamed);
        }

        [Test]
        public void should_wait_while_another_download_is_replacing_the_same_file()
        {
            GivenExistingFile(7, "Alien (1979) HDTV-720p.mkv");
            Mocker.GetMock<IEngineFileReplacementService>().Setup(s => s.IsReserved(_proxy.Object, _settings, Hash.ToUpperInvariant(), _final, _movie.MovieFile)).Returns(true);

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.RenameFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _link.Files[0].State.Should().Be(EngineLinkFileState.Pending);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_record_published_subtitles_the_library_lost()
        {
            GivenVideoRecorded();
            _link.Completed = true;
            _link.Files.ForEach(f => f.State = EngineLinkFileState.Imported);
            _existing.Remove(_pending);
            _existing.Remove(_subtitlePending);
            _existing.Add(_final);
            _existing.Add(Path.Combine(_link.SavePath, "Alien (1979) WEBDL-1080p.en.srt"));

            Subject.Process(GivenClient());

            Mocker.GetMock<IMovieSubtitleFileService>()
                .Verify(s => s.Upsert(It.Is<MovieSubtitleFile>(f => f.RelativePath == "Alien (1979) WEBDL-1080p.en.srt")), Times.Once());
        }
    }
}
