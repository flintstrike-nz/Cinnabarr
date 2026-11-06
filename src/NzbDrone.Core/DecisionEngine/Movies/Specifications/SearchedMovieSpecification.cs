using NLog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Movies.Specifications
{
    public class SearchedMovieSpecification : IMovieDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public SearchedMovieSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, MovieReleaseDecisionInformation information)
        {
            var searched = information.SearchedMovie;

            if (searched == null)
            {
                return DownloadSpecDecision.Accept();
            }

            _logger.Debug("Checking if movie matches searched movie");

            if (subject.Movie.Id != searched.Id)
            {
                _logger.Debug("Movie {0} does not match {1}", subject.Movie, searched);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.WrongMovie, "Wrong movie");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
