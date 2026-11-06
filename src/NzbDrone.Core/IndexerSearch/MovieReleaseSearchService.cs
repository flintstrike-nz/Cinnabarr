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
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.IndexerSearch
{
    public interface ISearchForMovieReleases
    {
        Task<List<DownloadDecision>> MovieSearch(int movieId, bool userInvokedSearch, bool interactiveSearch);
        Task<List<DownloadDecision>> MovieSearch(Movie movie, bool userInvokedSearch, bool interactiveSearch);
    }

    public class MovieReleaseSearchService : ISearchForMovieReleases
    {
        private readonly IIndexerFactory _indexerFactory;
        private readonly IMovieService _movieService;
        private readonly IMakeMovieDownloadDecision _makeDownloadDecision;
        private readonly IIndexerQueryService _indexerQueryService;
        private readonly Logger _logger;

        public MovieReleaseSearchService(IIndexerFactory indexerFactory,
                                         IMovieService movieService,
                                         IMakeMovieDownloadDecision makeDownloadDecision,
                                         IIndexerQueryService indexerQueryService,
                                         Logger logger)
        {
            _indexerFactory = indexerFactory;
            _movieService = movieService;
            _makeDownloadDecision = makeDownloadDecision;
            _indexerQueryService = indexerQueryService;
            _logger = logger;
        }

        public Task<List<DownloadDecision>> MovieSearch(int movieId, bool userInvokedSearch, bool interactiveSearch)
        {
            return MovieSearch(_movieService.GetMovie(movieId), userInvokedSearch, interactiveSearch);
        }

        public async Task<List<DownloadDecision>> MovieSearch(Movie movie, bool userInvokedSearch, bool interactiveSearch)
        {
            var criteria = new MovieSearchCriteria
            {
                Movie = movie,
                UserInvokedSearch = userInvokedSearch,
                InteractiveSearch = interactiveSearch,
                SceneTitles = GetSearchTitles(movie)
            };

            var indexers = interactiveSearch ? _indexerFactory.InteractiveSearchEnabled() : _indexerFactory.AutomaticSearchEnabled();

            // Untagged indexers search everything; tagged indexers only search movies sharing a tag.
            indexers = indexers.Where(i => i.Definition.Tags.Empty() || i.Definition.Tags.Intersect(movie.Tags).Any()).ToList();

            _logger.ProgressInfo("Searching indexers for {0}. {1} active indexers", criteria, indexers.Count);

            var batch = await Task.WhenAll(indexers.Select(i => DispatchIndexer(i, criteria)));
            var reports = batch.SelectMany(x => x).ToList();

            _logger.ProgressDebug("Total of {0} reports were found for {1} from {2} indexers", reports.Count, criteria, indexers.Count);

            if (indexers.Any())
            {
                movie.LastSearchTime = DateTime.UtcNow;
                _movieService.UpdateLastSearchTime(movie);
            }

            var decisions = _makeDownloadDecision.GetSearchDecision(reports, movie, userInvokedSearch, interactiveSearch);

            return DeDupeDecisions(decisions);
        }

        private static List<string> GetSearchTitles(Movie movie)
        {
            var metadata = movie.MovieMetadata.Value;

            return new[] { metadata.Title, metadata.OriginalTitle }
                .Concat((metadata.AlternativeTitles ?? new List<Movies.AlternativeTitles.AlternativeTitle>()).Select(t => t.Title))
                .Where(t => t.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();
        }

        private async Task<IList<ReleaseInfo>> DispatchIndexer(IIndexer indexer, MovieSearchCriteria criteria)
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

        private static List<DownloadDecision> DeDupeDecisions(IEnumerable<DownloadDecision> decisions)
        {
            // The same release can come back from several indexers; keep the one with the fewest rejections.
            return decisions
                .GroupBy(d => d.RemoteMovie?.Release?.Guid ?? Guid.NewGuid().ToString())
                .Select(g => g.OrderBy(d => d.Rejections.Count()).ThenBy(d => d.RemoteMovie?.Release?.IndexerPriority ?? IndexerDefinition.DefaultPriority).First())
                .ToList();
        }
    }
}
