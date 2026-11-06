using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests.Movies
{
    [TestFixture]
    public class MovieDownloadDecisionPrioritizationFixture : CoreTest<DownloadDecisionPriorizationService>
    {
        private Movie _movie;

        [SetUp]
        public void SetUp()
        {
            _movie = new Movie
            {
                Id = 1,
                QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() },
                MovieMetadata = new MovieMetadata { Runtime = 120 }
            };

            Mocker.GetMock<IDelayProfileService>()
                  .Setup(s => s.BestForTags(It.IsAny<HashSet<int>>()))
                  .Returns(new DelayProfile { PreferredProtocol = DownloadProtocol.Torrent });
        }

        private DownloadDecision Decision(Quality quality, int score = 0, DownloadProtocol protocol = DownloadProtocol.Torrent, long? size = null, int? seeders = null, Movie movie = null, int priority = 25)
        {
            var release = seeders.HasValue
                ? new TorrentInfo { Seeders = seeders, Peers = seeders }
                : new ReleaseInfo();

            release.DownloadProtocol = protocol;
            release.Size = size ?? 8.Gigabytes();
            release.IndexerPriority = priority;
            release.PublishDate = System.DateTime.UtcNow;

            return new DownloadDecision(new RemoteMovie
            {
                Movie = movie ?? _movie,
                ParsedMovieInfo = new ParsedMovieInfo { Quality = new QualityModel(quality) },
                CustomFormatScore = score,
                Release = release
            });
        }

        private List<DownloadDecision> Prioritize(params DownloadDecision[] decisions) => Subject.PrioritizeDecisions(decisions.ToList());

        [Test]
        public void should_put_the_higher_quality_first()
        {
            var sd = Decision(Quality.DVD);
            var hd = Decision(Quality.Bluray1080p);

            Prioritize(sd, hd).First().Should().BeSameAs(hd);
        }

        [Test]
        public void should_put_the_higher_custom_format_score_first_within_a_quality()
        {
            var low = Decision(Quality.Bluray1080p, score: 0);
            var high = Decision(Quality.Bluray1080p, score: 100);

            Prioritize(low, high).First().Should().BeSameAs(high);
        }

        [Test]
        public void should_prefer_the_preferred_protocol_over_indexer_priority()
        {
            var usenet = Decision(Quality.Bluray1080p, protocol: DownloadProtocol.Usenet);
            var torrent = Decision(Quality.Bluray1080p, protocol: DownloadProtocol.Torrent);

            Prioritize(usenet, torrent).First().Should().BeSameAs(torrent);
        }

        [Test]
        public void should_prefer_the_lower_indexer_priority_number()
        {
            var late = Decision(Quality.Bluray1080p, priority: 50);
            var early = Decision(Quality.Bluray1080p, priority: 1);

            Prioritize(late, early).First().Should().BeSameAs(early);
        }

        [Test]
        public void should_prefer_more_seeders_by_order_of_magnitude()
        {
            var few = Decision(Quality.Bluray1080p, seeders: 2);
            var many = Decision(Quality.Bluray1080p, seeders: 2000);

            Prioritize(few, many).First().Should().BeSameAs(many);
        }

        [Test]
        public void should_prefer_the_larger_release_when_there_is_no_preferred_size()
        {
            foreach (var item in _movie.QualityProfile.Items)
            {
                item.PreferredSize = null;
            }

            var small = Decision(Quality.Bluray1080p, size: 4.Gigabytes());
            var large = Decision(Quality.Bluray1080p, size: 12.Gigabytes());

            Prioritize(small, large).First().Should().BeSameAs(large);
        }

        [Test]
        public void should_order_each_movie_on_its_own()
        {
            var other = new Movie { Id = 2, QualityProfile = _movie.QualityProfile, MovieMetadata = new MovieMetadata { Runtime = 90 } };

            var a1 = Decision(Quality.DVD);
            var a2 = Decision(Quality.Bluray1080p);
            var b1 = Decision(Quality.DVD, movie: other);
            var b2 = Decision(Quality.Bluray1080p, movie: other);

            var result = Prioritize(a1, b1, a2, b2);

            result.Where(d => d.RemoteMovie.Movie.Id == 1).First().Should().BeSameAs(a2);
            result.Where(d => d.RemoteMovie.Movie.Id == 2).First().Should().BeSameAs(b2);
        }

        [Test]
        public void should_keep_decisions_that_matched_nothing_last()
        {
            var unmatched = new DownloadDecision(new RemoteMovie { Release = new ReleaseInfo() });
            var matched = Decision(Quality.Bluray1080p);

            Prioritize(unmatched, matched).Last().Should().BeSameAs(unmatched);
        }

        [Test]
        public void should_prefer_a_proper_when_configured_to()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.DownloadPropersAndRepacks).Returns(ProperDownloadTypes.PreferAndUpgrade);

            var original = Decision(Quality.Bluray1080p);
            var proper = Decision(Quality.Bluray1080p);
            proper.RemoteMovie.ParsedMovieInfo.Quality = new QualityModel(Quality.Bluray1080p, new Revision(2));

            Prioritize(original, proper).First().Should().BeSameAs(proper);
        }
    }
}
