using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.History;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class FreeTextSearchServiceFixture : CoreTest<FreeTextSearchService>
    {
        private Mock<IIndexer> _indexer;
        private ReleaseInfo _movieRelease;
        private ReleaseInfo _unknownRelease;

        [SetUp]
        public void SetUp()
        {
            _movieRelease = new ReleaseInfo { Guid = "m", Title = "The.Matrix.1999.1080p.BluRay.x264-GRP" };
            _unknownRelease = new ReleaseInfo { Guid = "u", Title = "Something.Else.2020.720p.WEB-DL-GRP" };

            _indexer = new Mock<IIndexer>();
            _indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition { Id = 1 });
            _indexer.Setup(i => i.Fetch(It.IsAny<FreeTextSearchCriteria>()))
                    .ReturnsAsync(new List<ReleaseInfo> { _movieRelease, _unknownRelease, _movieRelease });

            Mocker.GetMock<IIndexerFactory>().Setup(f => f.InteractiveSearchEnabled(true)).Returns(new List<IIndexer> { _indexer.Object });

            Mocker.GetMock<IIndexerQueryService>()
                  .Setup(s => s.Track(It.IsAny<IIndexer>(), It.IsAny<IndexerQuerySource>(), It.IsAny<string>(), It.IsAny<Func<Task<IList<ReleaseInfo>>>>()))
                  .Returns<IIndexer, IndexerQuerySource, string, Func<Task<IList<ReleaseInfo>>>>((_, _, _, fetch) => fetch());

            Mocker.GetMock<IMakeDownloadDecision>()
                  .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                  .Returns<List<ReleaseInfo>, bool>((reports, _) => reports.ConvertAll(r => SeriesDecision(r, null)));

            Mocker.GetMock<IMakeMovieDownloadDecision>()
                  .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                  .Returns<List<ReleaseInfo>, bool>((reports, _) => reports.ConvertAll(r => MovieDecision(r, r.Guid == "m" ? new Movie { Id = 3 } : null)));
        }

        private static DownloadDecision SeriesDecision(ReleaseInfo release, Series series)
        {
            return new DownloadDecision(new RemoteEpisode { Release = release, Series = series, Episodes = new List<Episode>() },
                new DownloadRejection(DownloadRejectionReason.UnknownSeries, "Unknown Series"));
        }

        private static DownloadDecision MovieDecision(ReleaseInfo release, Movie movie)
        {
            return movie == null
                ? new DownloadDecision(new RemoteMovie { Release = release }, new DownloadRejection(DownloadRejectionReason.UnknownMovie, "Unknown movie"))
                : new DownloadDecision(new RemoteMovie { Release = release, Movie = movie });
        }

        [Test]
        public async Task should_search_the_text_on_every_interactive_indexer()
        {
            await Subject.Search("  the matrix ");

            _indexer.Verify(i => i.Fetch(It.Is<FreeTextSearchCriteria>(c => c.Query == "the matrix")), Times.Once());
        }

        [Test]
        public async Task should_not_search_an_empty_query()
        {
            var decisions = await Subject.Search("   ");

            decisions.Should().BeEmpty();
            _indexer.Verify(i => i.Fetch(It.IsAny<FreeTextSearchCriteria>()), Times.Never());
        }

        [Test]
        public async Task should_decide_once_per_release_and_prefer_the_library_match()
        {
            var decisions = await Subject.Search("matrix");

            decisions.Should().HaveCount(2);
            decisions[0].RemoteMovie.Movie.Id.Should().Be(3);
            decisions[0].Approved.Should().BeTrue();
            decisions[1].RemoteEpisode.Should().NotBeNull();
            decisions[1].Approved.Should().BeFalse();
        }

        [Test]
        public async Task should_keep_going_when_an_indexer_throws()
        {
            _indexer.Setup(i => i.Fetch(It.IsAny<FreeTextSearchCriteria>())).ThrowsAsync(new InvalidOperationException("boom"));

            var decisions = await Subject.Search("matrix");

            decisions.Should().BeEmpty();
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void pick_should_prefer_the_series_when_both_matched_and_the_release_has_episodes()
        {
            var series = new DownloadDecision(new RemoteEpisode
            {
                Series = new Series { Id = 1 },
                ParsedEpisodeInfo = new ParsedEpisodeInfo { EpisodeNumbers = new[] { 1 } }
            });
            var movie = MovieDecision(new ReleaseInfo(), new Movie { Id = 2 });

            FreeTextSearchService.Pick(series, movie).Should().BeSameAs(series);
        }

        [Test]
        public void pick_should_prefer_the_movie_when_both_matched_and_the_release_has_no_episodes()
        {
            var series = new DownloadDecision(new RemoteEpisode
            {
                Series = new Series { Id = 1 },
                ParsedEpisodeInfo = new ParsedEpisodeInfo()
            });
            var movie = MovieDecision(new ReleaseInfo(), new Movie { Id = 2 });

            FreeTextSearchService.Pick(series, movie).Should().BeSameAs(movie);
        }
    }
}
