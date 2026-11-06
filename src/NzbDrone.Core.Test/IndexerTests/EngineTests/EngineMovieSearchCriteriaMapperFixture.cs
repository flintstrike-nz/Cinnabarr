using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerEngine.Indexers;
using NzbDrone.Core.Indexers.Engine;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using EngineBasicCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.BasicSearchCriteria;
using EngineMovieCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.MovieSearchCriteria;

namespace NzbDrone.Core.Test.IndexerTests.EngineTests
{
    [TestFixture]
    public class EngineMovieSearchCriteriaMapperFixture : CoreTest
    {
        private readonly EngineSearchOptions _options = new()
        {
            Categories = new[] { 5000 },
            MovieCategories = new[] { 2000 }
        };

        private static IndexerCapabilities Caps(bool basic, params MovieSearchParam[] movie)
        {
            return new IndexerCapabilities
            {
                SearchParams = basic ? new List<SearchParam> { SearchParam.Q } : new List<SearchParam>(),
                MovieSearchParams = movie.ToList()
            };
        }

        private static MovieSearchCriteria Criteria(SearchMode mode = SearchMode.Default) => new()
        {
            Movie = new Movie { MovieMetadata = new MovieMetadata { TmdbId = 603, ImdbId = "tt0133093", Title = "The Matrix", Year = 1999 } },
            SceneTitles = new List<string> { "The Matrix", "Matrix, The" },
            SearchMode = mode
        };

        [Test]
        public void recent_movies_uses_movie_categories_with_movie_search()
        {
            var tiers = EngineSearchCriteriaMapper.RecentMovies(Caps(true, MovieSearchParam.Q), _options);

            var query = tiers.Should().ContainSingle().Which.Should().ContainSingle().Which.Should().BeOfType<EngineMovieCriteria>().Subject;
            query.IsRssSearch.Should().BeTrue();
            query.Categories.Should().BeEquivalentTo(new[] { 2000 });
        }

        [Test]
        public void recent_movies_falls_back_to_the_generic_feed()
        {
            var tiers = EngineSearchCriteriaMapper.RecentMovies(Caps(true), _options);

            tiers.Single().Single().Should().BeOfType<EngineBasicCriteria>().Which.Categories.Should().BeEquivalentTo(new[] { 2000 });
        }

        [Test]
        public void recent_movies_is_empty_when_the_site_cannot_search()
        {
            EngineSearchCriteriaMapper.RecentMovies(Caps(false), _options).Should().BeEmpty();
        }

        [Test]
        public void searches_ids_first_then_titles_with_the_year()
        {
            var tiers = EngineSearchCriteriaMapper.Map(Criteria(), Caps(true, MovieSearchParam.Q, MovieSearchParam.ImdbId, MovieSearchParam.TmdbId), _options);

            tiers.Should().HaveCount(2);

            var ids = tiers[0].Cast<EngineMovieCriteria>().First();
            ids.ImdbId.Should().Be("tt0133093");
            ids.TmdbId.Should().Be(603);
            ids.SearchTerm.Should().BeNull();
            ids.Categories.Should().BeEquivalentTo(new[] { 2000 });

            tiers[1].Cast<EngineMovieCriteria>().Select(c => c.SearchTerm).Should().OnlyContain(t => t.EndsWith(" 1999"));
        }

        [Test]
        public void only_sends_ids_the_site_supports()
        {
            var tiers = EngineSearchCriteriaMapper.Map(Criteria(), Caps(false, MovieSearchParam.ImdbId), _options);

            var ids = tiers.Single().Cast<EngineMovieCriteria>().Single();
            ids.ImdbId.Should().Be("tt0133093");
            ids.TmdbId.Should().BeNull();
        }

        [Test]
        public void uses_a_generic_search_for_titles_when_there_is_no_movie_search()
        {
            var tiers = EngineSearchCriteriaMapper.Map(Criteria(), Caps(true), _options);

            tiers.Should().ContainSingle().Which.Should().OnlyContain(c => c is EngineBasicCriteria);
            tiers[0].Cast<EngineBasicCriteria>().Select(c => c.SearchTerm).Should().Contain("Matrix 1999");
        }

        [Test]
        public void title_only_mode_skips_ids()
        {
            var tiers = EngineSearchCriteriaMapper.Map(Criteria(SearchMode.SearchTitle), Caps(true, MovieSearchParam.Q, MovieSearchParam.ImdbId), _options);

            tiers.Should().ContainSingle();
            tiers[0].Cast<EngineMovieCriteria>().Should().OnlyContain(c => c.SearchTerm != null && c.ImdbId == null);
        }

        [Test]
        public void returns_nothing_when_the_site_cannot_search_at_all()
        {
            EngineSearchCriteriaMapper.Map(Criteria(), Caps(false), _options).Should().BeEmpty();
        }
    }
}
