using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.FailedDownloadServiceTests
{
    [TestFixture]
    public class MovieProcessFailedFixture : CoreTest<FailedDownloadService>
    {
        private const string DownloadId = "HASH";

        private TrackedDownload _trackedDownload;
        private MovieHistory _grab;

        [SetUp]
        public void SetUp()
        {
            _grab = new MovieHistory
            {
                Id = 31,
                MovieId = 8,
                EventType = MovieHistoryEventType.Grabbed,
                DownloadId = DownloadId,
                SourceTitle = "The.Matrix.1999.1080p.BluRay.x264-GRP",
                Quality = new QualityModel(Quality.Bluray1080p),
                Languages = new List<Language> { Language.English },
                Date = DateTime.UtcNow
            };

            _trackedDownload = new TrackedDownload
            {
                State = TrackedDownloadState.FailedPending,
                DownloadItem = new DownloadClientItem { DownloadId = DownloadId, Title = "The.Matrix", Status = DownloadItemStatus.Failed, Message = "No space left" }
            };

            Mocker.GetMock<IHistoryService>().Setup(s => s.Find(DownloadId, EpisodeHistoryEventType.Grabbed)).Returns(new List<EpisodeHistory>());
            Mocker.GetMock<IMovieHistoryService>().Setup(s => s.FindByDownloadId(DownloadId)).Returns(new List<MovieHistory> { _grab });
            Mocker.GetMock<IMovieHistoryService>().Setup(s => s.Get(31)).Returns(_grab);
        }

        [Test]
        public void should_publish_a_movie_failure_for_a_failed_movie_download()
        {
            Subject.ProcessFailed(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Failed);

            Mocker.GetMock<IEventAggregator>().Verify(
                e => e.PublishEvent(It.Is<MovieDownloadFailedEvent>(f => f.MovieId == 8 && f.DownloadId == DownloadId && f.Message == "No space left" && f.TrackedDownload == _trackedDownload)),
                Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
        }

        [Test]
        public void should_move_a_failed_movie_download_to_failed_pending_instead_of_warning()
        {
            _trackedDownload.State = TrackedDownloadState.Downloading;

            Subject.Check(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.FailedPending);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Ok);
        }

        [Test]
        public void should_warn_about_a_failed_download_with_no_grab_in_either_history()
        {
            _trackedDownload.State = TrackedDownloadState.Downloading;
            Mocker.GetMock<IMovieHistoryService>().Setup(s => s.FindByDownloadId(DownloadId)).Returns(new List<MovieHistory>());

            Subject.Check(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void should_publish_a_movie_failure_when_a_tracked_movie_download_is_marked_failed()
        {
            Subject.MarkAsFailed(_trackedDownload, "Rejected", "Embarr");

            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<MovieDownloadFailedEvent>(f => f.MovieId == 8 && f.Message == "Rejected" && f.Source == "Embarr")), Times.Once());
        }

        [Test]
        public void should_publish_a_movie_failure_when_a_movie_history_item_is_marked_failed()
        {
            Subject.MarkMovieAsFailed(31, null, "UI", skipRedownload: true);

            Mocker.GetMock<IEventAggregator>().Verify(
                e => e.PublishEvent(It.Is<MovieDownloadFailedEvent>(f => f.MovieId == 8 && f.Message == "Manually marked as failed" && f.SkipRedownload)),
                Times.Once());
        }
    }
}
