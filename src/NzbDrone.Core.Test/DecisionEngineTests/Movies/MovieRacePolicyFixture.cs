using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests.Movies
{
    [TestFixture]
    public class MovieRacePolicyFixture : CoreTest<MovieRacePolicy>
    {
        private RemoteMovie _candidate;
        private List<RemoteMovie> _others;

        [SetUp]
        public void Setup()
        {
            _candidate = Remote(1, "new");
            _others = new List<RemoteMovie> { Remote(2, "queued") };

            Mocker.GetMock<IConfigService>().SetupGet(c => c.EngineRaceMaxSources).Returns(2);
            GivenIndexer(1, true);
            GivenIndexer(2, true);
        }

        private static RemoteMovie Remote(int indexerId, string guid)
        {
            return new RemoteMovie { Release = new ReleaseInfo { IndexerId = indexerId, Guid = guid } };
        }

        private void GivenIndexer(int id, bool allowRace)
        {
            Mocker.GetMock<IIndexerFactory>().Setup(f => f.Exists(id)).Returns(true);
            Mocker.GetMock<IIndexerFactory>().Setup(f => f.Get(id)).Returns(new IndexerDefinition { Id = id, AllowRace = allowRace });
        }

        [Test]
        public void should_let_a_release_join_when_racing_is_on_and_every_indexer_allows_it()
        {
            Subject.MayJoin(_candidate, _others).Should().BeTrue();
        }

        [Test]
        public void should_not_race_when_only_one_source_is_allowed()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EngineRaceMaxSources).Returns(1);

            Subject.MayJoin(_candidate, _others).Should().BeFalse();
        }

        [Test]
        public void should_not_race_when_the_limit_is_reached()
        {
            _others.Add(Remote(2, "another"));

            Subject.MayJoin(_candidate, _others).Should().BeFalse();
        }

        [Test]
        public void should_not_race_when_the_new_releases_indexer_does_not_allow_it()
        {
            GivenIndexer(1, false);

            Subject.MayJoin(_candidate, _others).Should().BeFalse();
        }

        [Test]
        public void should_not_race_when_a_queued_releases_indexer_does_not_allow_it()
        {
            GivenIndexer(2, false);

            Subject.MayJoin(_candidate, _others).Should().BeFalse();
        }

        [Test]
        public void should_not_race_a_release_against_itself()
        {
            Subject.MayJoin(Remote(2, "queued"), _others).Should().BeFalse();
        }

        [Test]
        public void should_race_against_the_releases_queued_for_the_same_version()
        {
            var movie = new Movie { Id = 4 };
            var queued = Remote(2, "queued");
            queued.Movie = movie;
            _candidate.Movie = movie;

            Mocker.GetMock<IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue> { new NzbDrone.Core.Queue.Queue { RemoteMovie = queued } });

            Subject.MayRace(_candidate).Should().BeTrue();
        }

        [Test]
        public void should_not_race_against_another_movie_or_with_an_empty_queue()
        {
            var queued = Remote(2, "queued");
            queued.Movie = new Movie { Id = 9 };
            _candidate.Movie = new Movie { Id = 4 };

            Mocker.GetMock<IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue> { new NzbDrone.Core.Queue.Queue { RemoteMovie = queued } });

            Subject.MayRace(_candidate).Should().BeFalse();
        }

        [Test]
        public void should_not_race_a_release_with_no_known_indexer()
        {
            Subject.MayJoin(Remote(0, "pushed"), _others).Should().BeFalse();
        }
    }
}
