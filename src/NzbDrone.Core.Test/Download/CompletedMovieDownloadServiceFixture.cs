using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class CompletedMovieDownloadServiceFixture : CoreTest<CompletedMovieDownloadService>
    {
        private TrackedDownload _trackedDownload;
        private Movie _movie;

        [SetUp]
        public void SetUp()
        {
            _movie = new Movie { Id = 8, MovieFileId = 0, MovieMetadata = new MovieMetadata { Title = "The Matrix", Year = 1999 } };

            var item = Builder<DownloadClientItem>.CreateNew()
                .With(h => h.Status = DownloadItemStatus.Completed)
                .With(h => h.OutputPath = new OsPath(@"C:\DropFolder\The.Matrix.1999".AsOsAgnostic()))
                .With(h => h.Title = "The.Matrix.1999")
                .With(h => h.DownloadId = "HASH")
                .Build();

            _trackedDownload = new TrackedDownload
            {
                State = TrackedDownloadState.Downloading,
                DownloadItem = item,
                RemoteMovie = new RemoteMovie { Movie = _movie, Release = new ReleaseInfo { Title = "The.Matrix.1999" } }
            };

            Mocker.GetMock<IProvideImportItemService>()
                  .Setup(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                  .Returns<DownloadClientItem, DownloadClientItem>((current, _) => current);

            Mocker.GetMock<IMovieHistoryService>()
                  .Setup(s => s.FindByDownloadId("HASH"))
                  .Returns(new List<MovieHistory> { new() { MovieId = 8, EventType = MovieHistoryEventType.Grabbed, DownloadId = "HASH", SourceTitle = "The.Matrix.1999" } });
        }

        private void GivenImportResults(params ImportResult[] results)
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(s => s.ProcessPath(It.IsAny<string>(), ImportMode.Auto, _movie, It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>(results));
        }

        private static ImportResult Imported() => new(new ImportDecision(new LocalMovie { Path = "/drop/The.Matrix.1999.mkv" }));

        private static ImportResult Rejected(string reason) => new(new ImportDecision(new LocalMovie { Path = "/drop/The.Matrix.1999.mkv" }, new ImportRejection(ImportRejectionReason.Sample, reason)), reason);

        [Test]
        public void check_should_make_a_finished_download_ready_to_import()
        {
            Subject.Check(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        [Test]
        public void check_should_leave_an_unfinished_download_alone()
        {
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Downloading;

            Subject.Check(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);
        }

        [Test]
        public void check_should_warn_when_there_is_no_output_path()
        {
            _trackedDownload.DownloadItem.OutputPath = default;

            Subject.Check(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void import_should_mark_the_download_imported_and_publish_completed()
        {
            GivenImportResults(Imported());

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);

            Mocker.GetMock<IEventAggregator>().Verify(
                e => e.PublishEvent(It.Is<MovieDownloadCompletedEvent>(c => c.Movie == _movie && c.TrackedDownload == _trackedDownload && c.Release != null && c.Release.MovieIds.Contains(8))),
                Times.Once());
        }

        [Test]
        public void import_should_block_when_the_only_file_was_rejected()
        {
            GivenImportResults(Rejected("Sample"));

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportBlocked);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
            _trackedDownload.StatusMessages.Should().Contain(m => m.Messages.Contains("Sample"));
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<MovieDownloadCompletedEvent>()), Times.Never());
        }

        [Test]
        public void import_should_wait_when_there_is_nothing_to_import()
        {
            GivenImportResults();

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void import_should_treat_a_previously_imported_file_as_done()
        {
            Mocker.GetMock<IMovieHistoryService>()
                  .Setup(s => s.FindByDownloadId("HASH"))
                  .Returns(new List<MovieHistory>
                  {
                      new() { MovieId = 8, EventType = MovieHistoryEventType.DownloadFolderImported, DownloadId = "HASH" },
                      new() { MovieId = 8, EventType = MovieHistoryEventType.Grabbed, DownloadId = "HASH", SourceTitle = "The.Matrix.1999" }
                  });
            GivenImportResults(Rejected("Already imported"));

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        [Test]
        public void import_should_block_a_download_without_a_movie()
        {
            _trackedDownload.RemoteMovie = null;

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportBlocked);
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IDownloadedMovieImportService>().Verify(s => s.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }
    }
}
