using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerEngine.Indexers;
using NzbDrone.Core.Indexers.Engine;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using EngineBasicCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.BasicSearchCriteria;
using EngineTvCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.TvSearchCriteria;

namespace NzbDrone.Core.Test.IndexerTests.EngineTests
{
    [TestFixture]
    public class EngineSearchCriteriaMapperFixture : CoreTest
    {
        private readonly EngineSearchOptions _options = new()
        {
            Categories = new[] { 5000 },
            AnimeCategories = new[] { 5070 },
            AnimeStandardFormatSearch = true
        };

        private static IndexerCapabilities Caps(params TvSearchParam[] tv)
        {
            return new IndexerCapabilities
            {
                SearchParams = new List<SearchParam> { SearchParam.Q },
                TvSearchParams = tv.ToList()
            };
        }

        private static Series Series() => new() { Title = "The Show", TvdbId = 1234, ImdbId = "tt0000042", SeriesType = SeriesTypes.Standard };

        private static SingleEpisodeSearchCriteria Episode(SearchMode mode = SearchMode.Default) => new()
        {
            Series = Series(),
            SceneTitles = new List<string> { "The Show", "Show: Redux" },
            SeasonNumber = 2,
            EpisodeNumber = 5,
            SearchMode = mode
        };

        [Test]
        public void recent_search_is_a_single_tv_rss_query_over_all_categories()
        {
            var tiers = EngineSearchCriteriaMapper.Recent(_options);

            var query = tiers.Should().ContainSingle().Which.Should().ContainSingle().Which.Should().BeOfType<EngineTvCriteria>().Subject;
            query.IsRssSearch.Should().BeTrue();
            query.Categories.Should().BeEquivalentTo(new[] { 5000, 5070 });
        }

        [Test]
        public void single_episode_searches_ids_first_then_titles()
        {
            var tiers = EngineSearchCriteriaMapper.Map(Episode(), Caps(TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep, TvSearchParam.TvdbId, TvSearchParam.ImdbId), _options);

            tiers.Should().HaveCount(2);

            var ids = tiers[0].Should().ContainSingle().Which.Should().BeOfType<EngineTvCriteria>().Subject;
            ids.TvdbId.Should().Be(1234);
            ids.ImdbId.Should().Be("tt0000042");
            ids.SearchTerm.Should().BeNull();
            ids.Season.Should().Be(2);
            ids.Episode.Should().Be("5");

            tiers[1].Cast<EngineTvCriteria>().Select(c => c.SearchTerm).Should().BeEquivalentTo("Show", "Show Redux");
            tiers[1].Cast<EngineTvCriteria>().Should().OnlyContain(c => c.Season == 2 && c.Episode == "5");
        }

        [Test]
        public void only_ids_the_indexer_supports_are_sent()
        {
            var tiers = EngineSearchCriteriaMapper.Map(Episode(), Caps(TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep, TvSearchParam.ImdbId), _options);

            var ids = (EngineTvCriteria)tiers[0].Single();
            ids.TvdbId.Should().BeNull();
            ids.ImdbId.Should().Be("tt0000042");
        }

        [Test]
        public void falls_back_to_titles_only_when_no_id_is_supported()
        {
            var tiers = EngineSearchCriteriaMapper.Map(Episode(), Caps(TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep), _options);

            tiers.Should().ContainSingle();
            tiers[0].Cast<EngineTvCriteria>().Should().OnlyContain(c => c.SearchTerm != null);
        }

        [Test]
        public void id_only_search_mode_skips_the_title_fallback()
        {
            var tiers = EngineSearchCriteriaMapper.Map(Episode(SearchMode.SearchID), Caps(TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep, TvSearchParam.TvdbId), _options);

            tiers.Should().ContainSingle();
            tiers[0].Should().ContainSingle().Which.As<EngineTvCriteria>().TvdbId.Should().Be(1234);
        }

        [Test]
        public void no_search_is_made_when_the_indexer_cannot_filter_by_season_and_episode()
        {
            EngineSearchCriteriaMapper.Map(Episode(), Caps(TvSearchParam.Q, TvSearchParam.TvdbId), _options).Should().BeEmpty();
        }

        [Test]
        public void daily_episode_uses_year_as_season_and_month_day_as_episode()
        {
            var criteria = new DailyEpisodeSearchCriteria
            {
                Series = Series(),
                SceneTitles = new List<string> { "The Show" },
                AirDate = new DateTime(2024, 3, 9),
                SearchMode = SearchMode.SearchID
            };

            var tiers = EngineSearchCriteriaMapper.Map(criteria, Caps(TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep, TvSearchParam.TvdbId), _options);

            var query = (EngineTvCriteria)tiers.Single().Single();
            query.Season.Should().Be(2024);
            query.Episode.Should().Be("03/09");
        }

        [Test]
        public void season_search_has_no_episode()
        {
            var criteria = new SeasonSearchCriteria { Series = Series(), SceneTitles = new List<string> { "The Show" }, SeasonNumber = 3 };

            var tiers = EngineSearchCriteriaMapper.Map(criteria, Caps(TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.TvdbId), _options);

            tiers[0].Single().As<EngineTvCriteria>().Episode.Should().BeNull();
            tiers[0].Single().As<EngineTvCriteria>().Season.Should().Be(3);
        }

        [Test]
        public void anime_episode_searches_by_absolute_number_with_anime_categories()
        {
            var criteria = new AnimeEpisodeSearchCriteria
            {
                Series = new Series { Title = "Anime", SeriesType = SeriesTypes.Anime },
                SceneTitles = new List<string> { "Anime" },
                AbsoluteEpisodeNumber = 7,
                SeasonNumber = 1,
                EpisodeNumber = 7
            };

            var tiers = EngineSearchCriteriaMapper.Map(criteria, Caps(TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep), _options);

            var basics = tiers.Single().OfType<EngineBasicCriteria>().ToList();
            basics.Select(b => b.SearchTerm).Should().BeEquivalentTo("07", "Anime 07");
            basics.Should().OnlyContain(b => b.Categories.SequenceEqual(new[] { 5070 }));
            tiers.Single().OfType<EngineTvCriteria>().Should().NotBeEmpty("the standard S/E format search is on");
        }

        [Test]
        public void special_episode_searches_each_title()
        {
            var criteria = new SpecialEpisodeSearchCriteria
            {
                Series = Series(),
                SceneTitles = new List<string> { "The Show" },
                EpisodeQueryTitles = new[] { "The+Show+Special", "Other Title" }
            };

            var tiers = EngineSearchCriteriaMapper.Map(criteria, Caps(), _options);

            tiers.Single().Cast<EngineBasicCriteria>().Select(c => c.SearchTerm).Should().BeEquivalentTo("The Show Special", "Other Title");
        }
    }
}
