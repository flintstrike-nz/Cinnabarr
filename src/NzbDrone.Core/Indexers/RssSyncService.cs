using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers
{
    public class RssSyncService : IExecute<RssSyncCommand>
    {
        private readonly IFetchAndParseRss _rssFetcherAndParser;
        private readonly IMakeDownloadDecision _downloadDecisionMaker;
        private readonly IMakeMovieDownloadDecision _movieDecisionMaker;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly IPendingReleaseService _pendingReleaseService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public RssSyncService(IFetchAndParseRss rssFetcherAndParser,
                              IMakeDownloadDecision downloadDecisionMaker,
                              IMakeMovieDownloadDecision movieDecisionMaker,
                              IProcessDownloadDecisions processDownloadDecisions,
                              IPendingReleaseService pendingReleaseService,
                              IEventAggregator eventAggregator,
                              Logger logger)
        {
            _rssFetcherAndParser = rssFetcherAndParser;
            _downloadDecisionMaker = downloadDecisionMaker;
            _movieDecisionMaker = movieDecisionMaker;
            _processDownloadDecisions = processDownloadDecisions;
            _pendingReleaseService = pendingReleaseService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        private async Task<ProcessedDecisions> Sync()
        {
            _logger.ProgressInfo("Starting RSS Sync");

            var rssReleases = await _rssFetcherAndParser.Fetch();
            var pendingReleases = _pendingReleaseService.GetPending();

            var reports = rssReleases.Concat(pendingReleases).ToList();
            var decisions = _downloadDecisionMaker.GetRssDecision(reports);
            var processed = await _processDownloadDecisions.ProcessDecisions(decisions);

            // Movie releases are decided and grabbed on their own; the completion event only describes the series side.
            var movieReleases = await _rssFetcherAndParser.FetchMovies() ?? new List<ReleaseInfo>();
            var movieGrabbed = 0;

            if (movieReleases.Any())
            {
                var movieDecisions = _movieDecisionMaker.GetRssDecision(movieReleases);

                if (movieDecisions.Any())
                {
                    movieGrabbed = (await _processDownloadDecisions.ProcessDecisions(movieDecisions)).Grabbed.Count;
                }
            }

            var found = reports.Count + movieReleases.Count;
            var grabbed = processed.Grabbed.Count + movieGrabbed;

            if (processed.Pending.Any())
            {
                _logger.ProgressInfo("RSS Sync Completed. Reports found: {0}, Reports grabbed: {1}, Reports pending: {2}", found, grabbed, processed.Pending.Count);
            }
            else
            {
                _logger.ProgressInfo("RSS Sync Completed. Reports found: {0}, Reports grabbed: {1}", found, grabbed);
            }

            return processed;
        }

        public void Execute(RssSyncCommand message)
        {
            var processed = Sync().GetAwaiter().GetResult();

            _eventAggregator.PublishEvent(new RssSyncCompleteEvent(processed));
        }
    }
}
