using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadApprovedReportsTests
{
    [TestFixture]
    public class DownloadApprovedMovieFixture : CoreTest<ProcessDownloadDecisions>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IPrioritizeDownloadDecision>()
                  .Setup(v => v.PrioritizeDecisions(It.IsAny<List<DownloadDecision>>()))
                  .Returns<List<DownloadDecision>>(v => v);
        }

        private static DownloadDecision Decision(int movieId, DownloadProtocol protocol = DownloadProtocol.Torrent, params DownloadRejection[] rejections)
        {
            return new DownloadDecision(
                new RemoteMovie
                {
                    Movie = new Movie { Id = movieId },
                    Release = new ReleaseInfo { Title = $"Movie {movieId}", DownloadProtocol = protocol }
                },
                rejections);
        }

        [Test]
        public async Task should_grab_an_approved_movie_decision()
        {
            var decision = Decision(1);

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { decision });

            processed.Grabbed.Should().ContainSingle().Which.Should().BeSameAs(decision);
            Mocker.GetMock<IDownloadService>().Verify(s => s.DownloadReport(decision.RemoteMovie, null), Times.Once());
        }

        [Test]
        public async Task should_grab_only_the_first_decision_for_a_movie()
        {
            var first = Decision(1);
            var second = Decision(1);
            var other = Decision(2);

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { first, second, other });

            processed.Grabbed.Should().Equal(first, other);
            Mocker.GetMock<IDownloadService>().Verify(s => s.DownloadReport(second.RemoteMovie, It.IsAny<int?>()), Times.Never());
        }

        private static DownloadDecision VersionDecision(int movieId, int slotId, string guid)
        {
            var movie = new Movie { Id = movieId };
            movie.Slot = new NzbDrone.Core.Movies.Versions.VersionSlot { Id = slotId, MovieId = movieId };

            return new DownloadDecision(new RemoteMovie
            {
                Movie = movie,
                Release = new ReleaseInfo { Title = $"Movie {movieId} {slotId}", Guid = guid, IndexerId = 1, DownloadProtocol = DownloadProtocol.Torrent }
            });
        }

        [Test]
        public async Task should_grab_a_release_for_each_version_of_a_movie()
        {
            var versionOne = VersionDecision(1, 0, "a");
            var versionTwo = VersionDecision(1, 7, "b");

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { versionOne, versionTwo });

            processed.Grabbed.Should().Equal(versionOne, versionTwo);
        }

        [Test]
        public async Task should_not_grab_the_same_release_for_two_versions()
        {
            var versionOne = VersionDecision(1, 0, "a");
            var versionTwo = VersionDecision(1, 7, "a");

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { versionOne, versionTwo });

            processed.Grabbed.Should().ContainSingle().Which.Should().BeSameAs(versionOne);
        }

        [Test]
        public async Task should_not_grab_a_second_release_for_the_same_version()
        {
            var first = VersionDecision(1, 7, "a");
            var second = VersionDecision(1, 7, "b");

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { first, second });

            processed.Grabbed.Should().ContainSingle().Which.Should().BeSameAs(first);
        }

        [Test]
        public async Task should_not_grab_a_rejected_movie_decision_and_report_it_rejected()
        {
            var decision = Decision(1, DownloadProtocol.Torrent, new DownloadRejection(DownloadRejectionReason.MinimumAge, "too new"));

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { decision });

            processed.Grabbed.Should().BeEmpty();
            processed.Rejected.Should().ContainSingle();
            Mocker.GetMock<IDownloadService>().Verify(s => s.DownloadReport(It.IsAny<RemoteMedia>(), It.IsAny<int?>()), Times.Never());
        }

        [Test]
        public async Task should_not_hold_a_movie_when_the_download_client_is_unavailable()
        {
            Mocker.GetMock<IDownloadService>()
                  .Setup(s => s.DownloadReport(It.IsAny<RemoteMedia>(), It.IsAny<int?>()))
                  .ThrowsAsync(new DownloadClientUnavailableException("no client"));

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { Decision(1), Decision(2) });

            // The first failure marks the protocol failed, so the second isn't even tried; nothing is stored for later.
            processed.Grabbed.Should().BeEmpty();
            processed.Pending.Should().BeEmpty();
            Mocker.GetMock<IDownloadService>().Verify(s => s.DownloadReport(It.IsAny<RemoteMedia>(), It.IsAny<int?>()), Times.Once());
            Mocker.GetMock<IPendingReleaseService>().Verify(s => s.AddMany(It.IsAny<List<System.Tuple<DownloadDecision, PendingReleaseReason>>>()), Times.Never());
        }

        [Test]
        public async Task should_skip_a_single_movie_decision_that_is_temporarily_rejected()
        {
            var decision = new DownloadDecision(
                new RemoteMovie { Movie = new Movie { Id = 1 }, Release = new ReleaseInfo { DownloadProtocol = DownloadProtocol.Torrent } },
                new DownloadRejection(DownloadRejectionReason.MinimumAge, "wait", RejectionType.Temporary));

            var result = await Subject.ProcessDecision(decision, null);

            result.Should().Be(ProcessedDecisionResult.Skipped);
            Mocker.GetMock<IPendingReleaseService>().Verify(s => s.Add(It.IsAny<DownloadDecision>(), It.IsAny<PendingReleaseReason>()), Times.Never());
        }

        [Test]
        public async Task should_still_handle_series_and_movie_decisions_together()
        {
            var movie = Decision(1);
            var episode = new DownloadDecision(new RemoteEpisode
            {
                Series = new NzbDrone.Core.Tv.Series { Id = 5 },
                Episodes = new List<NzbDrone.Core.Tv.Episode> { new() { Id = 9 } },
                Release = new ReleaseInfo { DownloadProtocol = DownloadProtocol.Torrent }
            });

            var processed = await Subject.ProcessDecisions(new List<DownloadDecision> { movie, episode });

            processed.Grabbed.Should().HaveCount(2);
        }
    }
}
