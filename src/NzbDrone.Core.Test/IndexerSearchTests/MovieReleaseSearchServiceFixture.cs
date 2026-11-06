using System;
using System.Collections.Generic;
using System.Linq;
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
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class MovieReleaseSearchServiceFixture : CoreTest<MovieReleaseSearchService>
    {
        private Movie _movie;
        private Mock<IIndexer> _indexer;
        private List<ReleaseInfo> _releases;

        [SetUp]
        public void SetUp()
        {
            _movie = new Movie
            {
                Id = 7,
                MovieMetadata = new MovieMetadata
                {
                    TmdbId = 603,
                    Title = "The Matrix",
                    OriginalTitle = "The Matrix",
                    Year = 1999,
                    AlternativeTitles = new List<Movies.AlternativeTitles.AlternativeTitle> { new Movies.AlternativeTitles.AlternativeTitle("Matrix") }
                }
            };

            _releases = new List<ReleaseInfo> { new ReleaseInfo { Guid = "a", Title = "The.Matrix.1999.1080p.BluRay.x264-GRP" } };

            _indexer = new Mock<IIndexer>();
            _indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition { Id = 1 });
            _indexer.Setup(i => i.Fetch(It.IsAny<MovieSearchCriteria>())).ReturnsAsync(_releases);

            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(7)).Returns(_movie);

            Mocker.GetMock<IIndexerQueryService>()
                  .Setup(s => s.Track(It.IsAny<IIndexer>(), It.IsAny<IndexerQuerySource>(), It.IsAny<string>(), It.IsAny<Func<Task<IList<ReleaseInfo>>>>()))
                  .Returns<IIndexer, IndexerQuerySource, string, Func<Task<IList<ReleaseInfo>>>>((_, _, _, fetch) => fetch());

            Mocker.GetMock<IIndexerFactory>().Setup(f => f.AutomaticSearchEnabled(true)).Returns(new List<IIndexer> { _indexer.Object });
            Mocker.GetMock<IIndexerFactory>().Setup(f => f.InteractiveSearchEnabled(true)).Returns(new List<IIndexer> { _indexer.Object });

            Mocker.GetMock<IMakeMovieDownloadDecision>()
                  .Setup(d => d.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<Movie>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns(new List<DownloadDecision>());
        }

        [Test]
        public async Task should_search_with_every_title_of_the_movie()
        {
            await Subject.MovieSearch(7, false, false);

            _indexer.Verify(i => i.Fetch(It.Is<MovieSearchCriteria>(c => c.Movie == _movie && c.SceneTitles.SequenceEqual(new[] { "The Matrix", "Matrix" }))), Times.Once());
        }

        [Test]
        public async Task should_make_decisions_for_the_found_releases_against_the_searched_movie()
        {
            await Subject.MovieSearch(7, true, true);

            Mocker.GetMock<IMakeMovieDownloadDecision>().Verify(d => d.GetSearchDecision(_releases, _movie, true, true), Times.Once());
        }

        [Test]
        public async Task should_use_interactive_indexers_for_interactive_searches()
        {
            await Subject.MovieSearch(7, true, true);

            Mocker.GetMock<IIndexerFactory>().Verify(f => f.InteractiveSearchEnabled(true), Times.Once());
            Mocker.GetMock<IIndexerFactory>().Verify(f => f.AutomaticSearchEnabled(It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public async Task should_skip_indexers_whose_tags_the_movie_does_not_share()
        {
            _indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition { Id = 1, Tags = new HashSet<int> { 5 } });

            await Subject.MovieSearch(7, false, false);

            _indexer.Verify(i => i.Fetch(It.IsAny<MovieSearchCriteria>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateLastSearchTime(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public async Task should_update_last_search_time_when_an_indexer_was_searched()
        {
            await Subject.MovieSearch(7, false, false);

            _movie.LastSearchTime.Should().NotBeNull();
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateLastSearchTime(_movie), Times.Once());
        }

        [Test]
        public async Task should_keep_going_when_an_indexer_throws()
        {
            _indexer.Setup(i => i.Fetch(It.IsAny<MovieSearchCriteria>())).ThrowsAsync(new InvalidOperationException("boom"));

            var decisions = await Subject.MovieSearch(7, false, false);

            decisions.Should().BeEmpty();
            _indexer.Verify(i => i.Fetch(It.IsAny<MovieSearchCriteria>()), Times.Once());
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public async Task should_keep_only_the_best_decision_for_a_release_found_twice()
        {
            var release = new ReleaseInfo { Guid = "dup", Title = "x" };
            var rejected = new DownloadDecision(new RemoteMovie { Release = release }, new DownloadRejection(DownloadRejectionReason.MinimumAge, "old"));
            var accepted = new DownloadDecision(new RemoteMovie { Release = release });

            Mocker.GetMock<IMakeMovieDownloadDecision>()
                  .Setup(d => d.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<Movie>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns(new List<DownloadDecision> { rejected, accepted });

            var decisions = await Subject.MovieSearch(7, false, false);

            decisions.Should().ContainSingle().Which.Should().BeSameAs(accepted);
        }
    }
}
