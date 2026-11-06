using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Movies.Specifications
{
    public class MovieAvailabilitySpecification : IMovieDecisionEngineSpecification
    {
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MovieAvailabilitySpecification(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, MovieReleaseDecisionInformation information)
        {
            if (information.UserInvokedSearch)
            {
                _logger.Debug("Skipping availability check during a search the user asked for");
                return DownloadSpecDecision.Accept();
            }

            var availabilityDelay = _configService.AvailabilityDelay;

            if (!subject.Movie.IsAvailable(availabilityDelay))
            {
                return DownloadSpecDecision.Reject(DownloadRejectionReason.Availability, "Movie {0} will only be considered available {1} days after {2}", subject.Movie, availabilityDelay, subject.Movie.MinimumAvailability.ToString());
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
