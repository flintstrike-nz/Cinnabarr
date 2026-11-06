using NLog;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Movies.Specifications
{
    public class MovieBlocklistSpecification : IMovieDecisionEngineSpecification
    {
        private readonly IMovieBlocklistService _blocklistService;
        private readonly Logger _logger;

        public MovieBlocklistSpecification(IMovieBlocklistService blocklistService, Logger logger)
        {
            _blocklistService = blocklistService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, MovieReleaseDecisionInformation information)
        {
            if (_blocklistService.Blocklisted(subject.Movie.Id, subject.Release))
            {
                _logger.Debug("{0} is blocklisted, rejecting.", subject.Release.Title);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.Blocklisted, "Release is blocklisted");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
