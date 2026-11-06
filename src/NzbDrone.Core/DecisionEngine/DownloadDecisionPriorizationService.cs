using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.Profiles.Delay;

namespace NzbDrone.Core.DecisionEngine
{
    public interface IPrioritizeDownloadDecision
    {
        List<DownloadDecision> PrioritizeDecisions(List<DownloadDecision> decisions);
    }

    public class DownloadDecisionPriorizationService : IPrioritizeDownloadDecision
    {
        private readonly IConfigService _configService;
        private readonly IDelayProfileService _delayProfileService;

        public DownloadDecisionPriorizationService(IConfigService configService, IDelayProfileService delayProfileService)
        {
            _configService = configService;
            _delayProfileService = delayProfileService;
        }

        public List<DownloadDecision> PrioritizeDecisions(List<DownloadDecision> decisions)
        {
            var episodeComparer = new DownloadDecisionComparer(_configService, _delayProfileService);
            var movieComparer = new MovieDownloadDecisionComparer(_configService, _delayProfileService);

            // Each series or movie is ordered on its own, best first; decisions that matched nothing come last.
            var seriesDecisions = decisions.Where(c => c.RemoteEpisode?.Series != null)
                                           .GroupBy(c => c.RemoteEpisode.Series.Id)
                                           .SelectMany(g => g.OrderByDescending(decision => decision, episodeComparer));

            var movieDecisions = decisions.Where(c => c.RemoteMovie?.Movie != null)
                                          .GroupBy(c => c.RemoteMovie.Movie.Id)
                                          .SelectMany(g => g.OrderByDescending(decision => decision, movieComparer));

            var unmatched = decisions.Where(c => c.RemoteEpisode?.Series == null && c.RemoteMovie?.Movie == null);

            return seriesDecisions.Concat(movieDecisions).Concat(unmatched).ToList();
        }
    }
}
