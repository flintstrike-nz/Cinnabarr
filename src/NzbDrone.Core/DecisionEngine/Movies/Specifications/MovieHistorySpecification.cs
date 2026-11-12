using System;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.History;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Movies.Specifications
{
    public class MovieHistorySpecification : IMovieDecisionEngineSpecification
    {
        private readonly IMovieHistoryService _historyService;
        private readonly UpgradableSpecification _upgradableSpecification;
        private readonly ICustomFormatCalculationService _formatService;
        private readonly IConfigService _configService;
        private readonly IMovieRacePolicy _racePolicy;
        private readonly Logger _logger;

        public MovieHistorySpecification(IMovieHistoryService historyService,
                                         UpgradableSpecification upgradableSpecification,
                                         ICustomFormatCalculationService formatService,
                                         IConfigService configService,
                                         IMovieRacePolicy racePolicy,
                                         Logger logger)
        {
            _historyService = historyService;
            _upgradableSpecification = upgradableSpecification;
            _formatService = formatService;
            _configService = configService;
            _racePolicy = racePolicy;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public virtual DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, MovieReleaseDecisionInformation information)
        {
            if (information.SearchedMovie != null)
            {
                _logger.Debug("Skipping history check during search");
                return DownloadSpecDecision.Accept();
            }

            var cdhEnabled = _configService.EnableCompletedDownloadHandling;
            var qualityProfile = subject.Movie.QualityProfile;

            _logger.Debug("Checking current status of movie [{0}] in history", subject.Movie.Id);
            var mostRecent = _historyService.MostRecentForMovie(subject.Movie.Id, subject.Movie.VersionSlotId);

            if (mostRecent == null || mostRecent.EventType != MovieHistoryEventType.Grabbed)
            {
                return DownloadSpecDecision.Accept();
            }

            // The grab is still downloading and racing is allowed: another release may download alongside it.
            if (_racePolicy.MayRace(subject))
            {
                return DownloadSpecDecision.Accept();
            }

            var recent = mostRecent.Date.After(DateTime.UtcNow.AddHours(-12));

            if (!recent && cdhEnabled)
            {
                return DownloadSpecDecision.Accept();
            }

            var customFormats = _formatService.ParseCustomFormat(mostRecent, subject.Movie);

            var cutoffUnmet = _upgradableSpecification.CutoffNotMet(qualityProfile, mostRecent.Quality, customFormats, subject.ParsedMovieInfo.Quality);

            var upgradeableRejectReason = _upgradableSpecification.IsUpgradable(qualityProfile, mostRecent.Quality, customFormats, subject.ParsedMovieInfo.Quality, subject.CustomFormats);

            if (!cutoffUnmet)
            {
                if (recent)
                {
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.HistoryRecentCutoffMet, "Recent grab event in history already meets cutoff: {0}", mostRecent.Quality);
                }

                return DownloadSpecDecision.Reject(DownloadRejectionReason.HistoryCdhDisabledCutoffMet, "CDH is disabled and grab event in history already meets cutoff: {0}", mostRecent.Quality);
            }

            var rejectionSubject = recent ? "Recent" : "CDH is disabled and";

            switch (upgradeableRejectReason)
            {
                case UpgradeableRejectReason.BetterQuality:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.HistoryHigherPreference, "{0} grab event in history is of equal or higher preference: {1}", rejectionSubject, mostRecent.Quality);

                case UpgradeableRejectReason.BetterRevision:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.HistoryHigherRevision, "{0} grab event in history is of equal or higher revision: {1}", rejectionSubject, mostRecent.Quality.Revision);

                case UpgradeableRejectReason.QualityCutoff:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.HistoryCutoffMet, "{0} grab event in history meets quality cutoff: {1}", rejectionSubject, qualityProfile.Items[qualityProfile.GetIndex(qualityProfile.Cutoff).Index]);

                case UpgradeableRejectReason.CustomFormatCutoff:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.HistoryCustomFormatCutoffMet, "{0} grab event in history meets Custom Format cutoff: {1}", rejectionSubject, qualityProfile.CutoffFormatScore);

                case UpgradeableRejectReason.CustomFormatScore:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.HistoryCustomFormatScore, "{0} grab event in history has an equal or higher Custom Format score: {1}", rejectionSubject, qualityProfile.CalculateCustomFormatScore(customFormats));

                case UpgradeableRejectReason.MinCustomFormatScore:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.HistoryCustomFormatScoreIncrement, "{0} grab event in history has Custom Format score within Custom Format score increment: {1}", rejectionSubject, qualityProfile.MinUpgradeFormatScore);

                case UpgradeableRejectReason.UpgradesNotAllowed:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.HistoryUpgradesNotAllowed, "{0} grab event in history and Quality Profile '{1}' does not allow upgrades", rejectionSubject, qualityProfile.Name);
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
