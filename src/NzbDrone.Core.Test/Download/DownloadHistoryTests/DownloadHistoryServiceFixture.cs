using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadHistoryTests
{
    [TestFixture]
    public class DownloadHistoryServiceFixture : CoreTest<DownloadHistoryService>
    {
        private const int ClientId = 3;
        private readonly DateTime _start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private List<DownloadHistory> _history;

        [SetUp]
        public void Setup()
        {
            _history = new List<DownloadHistory>();

            Mocker.GetMock<IDownloadHistoryRepository>()
                .Setup(r => r.FindByDownloadClientId(ClientId))
                .Returns(() => _history.OrderByDescending(h => h.Date).ToList());
        }

        private void GivenEvent(string downloadId, DownloadHistoryEventType eventType, int minutes)
        {
            _history.Add(new DownloadHistory { DownloadId = downloadId, EventType = eventType, Date = _start.AddMinutes(minutes), DownloadClientId = ClientId });
        }

        [Test]
        public void should_return_grab_with_no_later_outcome()
        {
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 1);

            Subject.GetUnfinishedGrabs(ClientId).Should().ContainSingle().Which.DownloadId.Should().Be("AAA");
        }

        [Test]
        public void should_return_grab_that_has_only_imported_some_files()
        {
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 1);
            GivenEvent("AAA", DownloadHistoryEventType.FileImported, 2);

            Subject.GetUnfinishedGrabs(ClientId).Should().ContainSingle();
        }

        [TestCase(DownloadHistoryEventType.DownloadImported)]
        [TestCase(DownloadHistoryEventType.DownloadFailed)]
        [TestCase(DownloadHistoryEventType.DownloadIgnored)]
        public void should_not_return_finished_grab(DownloadHistoryEventType outcome)
        {
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 1);
            GivenEvent("aaa", outcome, 2);

            Subject.GetUnfinishedGrabs(ClientId).Should().BeEmpty();
        }

        [Test]
        public void should_return_regrab_after_an_earlier_failure()
        {
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 1);
            GivenEvent("AAA", DownloadHistoryEventType.DownloadFailed, 2);
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 3);

            Subject.GetUnfinishedGrabs(ClientId).Should().ContainSingle().Which.Date.Should().Be(_start.AddMinutes(3));
        }
    }
}
