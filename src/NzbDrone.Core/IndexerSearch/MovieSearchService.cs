using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.IndexerSearch
{
    public class MovieSearchService : IExecute<MoviesSearchCommand>, IExecute<MissingMoviesSearchCommand>, IExecute<CutoffUnmetMoviesSearchCommand>
    {
        private readonly IMovieService _movieService;
        private readonly IMovieCutoffService _movieCutoffService;
        private readonly IQueueService _queueService;
        private readonly ISearchForMovieReleases _releaseSearchService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly Logger _logger;

        public MovieSearchService(IMovieService movieService,
                                  IMovieCutoffService movieCutoffService,
                                  IQueueService queueService,
                                  ISearchForMovieReleases releaseSearchService,
                                  IProcessDownloadDecisions processDownloadDecisions,
                                  Logger logger)
        {
            _movieService = movieService;
            _movieCutoffService = movieCutoffService;
            _queueService = queueService;
            _releaseSearchService = releaseSearchService;
            _processDownloadDecisions = processDownloadDecisions;
            _logger = logger;
        }

        public void Execute(MoviesSearchCommand message)
        {
            var userInvokedSearch = message.Trigger == CommandTrigger.Manual;

            // An automatic search only looks for monitored movies that are out; one you ask for is always searched.
            var movies = _movieService.GetMovies(message.MovieIds)
                .Where(m => (m.Monitored && m.IsAvailable()) || userInvokedSearch)
                .ToList();

            SearchForBulkMovies(movies, userInvokedSearch).GetAwaiter().GetResult();
        }

        public void Execute(MissingMoviesSearchCommand message)
        {
            var movies = _movieService.MoviesWithoutFiles(AllMonitored()).Records.ToList();

            SearchForBulkMovies(NotInQueue(movies), message.Trigger == CommandTrigger.Manual).GetAwaiter().GetResult();
        }

        public void Execute(CutoffUnmetMoviesSearchCommand message)
        {
            var movies = _movieCutoffService.MoviesWhereCutoffUnmet(AllMonitored()).Records.ToList();

            SearchForBulkMovies(NotInQueue(movies), message.Trigger == CommandTrigger.Manual).GetAwaiter().GetResult();
        }

        private static PagingSpec<Movie> AllMonitored()
        {
            var pagingSpec = new PagingSpec<Movie>
            {
                Page = 1,
                PageSize = 100000,
                SortDirection = SortDirection.Ascending,
                SortKey = "Id"
            };

            pagingSpec.FilterExpressions.Add(v => v.Monitored == true);

            return pagingSpec;
        }

        // A movie that is already downloading doesn't need another search.
        private List<Movie> NotInQueue(List<Movie> movies)
        {
            var queued = _queueService.GetQueue().Where(q => q.Movie != null).Select(q => q.Movie.Id).ToHashSet();

            return movies.Where(m => !queued.Contains(m.Id)).ToList();
        }

        public async Task SearchForBulkMovies(List<Movie> movies, bool userInvokedSearch)
        {
            _logger.ProgressInfo("Performing search for {0} movies", movies.Count);

            var downloadedCount = 0;

            // The movies searched longest ago go first.
            foreach (var movie in movies.GroupBy(m => m.Id).OrderBy(g => g.Min(m => m.LastSearchTime ?? DateTime.MinValue)))
            {
                List<DownloadDecision> decisions;

                try
                {
                    decisions = await _releaseSearchService.MovieSearch(movie.Key, userInvokedSearch, false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to search for movie: [{0}]", movie.Key);
                    continue;
                }

                var processed = await _processDownloadDecisions.ProcessDecisions(decisions);

                downloadedCount += processed.Grabbed.Count;
            }

            _logger.ProgressInfo("Completed search for {0} movies. {1} reports downloaded.", movies.Count, downloadedCount);
        }
    }
}
