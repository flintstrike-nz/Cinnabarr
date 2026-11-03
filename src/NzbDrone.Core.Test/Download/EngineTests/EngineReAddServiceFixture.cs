using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineReAddServiceFixture : CoreTest<EngineReAddService>
    {
        private const string Hash = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";

        private TrackedDownload _trackedDownload;
        private DownloadHistory _grab;

        [SetUp]
        public void Setup()
        {
            _trackedDownload = new TrackedDownload
            {
                DownloadClient = 3,
                IsMissingFromEngine = true,
                DownloadItem = new DownloadClientItem { DownloadId = Hash, Title = "Series.S01E01.1080p.WEB-DL-Group" },
                RemoteEpisode = new RemoteEpisode
                {
                    Series = new Series { Id = 5 },
                    Episodes = new List<Episode> { new Episode { Id = 4 } },
                    ParsedEpisodeInfo = new ParsedEpisodeInfo()
                }
            };

            _grab = new DownloadHistory
            {
                Id = 10,
                DownloadId = Hash,
                SourceTitle = "Series.S01E01.1080p.WEB-DL-Group",
                Release = new ReleaseInfo { Title = "Series.S01E01.1080p.WEB-DL-Group", DownloadUrl = "http://indexer/download/1", IndexerId = 2, Indexer = "Indexer", Size = 1000 }
            };

            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find(Hash)).Returns(() => _trackedDownload);
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestGrab(Hash)).Returns(() => _grab);
        }

        private void GivenEngineRegrabsSameTorrent()
        {
            Mocker.GetMock<IDownloadService>()
                .Setup(s => s.DownloadReport(It.IsAny<RemoteEpisode>(), 3))
                .Callback(() => _grab = new DownloadHistory { Id = 11, DownloadId = Hash })
                .Returns(Task.CompletedTask);
        }

        [Test]
        public async Task should_send_stored_release_back_to_the_engine()
        {
            GivenEngineRegrabsSameTorrent();

            await Subject.ReAdd(Hash);

            Mocker.GetMock<IDownloadService>()
                .Verify(s => s.DownloadReport(It.Is<RemoteEpisode>(r => r.Series.Id == 5 &&
                                                                        r.Release is TorrentInfo &&
                                                                        ((TorrentInfo)r.Release).InfoHash == Hash &&
                                                                        ((TorrentInfo)r.Release).MagnetUrl == null &&
                                                                        r.Release.DownloadUrl == "http://indexer/download/1" &&
                                                                        r.Release.IndexerId == 2),
                                              3),
                        Times.Once());

            Mocker.GetMock<IIgnoredDownloadService>().Verify(s => s.IgnoreDownload(It.IsAny<TrackedDownload>()), Times.Never());
            Mocker.GetMock<ITrackedDownloadService>().Verify(s => s.StopTracking(Hash), Times.Once());
        }

        [Test]
        public async Task should_pass_magnet_link_as_magnet()
        {
            _grab.Release.DownloadUrl = "magnet:?xt=urn:btih:" + Hash;
            GivenEngineRegrabsSameTorrent();

            await Subject.ReAdd(Hash);

            Mocker.GetMock<IDownloadService>()
                .Verify(s => s.DownloadReport(It.Is<RemoteEpisode>(r => ((TorrentInfo)r.Release).MagnetUrl == "magnet:?xt=urn:btih:" + Hash), 3), Times.Once());
        }

        [Test]
        public async Task should_dismiss_old_grab_if_engine_returned_a_different_torrent()
        {
            await Subject.ReAdd(Hash);

            Mocker.GetMock<IIgnoredDownloadService>().Verify(s => s.IgnoreDownload(_trackedDownload), Times.Once());
            Mocker.GetMock<ITrackedDownloadService>().Verify(s => s.StopTracking(Hash), Times.Once());
        }

        [Test]
        public void should_only_re_add_downloads_missing_from_the_engine()
        {
            _trackedDownload.IsMissingFromEngine = false;

            Assert.ThrowsAsync<EngineReAddException>(() => Subject.ReAdd(Hash));
            VerifyNotSent();
        }

        [Test]
        public void should_not_re_add_when_linked_episodes_are_gone()
        {
            _trackedDownload.HasIncompleteLink = true;

            Assert.ThrowsAsync<EngineReAddException>(() => Subject.ReAdd(Hash));
            VerifyNotSent();
        }

        [Test]
        public void should_not_re_add_without_a_stored_release_link()
        {
            _grab.Release.DownloadUrl = null;

            Assert.ThrowsAsync<EngineReAddException>(() => Subject.ReAdd(Hash));
            VerifyNotSent();
        }

        [Test]
        public void should_report_engine_errors_as_re_add_errors()
        {
            Mocker.GetMock<IDownloadService>()
                .Setup(s => s.DownloadReport(It.IsAny<RemoteEpisode>(), 3))
                .ThrowsAsync(new DownloadClientUnavailableException("engine is down"));

            var ex = Assert.ThrowsAsync<EngineReAddException>(() => Subject.ReAdd(Hash));

            ex.Message.Should().Contain("engine is down");
            Mocker.GetMock<ITrackedDownloadService>().Verify(s => s.StopTracking(It.IsAny<string>()), Times.Never());
        }

        private void VerifyNotSent()
        {
            Mocker.GetMock<IDownloadService>().Verify(s => s.DownloadReport(It.IsAny<RemoteEpisode>(), It.IsAny<int?>()), Times.Never());
        }
    }
}
