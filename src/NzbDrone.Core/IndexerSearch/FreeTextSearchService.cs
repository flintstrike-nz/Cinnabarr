using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.History;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.IndexerSearch
{
    public interface ISearchForFreeText
    {
        // Searches every interactive-search indexer for the text and decides, for each release found, whether it is an
        // episode of a series or a movie in the library. A release that matches neither comes back rejected as unknown.
        Task<List<DownloadDecision>> Search(string query);
    }

    public class FreeTextSearchService : ISearchForFreeText
    {
        private readonly IIndexerFactory _indexerFactory;
        private readonly IMakeDownloadDecision _seriesDecisionMaker;
        private readonly IMakeMovieDownloadDecision _movieDecisionMaker;
        private readonly IIndexerQueryService _indexerQueryService;
        private readonly Logger _logger;

        public FreeTextSearchService(IIndexerFactory indexerFactory,
                                     IMakeDownloadDecision seriesDecisionMaker,
                                     IMakeMovieDownloadDecision movieDecisionMaker,
                                     IIndexerQueryService indexerQueryService,
                                     Logger logger)
        {
            _indexerFactory = indexerFactory;
            _seriesDecisionMaker = seriesDecisionMaker;
            _movieDecisionMaker = movieDecisionMaker;
            _indexerQueryService = indexerQueryService;
            _logger = logger;
        }

        public async Task<List<DownloadDecision>> Search(string query)
        {
            var criteria = new FreeTextSearchCriteria { Query = query?.Trim() };

            if (criteria.Query.IsNullOrWhiteSpace())
            {
                return new List<DownloadDecision>();
            }

            var indexers = _indexerFactory.InteractiveSearchEnabled();

            _logger.ProgressInfo("Searching indexers for {0}. {1} active indexers", criteria, indexers.Count);

            var batch = await Task.WhenAll(indexers.Select(i => DispatchIndexer(i, criteria)));

            // The same release can come from several indexers; the first copy of each guid is enough to decide on.
            var reports = batch.SelectMany(x => x)
                .GroupBy(r => r.Guid)
                .Select(g => g.First())
                .ToList();

            _logger.ProgressDebug("Total of {0} reports were found for {1} from {2} indexers", reports.Count, criteria, indexers.Count);

            var seriesDecisions = _seriesDecisionMaker.GetRssDecision(reports).ToLookup(d => d.RemoteEpisode?.Release?.Guid);
            var movieDecisions = _movieDecisionMaker.GetRssDecision(reports).ToLookup(d => d.RemoteMovie?.Release?.Guid);

            return reports
                .Select(r => Pick(seriesDecisions[r.Guid].FirstOrDefault(), movieDecisions[r.Guid].FirstOrDefault()))
                .Where(d => d != null)
                .ToList();
        }

        // Whichever of the two decisions found something in the library wins. A release that parses as episodes goes to
        // the series when both did; one that matched neither is kept as the series (unknown) decision.
        public static DownloadDecision Pick(DownloadDecision series, DownloadDecision movie)
        {
            var seriesMatched = series?.RemoteEpisode?.Series != null;
            var movieMatched = movie?.RemoteMovie?.Movie != null;

            if (seriesMatched && movieMatched)
            {
                return LooksLikeEpisodes(series.RemoteEpisode.ParsedEpisodeInfo) ? series : movie;
            }

            if (movieMatched)
            {
                return movie;
            }

            // Neither found a match in the library: keep the one whose parse looks right, so the page can offer to add it.
            if (!LooksLikeEpisodes(series?.RemoteEpisode?.ParsedEpisodeInfo) && movie?.RemoteMovie?.ParsedMovieInfo != null)
            {
                return movie;
            }

            return series ?? movie;
        }

        private static bool LooksLikeEpisodes(ParsedEpisodeInfo parsed)
        {
            return parsed != null && (parsed.EpisodeNumbers.Any() || parsed.AbsoluteEpisodeNumbers.Any() || parsed.IsDaily || parsed.FullSeason);
        }

        private async Task<IList<ReleaseInfo>> DispatchIndexer(IIndexer indexer, FreeTextSearchCriteria criteria)
        {
            try
            {
                return await _indexerQueryService.Track(indexer, IndexerQuerySource.Search, criteria.ToString(), () => indexer.Fetch(criteria));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while searching for {0}", criteria);
            }

            return Array.Empty<ReleaseInfo>();
        }
    }
}
