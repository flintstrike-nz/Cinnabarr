using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Versions;

namespace NzbDrone.Core.Test.Download.DownloadApprovedReportsTests
{
    [TestFixture]
    public class DownloadApprovedSeriesVersionFixture : CoreTest<ProcessDownloadDecisions>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IPrioritizeDownloadDecision>()
                  .Setup(v => v.PrioritizeDecisions(It.IsAny<List<DownloadDecision>>()))
                  .Returns<List<DownloadDecision>>(v => v);

            Mocker.GetMock<IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue>());
        }

        private static DownloadDecision Decision(int slotId, string guid, params DownloadRejection[] rejections)
        {
            var series = new Series { Id = 5 };

            if (slotId != 0)
            {
                series.Slot = new SeriesVersionSlot { Id = slotId, SeriesId = 5 };
            }

            return new DownloadDecision(
                new RemoteEpisode
                {
                    Series = series,
                    Episodes = new List<Episode> { new Episode { Id = 10, SeriesId = 5 } },
                    ParsedEpisodeInfo = new ParsedEpisodeInfo(),
                    Release = new ReleaseInfo { Title = "Chernobyl " + guid, Guid = guid, IndexerId = 1, DownloadProtocol = DownloadProtocol.Torrent }
                },
                rejections);
        }

        [Test]
        public async Task should_grab_a_release_for_each_version_of_an_episode()
        {
            var versionOne = Decision(0, "a");
            var versionTwo = Decision(3, "b");

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { versionOne, versionTwo });

            processed.Grabbed.Should().Equal(versionOne, versionTwo);
        }

        [Test]
        public async Task should_grab_only_one_release_for_a_version_of_an_episode()
        {
            var first = Decision(3, "a");
            var second = Decision(3, "b");

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { first, second });

            processed.Grabbed.Should().ContainSingle().Which.Should().BeSameAs(first);
        }

        [Test]
        public async Task should_not_grab_the_same_release_for_two_versions()
        {
            var versionOne = Decision(0, "a");
            var versionTwo = Decision(3, "a");

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { versionOne, versionTwo });

            processed.Grabbed.Should().ContainSingle().Which.Should().BeSameAs(versionOne);
        }

        [Test]
        public async Task should_not_hold_a_release_for_a_version_back_in_the_pending_queue()
        {
            var delayed = Decision(3, "a", new DownloadRejection(DownloadRejectionReason.MinimumAgeDelay, "delay", RejectionType.Temporary));

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { delayed });

            processed.Grabbed.Should().BeEmpty();
            processed.Pending.Should().BeEmpty();
            Mocker.GetMock<IPendingReleaseService>().Verify(p => p.AddMany(It.IsAny<List<System.Tuple<DownloadDecision, PendingReleaseReason>>>()), Times.Never());
        }
    }
}
