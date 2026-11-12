using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class IgnoredDownloadServiceFixture : CoreTest<IgnoredDownloadService>
    {
        private TrackedDownload GivenMissingWithoutEpisodes()
        {
            return new TrackedDownload
            {
                IsMissingFromEngine = true,
                DownloadClient = 3,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "AAA",
                    Title = "Series.S01E01",
                    DownloadClientInfo = new DownloadClientItemClientInfo { Id = 3, Name = "Cinnabarr Engine" }
                }
            };
        }

        [Test]
        public void should_dismiss_missing_row_without_episode_history_using_its_grab()
        {
            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestGrab("AAA"))
                .Returns(new DownloadHistory { DownloadId = "AAA", SeriesId = 7 });

            Subject.IgnoreDownload(GivenMissingWithoutEpisodes()).Should().BeTrue();

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.Is<DownloadIgnoredEvent>(m => m.SeriesId == 7 && m.DownloadId == "AAA" && m.EpisodeIds.Count == 0)), Times.Once());
        }

        [Test]
        public void should_not_ignore_download_for_unknown_series()
        {
            var trackedDownload = GivenMissingWithoutEpisodes();
            trackedDownload.IsMissingFromEngine = false;

            Subject.IgnoreDownload(trackedDownload).Should().BeFalse();

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<DownloadIgnoredEvent>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
