using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadHistoryTests
{
    [TestFixture]
    public class DownloadHistoryServiceFixture : CoreTest<DownloadHistoryService>
    {
        private const int ClientId = 3;

        [Test]
        public void should_record_failure_marked_from_history_against_the_grab()
        {
            var grab = new DownloadHistory { DownloadId = "AAA", EventType = DownloadHistoryEventType.DownloadGrabbed, DownloadClientId = ClientId, Protocol = DownloadProtocol.Torrent };

            Mocker.GetMock<IDownloadHistoryRepository>()
                .Setup(r => r.FindByDownloadId("AAA"))
                .Returns([grab]);

            Subject.Handle(new DownloadFailedEvent { DownloadId = "AAA", SeriesId = 5, SourceTitle = "Series.S01E01" });

            Mocker.GetMock<IDownloadHistoryRepository>()
                .Verify(r => r.Insert(It.Is<DownloadHistory>(h => h.EventType == DownloadHistoryEventType.DownloadFailed && h.DownloadId == "AAA" && h.DownloadClientId == ClientId)), Times.Once());
        }

        [Test]
        public void should_not_record_failure_marked_from_history_for_unknown_download()
        {
            Mocker.GetMock<IDownloadHistoryRepository>()
                .Setup(r => r.FindByDownloadId("AAA"))
                .Returns([]);

            Subject.Handle(new DownloadFailedEvent { DownloadId = "AAA", SeriesId = 5 });

            Mocker.GetMock<IDownloadHistoryRepository>()
                .Verify(r => r.Insert(It.IsAny<DownloadHistory>()), Times.Never());
        }
    }
}
