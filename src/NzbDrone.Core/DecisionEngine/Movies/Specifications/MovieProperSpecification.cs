using System;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Movies.Specifications
{
    public class MovieProperSpecification : IMovieDecisionEngineSpecification
    {
        private readonly UpgradableSpecification _upgradableSpecification;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MovieProperSpecification(UpgradableSpecification upgradableSpecification, IConfigService configService, Logger logger)
        {
            _upgradableSpecification = upgradableSpecification;
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, MovieReleaseDecisionInformation information)
        {
            if (information.SearchedMovie != null)
            {
                return DownloadSpecDecision.Accept();
            }

            var downloadPropersAndRepacks = _configService.DownloadPropersAndRepacks;

            if (downloadPropersAndRepacks == ProperDownloadTypes.DoNotPrefer)
            {
                _logger.Debug("Propers are not preferred, skipping check");
                return DownloadSpecDecision.Accept();
            }

            var file = subject.Movie.MovieFile;

            if (file == null)
            {
                return DownloadSpecDecision.Accept();
            }

            if (_upgradableSpecification.IsRevisionUpgrade(file.Quality, subject.ParsedMovieInfo.Quality))
            {
                if (downloadPropersAndRepacks == ProperDownloadTypes.DoNotUpgrade)
                {
                    _logger.Debug("Auto downloading of propers is disabled");
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.PropersDisabled, "Proper downloading is disabled");
                }

                if (file.DateAdded < DateTime.Today.AddDays(-7))
                {
                    _logger.Debug("Proper for old file, rejecting: {0}", subject);
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.ProperForOldFile, "Proper for old file");
                }
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
