using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineReconcileServiceFixture : CoreTest<EngineReconcileService>
    {
        private DownloadClientDefinition _client;
        private List<DownloadHistory> _unfinished;

        [SetUp]
        public void Setup()
        {
            _client = new DownloadClientDefinition { Id = 3, Name = "Embarr Engine" };
            _unfinished = new List<DownloadHistory> { new DownloadHistory { DownloadId = "AAA", SourceTitle = "Series.S01E01" } };

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetUnfinishedGrabs(3))
                .Returns(() => _unfinished);

            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(s => s.TrackMissingFromEngine(_client, It.IsAny<DownloadHistory>()))
                .Returns((DownloadClientDefinition c, DownloadHistory g) => new TrackedDownload { IsMissingFromEngine = true, DownloadItem = new DownloadClientItem { DownloadId = g.DownloadId } });
        }

        [Test]
        public void should_not_flag_grab_that_is_in_the_engine()
        {
            Subject.FindMissing(_client, new[] { "aaa" }).Should().BeEmpty();
            Subject.FindMissing(_client, new[] { "aaa" }).Should().BeEmpty();
        }

        [Test]
        public void should_not_flag_grab_after_a_single_missed_poll()
        {
            Subject.FindMissing(_client, new string[0]).Should().BeEmpty();

            Mocker.GetMock<ITrackedDownloadService>()
                .Verify(s => s.TrackMissingFromEngine(It.IsAny<DownloadClientDefinition>(), It.IsAny<DownloadHistory>()), Times.Never());
        }

        [Test]
        public void should_flag_grab_missing_from_consecutive_polls()
        {
            Subject.FindMissing(_client, new string[0]);

            Subject.FindMissing(_client, new string[0]).Should().ContainSingle()
                .Which.DownloadItem.DownloadId.Should().Be("AAA");
        }

        [Test]
        public void should_start_counting_again_after_grab_reappears()
        {
            Subject.FindMissing(_client, new string[0]);
            Subject.FindMissing(_client, new[] { "AAA" });

            Subject.FindMissing(_client, new string[0]).Should().BeEmpty();
        }

        [Test]
        public void should_not_flag_grab_once_it_is_finished()
        {
            Subject.FindMissing(_client, new string[0]);
            _unfinished.Clear();

            Subject.FindMissing(_client, new string[0]).Should().BeEmpty();
        }
    }
}
