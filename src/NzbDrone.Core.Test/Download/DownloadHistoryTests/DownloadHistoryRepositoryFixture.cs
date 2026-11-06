using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Download.DownloadHistoryTests
{
    [TestFixture]
    public class DownloadHistoryRepositoryFixture : DbTest<DownloadHistoryRepository, DownloadHistory>
    {
        private Series _series1;
        private Series _series2;

        [SetUp]
        public void Setup()
        {
            _series1 = Builder<Series>.CreateNew()
                                      .With(s => s.Id = 7)
                                      .Build();

            _series2 = Builder<Series>.CreateNew()
                                      .With(s => s.Id = 8)
                                      .Build();
        }

        [Test]
        public void should_delete_history_items_by_seriesId()
        {
            var items = Builder<DownloadHistory>.CreateListOfSize(5)
                .TheFirst(1)
                .With(c => c.Id = 0)
                .With(c => c.SeriesId = _series2.Id)
                .TheRest()
                .With(c => c.Id = 0)
                .With(c => c.SeriesId = _series1.Id)
                .BuildListOfNew();

            Db.InsertMany(items);

            Subject.DeleteBySeriesIds(new List<int> { _series1.Id });

            var removedItems = Subject.All().Where(h => h.SeriesId == _series1.Id);
            var nonRemovedItems = Subject.All().Where(h => h.SeriesId == _series2.Id);

            removedItems.Should().HaveCount(0);
            nonRemovedItems.Should().HaveCount(1);
        }

        private void GivenEvent(string downloadId, DownloadHistoryEventType eventType, int minutes, int downloadClientId = 3)
        {
            Db.Insert(new DownloadHistory
            {
                DownloadId = downloadId,
                EventType = eventType,
                Date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes),
                DownloadClientId = downloadClientId,
                SeriesId = _series1.Id,
                SourceTitle = "Series.S01E01"
            });
        }

        [Test]
        public void should_find_grab_with_no_later_outcome()
        {
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 1);

            Subject.FindUnfinishedGrabs(3).Should().ContainSingle().Which.DownloadId.Should().Be("AAA");
        }

        [Test]
        public void should_find_grab_that_has_only_imported_some_files()
        {
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 1);
            GivenEvent("AAA", DownloadHistoryEventType.FileImported, 2);

            Subject.FindUnfinishedGrabs(3).Should().ContainSingle();
        }

        [TestCase(DownloadHistoryEventType.DownloadImported)]
        [TestCase(DownloadHistoryEventType.DownloadFailed)]
        [TestCase(DownloadHistoryEventType.DownloadIgnored)]
        public void should_not_find_finished_grab(DownloadHistoryEventType outcome)
        {
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 1);
            GivenEvent("AAA", outcome, 2);

            Subject.FindUnfinishedGrabs(3).Should().BeEmpty();
        }

        [Test]
        public void should_find_only_the_latest_regrab_after_a_failure()
        {
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 1);
            GivenEvent("AAA", DownloadHistoryEventType.DownloadFailed, 2);
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 3);

            Subject.FindUnfinishedGrabs(3).Should().ContainSingle()
                .Which.Date.Should().Be(new DateTime(2026, 1, 1, 0, 3, 0, DateTimeKind.Utc));
        }

        [Test]
        public void should_not_find_grabs_sent_to_other_clients()
        {
            GivenEvent("AAA", DownloadHistoryEventType.DownloadGrabbed, 1, downloadClientId: 4);

            Subject.FindUnfinishedGrabs(3).Should().BeEmpty();
        }

        [Test]
        public void should_store_the_movie_of_a_grab_and_delete_history_items_by_movie_id()
        {
            Subject.Insert(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadGrabbed, MovieId = 8, DownloadId = "A", SourceTitle = "a", Date = DateTime.UtcNow });
            Subject.Insert(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadGrabbed, MovieId = 9, DownloadId = "B", SourceTitle = "b", Date = DateTime.UtcNow });
            Subject.Insert(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadGrabbed, SeriesId = 7, DownloadId = "C", SourceTitle = "c", Date = DateTime.UtcNow });

            Subject.FindByDownloadId("A").Should().ContainSingle().Which.MovieId.Should().Be(8);

            Subject.DeleteByMovieIds(new List<int> { 8 });

            Subject.All().Select(h => h.DownloadId).Should().BeEquivalentTo(new[] { "B", "C" });
        }
    }
}
