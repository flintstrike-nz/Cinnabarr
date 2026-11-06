using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerEngine.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Tv;
using EngineBasicCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.BasicSearchCriteria;
using EngineCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.SearchCriteriaBase;
using EngineMovieCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.MovieSearchCriteria;
using EngineTvCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.TvSearchCriteria;

namespace NzbDrone.Core.Indexers.Engine
{
    public class EngineSearchOptions
    {
        public int[] Categories { get; set; } = Array.Empty<int>();
        public int[] AnimeCategories { get; set; } = Array.Empty<int>();
        public bool AnimeStandardFormatSearch { get; set; }
        public int[] MovieCategories { get; set; } = Array.Empty<int>();
    }

    /// <summary>
    /// Turns Embarr's search criteria into engine criteria. The result is a list of tiers: the first tier that
    /// finds releases wins and later tiers are not run (IDs first, then scene titles), as Sonarr's Newznab
    /// request generator does.
    /// </summary>
    public static class EngineSearchCriteriaMapper
    {
        public static List<List<EngineCriteria>> Recent(IndexerCapabilities caps, EngineSearchOptions options)
        {
            if (caps.TvSearchAvailable)
            {
                return Tiers(Tier(new EngineTvCriteria { SearchType = "tvsearch", Categories = AllCategories(options) }));
            }

            // Sites without tv search only offer the generic feed, which Sonarr reads with the anime categories.
            if (caps.SearchAvailable)
            {
                return Tiers(Tier(new EngineBasicCriteria { SearchType = "search", Categories = options.AnimeCategories }));
            }

            return Tiers();
        }

        public static List<List<EngineCriteria>> RecentMovies(IndexerCapabilities caps, EngineSearchOptions options)
        {
            if (caps.MovieSearchAvailable)
            {
                return Tiers(Tier(new EngineMovieCriteria { SearchType = "movie", Categories = options.MovieCategories }));
            }

            if (caps.SearchAvailable)
            {
                return Tiers(Tier(new EngineBasicCriteria { SearchType = "search", Categories = options.MovieCategories }));
            }

            return Tiers();
        }

        // A plain text search: the query as typed, in every category.
        public static List<List<EngineCriteria>> Map(FreeTextSearchCriteria criteria, IndexerCapabilities caps)
        {
            if (!caps.SearchAvailable)
            {
                return Tiers();
            }

            return Tiers(Tier(Basic(criteria.Query, Array.Empty<int>())));
        }

        public static List<List<EngineCriteria>> Map(MovieSearchCriteria criteria, IndexerCapabilities caps, EngineSearchOptions options)
        {
            var metadata = criteria.Movie.MovieMetadata.Value;
            var ids = new List<EngineCriteria>();

            if (caps.MovieSearchAvailable)
            {
                var idCriteria = new EngineMovieCriteria { SearchType = "movie", Categories = options.MovieCategories };
                var any = false;

                if (caps.MovieSearchImdbAvailable && metadata.ImdbId.IsNotNullOrWhiteSpace())
                {
                    idCriteria.ImdbId = metadata.ImdbId;
                    any = true;
                }

                if (caps.MovieSearchTmdbAvailable && metadata.TmdbId > 0)
                {
                    idCriteria.TmdbId = metadata.TmdbId;
                    any = true;
                }

                if (any)
                {
                    ids.Add(idCriteria);
                }
            }

            var year = metadata.Year > 0 ? $" {metadata.Year}" : string.Empty;
            var terms = Titles(criteria, caps).Select(t => t + year).ToList();
            var titles = new List<EngineCriteria>();

            if (caps.MovieSearchParams.Contains(MovieSearchParam.Q))
            {
                titles.AddRange(terms.Select(t => (EngineCriteria)new EngineMovieCriteria { SearchType = "movie", SearchTerm = t, Categories = options.MovieCategories }));
            }
            else if (caps.SearchAvailable)
            {
                titles.AddRange(terms.Select(t => (EngineCriteria)Basic(t, options.MovieCategories)));
            }

            var idSearch = criteria.SearchMode.HasFlag(SearchMode.SearchID) || criteria.SearchMode == SearchMode.Default;
            var titleSearch = criteria.SearchMode.HasFlag(SearchMode.SearchTitle);
            var titleFallback = criteria.SearchMode == SearchMode.Default;

            var first = new List<EngineCriteria>();

            if (idSearch)
            {
                first.AddRange(ids);
            }

            if (titleSearch)
            {
                first.AddRange(titles);
            }

            return Tiers(first, titleFallback ? titles : new List<EngineCriteria>());
        }

        public static List<List<EngineCriteria>> Map(SingleEpisodeSearchCriteria criteria, IndexerCapabilities caps, EngineSearchOptions options)
        {
            if (!caps.TvSearchSeasonAvailable || !caps.TvSearchEpAvailable)
            {
                return Tiers();
            }

            return Standard(criteria, caps, options.Categories, season: criteria.SeasonNumber, episode: criteria.EpisodeNumber.ToString());
        }

        public static List<List<EngineCriteria>> Map(SeasonSearchCriteria criteria, IndexerCapabilities caps, EngineSearchOptions options)
        {
            if (!caps.TvSearchSeasonAvailable)
            {
                return Tiers();
            }

            return Standard(criteria, caps, options.Categories, season: criteria.SeasonNumber, episode: null);
        }

        public static List<List<EngineCriteria>> Map(DailyEpisodeSearchCriteria criteria, IndexerCapabilities caps, EngineSearchOptions options)
        {
            if (!caps.TvSearchSeasonAvailable || !caps.TvSearchEpAvailable)
            {
                return Tiers();
            }

            return Standard(criteria, caps, options.Categories, season: criteria.AirDate.Year, episode: $"{criteria.AirDate:MM}/{criteria.AirDate:dd}");
        }

        public static List<List<EngineCriteria>> Map(DailySeasonSearchCriteria criteria, IndexerCapabilities caps, EngineSearchOptions options)
        {
            if (!caps.TvSearchSeasonAvailable)
            {
                return Tiers();
            }

            return Standard(criteria, caps, options.Categories, season: criteria.Year, episode: null);
        }

        public static List<List<EngineCriteria>> Map(AnimeEpisodeSearchCriteria criteria, IndexerCapabilities caps, EngineSearchOptions options)
        {
            var first = new List<EngineCriteria>();
            var standard = options.AnimeStandardFormatSearch && criteria.SeasonNumber > 0 && criteria.EpisodeNumber > 0 &&
                           caps.TvSearchSeasonAvailable && caps.TvSearchEpAvailable;

            if (standard)
            {
                AddIds(first, criteria, caps, options.AnimeCategories, criteria.SeasonNumber, criteria.EpisodeNumber.ToString());
            }

            if (caps.SearchAvailable)
            {
                first.Add(Basic($"{criteria.AbsoluteEpisodeNumber:00}", options.AnimeCategories));
            }

            foreach (var title in Titles(criteria, caps))
            {
                if (caps.SearchAvailable)
                {
                    first.Add(Basic($"{title} {criteria.AbsoluteEpisodeNumber:00}", options.AnimeCategories));
                }

                if (standard && HasTvText(caps))
                {
                    first.Add(Tv(title, options.AnimeCategories, criteria.SeasonNumber, criteria.EpisodeNumber.ToString()));
                }
            }

            return Tiers(first);
        }

        public static List<List<EngineCriteria>> Map(AnimeSeasonSearchCriteria criteria, IndexerCapabilities caps, EngineSearchOptions options)
        {
            var standard = options.AnimeStandardFormatSearch && caps.TvSearchSeasonAvailable;

            // Each path below needs a different capability: titles alone use the generic search, the S/E format uses tv search.
            if (criteria.SeasonNumber <= 0 || (!standard && !caps.SearchAvailable))
            {
                return Tiers();
            }

            var first = new List<EngineCriteria>();

            if (standard)
            {
                AddIds(first, criteria, caps, options.AnimeCategories, criteria.SeasonNumber, null);
            }

            var seasonTitles = (caps.SupportsRawSearch ? criteria.AllSeasonSceneTitles : criteria.CleanSeasonSceneTitles).ToList();

            foreach (var title in Titles(criteria, caps))
            {
                if (seasonTitles.Contains(title, StringComparer.InvariantCultureIgnoreCase))
                {
                    // A season title alias already identifies the season, no need to add a season number.
                    if (caps.SearchAvailable)
                    {
                        first.Add(Basic(title, options.AnimeCategories));
                    }
                }
                else if (standard && HasTvText(caps))
                {
                    first.Add(Tv(title, options.AnimeCategories, criteria.SeasonNumber, null));
                }
            }

            return Tiers(first);
        }

        public static List<List<EngineCriteria>> Map(SpecialEpisodeSearchCriteria criteria, IndexerCapabilities caps, EngineSearchOptions options)
        {
            if (!caps.SearchAvailable)
            {
                return Tiers();
            }

            var categories = Categories(criteria, options);

            return Tiers((criteria.EpisodeQueryTitles ?? Array.Empty<string>())
                .Select(t => (EngineCriteria)Basic(t.Replace('+', ' '), categories))
                .ToList());
        }

        private static List<List<EngineCriteria>> Standard(SearchCriteriaBase criteria, IndexerCapabilities caps, int[] categories, int season, string episode)
        {
            var idSearch = HasIds(caps) && (criteria.SearchMode.HasFlag(SearchMode.SearchID) || criteria.SearchMode == SearchMode.Default);
            var titleSearch = criteria.SearchMode.HasFlag(SearchMode.SearchTitle);
            var titleFallback = criteria.SearchMode == SearchMode.Default;

            var first = new List<EngineCriteria>();

            if (idSearch)
            {
                AddIds(first, criteria, caps, categories, season, episode);
            }

            if (titleSearch)
            {
                AddTitles(first, criteria, caps, categories, season, episode);
            }

            var second = new List<EngineCriteria>();

            if (titleFallback)
            {
                AddTitles(second, criteria, caps, categories, season, episode);
            }

            return Tiers(first, second);
        }

        private static bool HasTvText(IndexerCapabilities caps)
        {
            return caps.TvSearchParams.Contains(TvSearchParam.Q);
        }

        private static bool HasIds(IndexerCapabilities caps)
        {
            return caps.TvSearchTvdbAvailable || caps.TvSearchImdbAvailable || caps.TvSearchTvRageAvailable || caps.TvSearchTvMazeAvailable || caps.TvSearchTmdbAvailable;
        }

        private static void AddIds(List<EngineCriteria> into, SearchCriteriaBase criteria, IndexerCapabilities caps, int[] categories, int season, string episode)
        {
            var series = criteria.Series;
            var ids = new EngineTvCriteria
            {
                SearchType = "tvsearch",
                Categories = categories,
                Season = season,
                Episode = episode
            };

            var any = false;

            if (caps.TvSearchTvdbAvailable && series.TvdbId > 0)
            {
                ids.TvdbId = series.TvdbId;
                any = true;
            }

            if (caps.TvSearchImdbAvailable && series.ImdbId.IsNotNullOrWhiteSpace())
            {
                ids.ImdbId = series.ImdbId;
                any = true;
            }

            if (caps.TvSearchTvRageAvailable && series.TvRageId > 0)
            {
                ids.RId = series.TvRageId;
                any = true;
            }

            if (caps.TvSearchTvMazeAvailable && series.TvMazeId > 0)
            {
                ids.TvMazeId = series.TvMazeId;
                any = true;
            }

            if (caps.TvSearchTmdbAvailable && series.TmdbId > 0)
            {
                ids.TmdbId = series.TmdbId;
                any = true;
            }

            if (any)
            {
                into.Add(ids);
            }
        }

        private static void AddTitles(List<EngineCriteria> into, SearchCriteriaBase criteria, IndexerCapabilities caps, int[] categories, int season, string episode)
        {
            // A tv search needs a text field to put the title in; without one nothing sensible can be sent.
            if (!HasTvText(caps))
            {
                return;
            }

            into.AddRange(Titles(criteria, caps).Select(t => (EngineCriteria)Tv(t, categories, season, episode)));
        }

        private static IEnumerable<string> Titles(SearchCriteriaBase criteria, IndexerCapabilities caps)
        {
            var titles = caps.SupportsRawSearch ? criteria.AllSceneTitles : criteria.CleanSceneTitles;

            return titles.Select(t => t.Replace('+', ' ').Trim()).Where(t => t.IsNotNullOrWhiteSpace()).Distinct(StringComparer.InvariantCultureIgnoreCase);
        }

        private static EngineTvCriteria Tv(string term, int[] categories, int season, string episode)
        {
            return new EngineTvCriteria { SearchType = "tvsearch", SearchTerm = term, Categories = categories, Season = season, Episode = episode };
        }

        private static EngineBasicCriteria Basic(string term, int[] categories)
        {
            return new EngineBasicCriteria { SearchType = "search", SearchTerm = term, Categories = categories };
        }

        private static int[] Categories(SearchCriteriaBase criteria, EngineSearchOptions options)
        {
            return criteria.Series?.SeriesType is SeriesTypes.Anime ? options.AnimeCategories : options.Categories;
        }

        private static int[] AllCategories(EngineSearchOptions options)
        {
            return options.Categories.Concat(options.AnimeCategories).Distinct().ToArray();
        }

        private static List<EngineCriteria> Tier(params EngineCriteria[] criteria) => criteria.ToList();

        private static List<List<EngineCriteria>> Tiers(params List<EngineCriteria>[] tiers)
        {
            return tiers.Where(t => t.Any()).ToList();
        }
    }
}
