using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download.Aggregation;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv.Versions;

namespace NzbDrone.Core.DecisionEngine
{
    public interface IMakeDownloadDecision
    {
        List<DownloadDecision> GetRssDecision(List<ReleaseInfo> reports, bool pushedRelease = false);
        List<DownloadDecision> GetSearchDecision(List<ReleaseInfo> reports, SearchCriteriaBase searchCriteriaBase);

        // The releases decided for one extra version of the series they match (a pushed release for that version).
        List<DownloadDecision> GetVersionDecision(List<ReleaseInfo> reports, int versionSlotId, bool pushedRelease = false);
    }

    public class DownloadDecisionMaker : IMakeDownloadDecision
    {
        private readonly IEnumerable<IDownloadDecisionEngineSpecification> _specifications;
        private readonly IParsingService _parsingService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IRemoteEpisodeAggregationService _aggregationService;
        private readonly ISceneMappingService _sceneMappingService;
        private readonly ISeriesVersionSlotService _versionSlotService;
        private readonly Logger _logger;

        public DownloadDecisionMaker(IEnumerable<IDownloadDecisionEngineSpecification> specifications,
                                     IParsingService parsingService,
                                     ICustomFormatCalculationService formatService,
                                     IRemoteEpisodeAggregationService aggregationService,
                                     ISceneMappingService sceneMappingService,
                                     ISeriesVersionSlotService versionSlotService,
                                     Logger logger)
        {
            _specifications = specifications;
            _parsingService = parsingService;
            _formatCalculator = formatService;
            _aggregationService = aggregationService;
            _sceneMappingService = sceneMappingService;
            _versionSlotService = versionSlotService;
            _logger = logger;
        }

        public List<DownloadDecision> GetRssDecision(List<ReleaseInfo> reports, bool pushedRelease = false)
        {
            return GetDecisions(reports, pushedRelease).ToList();
        }

        public List<DownloadDecision> GetSearchDecision(List<ReleaseInfo> reports, SearchCriteriaBase searchCriteriaBase)
        {
            return GetDecisions(reports, false, searchCriteriaBase).ToList();
        }

        public List<DownloadDecision> GetVersionDecision(List<ReleaseInfo> reports, int versionSlotId, bool pushedRelease = false)
        {
            return GetDecisions(reports, pushedRelease, null, versionSlotId).ToList();
        }

        private IEnumerable<DownloadDecision> GetDecisions(List<ReleaseInfo> reports, bool pushedRelease, SearchCriteriaBase searchCriteria = null, int versionSlotId = 0)
        {
            if (reports.Any())
            {
                _logger.ProgressInfo("Processing {0} releases", reports.Count);
            }
            else
            {
                _logger.ProgressInfo("No results found");
            }

            var reportNumber = 1;

            foreach (var report in reports)
            {
                DownloadDecision decision = null;
                ParsedEpisodeInfo parsedEpisodeInfo = null;
                _logger.ProgressTrace("Processing release {0}/{1}", reportNumber, reports.Count);
                _logger.Debug("Processing release '{0}' from '{1}'", report.Title, report.Indexer);

                try
                {
                    parsedEpisodeInfo = Parser.Parser.ParseTitle(report.Title);

                    if (parsedEpisodeInfo == null || parsedEpisodeInfo.IsPossibleSpecialEpisode)
                    {
                        var specialEpisodeInfo = _parsingService.ParseSpecialEpisodeTitle(parsedEpisodeInfo, report.Title, report.TvdbId, report.TvRageId, report.ImdbId, searchCriteria);

                        if (specialEpisodeInfo != null)
                        {
                            parsedEpisodeInfo = specialEpisodeInfo;
                        }
                    }

                    if (parsedEpisodeInfo == null)
                    {
                        // Attempt to parse as a release that includes the season title without a season number
                        parsedEpisodeInfo = Parser.Parser.ParseSeasonTitle(report.Title);
                    }

                    if (parsedEpisodeInfo != null && !parsedEpisodeInfo.SeriesTitle.IsNullOrWhiteSpace())
                    {
                        var remoteEpisode = _parsingService.Map(parsedEpisodeInfo, report.TvdbId, report.TvRageId, report.ImdbId, searchCriteria);
                        remoteEpisode.Release = report;
                        remoteEpisode.ReleaseSource = GetReleaseSource(pushedRelease, searchCriteria);

                        if (remoteEpisode.Series == null)
                        {
                            var matchingTvdbId = _sceneMappingService.FindTvdbId(parsedEpisodeInfo.SeriesTitle, parsedEpisodeInfo.ReleaseTitle, parsedEpisodeInfo.SeasonNumbers.FirstOrDefault(-1));

                            if (matchingTvdbId.HasValue)
                            {
                                decision = new DownloadDecision(remoteEpisode, new DownloadRejection(DownloadRejectionReason.MatchesAnotherSeries, $"{parsedEpisodeInfo.SeriesTitle} matches an alias for series with TVDB ID: {matchingTvdbId}"));
                            }
                            else
                            {
                                decision = new DownloadDecision(remoteEpisode, new DownloadRejection(DownloadRejectionReason.UnknownSeries, "Unknown Series"));
                            }
                        }
                        else if (remoteEpisode.Episodes.Empty())
                        {
                            decision = new DownloadDecision(remoteEpisode, new DownloadRejection(DownloadRejectionReason.UnknownEpisode, "Unable to identify correct episode(s) using release name and scene mappings"));
                        }
                        else
                        {
                            // A search for one of the series' extra versions decides the release for that version: the
                            // slot's profile, monitored state and files.
                            if (searchCriteria?.Series is { VersionSlotId: > 0 } searchedVersion && searchedVersion.Id == remoteEpisode.Series.Id)
                            {
                                remoteEpisode.Series = searchedVersion;
                                remoteEpisode.Episodes = _versionSlotService.GetEpisodeViews(remoteEpisode.Episodes, searchedVersion.VersionSlotId);
                            }
                            else if (versionSlotId != 0)
                            {
                                // A release pushed for one version of the series it matches.
                                remoteEpisode.Series = _versionSlotService.GetView(remoteEpisode.Series, versionSlotId);
                                remoteEpisode.Episodes = _versionSlotService.GetEpisodeViews(remoteEpisode.Episodes, versionSlotId);
                            }

                            _aggregationService.Augment(remoteEpisode);

                            remoteEpisode.CustomFormats = _formatCalculator.ParseCustomFormat(remoteEpisode, remoteEpisode.Release.Size);
                            remoteEpisode.CustomFormatScore = remoteEpisode?.Series?.QualityProfile?.Value.CalculateCustomFormatScore(remoteEpisode.CustomFormats) ?? 0;

                            _logger.Trace("Custom Format Score of '{0}' [{1}] calculated for '{2}'", remoteEpisode.CustomFormatScore, remoteEpisode.CustomFormats?.ConcatToString(), report.Title);

                            remoteEpisode.DownloadAllowed = remoteEpisode.Episodes.Any();
                            decision = GetDecisionForReport(remoteEpisode, new ReleaseDecisionInformation(pushedRelease, searchCriteria));
                        }
                    }

                    if (searchCriteria != null)
                    {
                        if (parsedEpisodeInfo == null)
                        {
                            parsedEpisodeInfo = new ParsedEpisodeInfo
                            {
                                Languages = LanguageParser.ParseLanguages(report.Title),
                                Quality = QualityParser.ParseQuality(report.Title)
                            };
                        }

                        if (parsedEpisodeInfo.SeriesTitle.IsNullOrWhiteSpace())
                        {
                            var remoteEpisode = new RemoteEpisode
                            {
                                Release = report,
                                ReleaseSource = GetReleaseSource(pushedRelease, searchCriteria),
                                ParsedEpisodeInfo = parsedEpisodeInfo,
                                Languages = parsedEpisodeInfo.Languages,
                            };

                            decision = new DownloadDecision(remoteEpisode, new DownloadRejection(DownloadRejectionReason.UnableToParse, "Unable to parse release"));
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't process release.");

                    parsedEpisodeInfo ??= new ParsedEpisodeInfo
                    {
                        Languages = LanguageParser.ParseLanguages(report.Title),
                        Quality = QualityParser.ParseQuality(report.Title)
                    };

                    var remoteEpisode = new RemoteEpisode
                    {
                        Release = report,
                        ReleaseSource = GetReleaseSource(pushedRelease, searchCriteria),
                        ParsedEpisodeInfo = parsedEpisodeInfo,
                        Languages = parsedEpisodeInfo.Languages,
                    };

                    decision = new DownloadDecision(remoteEpisode, new DownloadRejection(DownloadRejectionReason.Error, "Unexpected error processing release"));
                }

                reportNumber++;

                if (decision != null)
                {
                    if (decision.Rejections.Any())
                    {
                        _logger.Debug("Release '{0}' from '{1}' rejected for the following reasons: {2}", report.Title, report.Indexer, string.Join(", ", decision.Rejections));
                    }
                    else
                    {
                        _logger.Debug("Release '{0}' from '{1}' accepted", report.Title, report.Indexer);
                    }

                    yield return decision;

                    // A release found by RSS is also decided for each of the series' other versions, each with its own
                    // profile; the default version's decision comes first. When more than one accepts the release, only
                    // one of them is grabbed (the release is not downloaded twice).
                    if (searchCriteria == null && versionSlotId == 0 && !pushedRelease && decision.RemoteEpisode?.Series != null && decision.RemoteEpisode.Episodes.Any())
                    {
                        foreach (var versionDecision in GetVersionDecisions(decision.RemoteEpisode, new ReleaseDecisionInformation(pushedRelease, null)))
                        {
                            yield return versionDecision;
                        }
                    }
                }
            }
        }

        private IEnumerable<DownloadDecision> GetVersionDecisions(RemoteEpisode remoteEpisode, ReleaseDecisionInformation information)
        {
            var decisions = new List<DownloadDecision>();

            try
            {
                foreach (var slot in _versionSlotService.GetBySeries(remoteEpisode.Series.Id))
                {
                    var view = _versionSlotService.GetView(remoteEpisode.Series, slot.Id);
                    var forVersion = new RemoteEpisode
                    {
                        Release = remoteEpisode.Release,
                        ReleaseSource = remoteEpisode.ReleaseSource,
                        ParsedEpisodeInfo = remoteEpisode.ParsedEpisodeInfo,
                        SeriesMatchType = remoteEpisode.SeriesMatchType,
                        MappedSeasonNumber = remoteEpisode.MappedSeasonNumber,
                        EpisodeRequested = remoteEpisode.EpisodeRequested,
                        Languages = remoteEpisode.Languages,
                        CustomFormats = remoteEpisode.CustomFormats,
                        Series = view,
                        Episodes = _versionSlotService.GetEpisodeViews(remoteEpisode.Episodes, slot.Id),
                        DownloadAllowed = true
                    };

                    forVersion.CustomFormatScore = view.QualityProfile?.Value.CalculateCustomFormatScore(forVersion.CustomFormats) ?? 0;

                    decisions.Add(GetDecisionForReport(forVersion, information));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't process release '{0}' for the versions of '{1}'.", remoteEpisode.Release.Title, remoteEpisode.Series.Title);
            }

            return decisions;
        }

        private DownloadDecision GetDecisionForReport(RemoteEpisode remoteEpisode, ReleaseDecisionInformation information)
        {
            var reasons = Array.Empty<DownloadRejection>();

            foreach (var specifications in _specifications.GroupBy(v => v.Priority).OrderBy(v => v.Key))
            {
                reasons = specifications.Select(c => EvaluateSpec(c, remoteEpisode, information))
                                        .Where(c => c != null)
                                        .ToArray();

                if (reasons.Any())
                {
                    break;
                }
            }

            return new DownloadDecision(remoteEpisode, reasons.ToArray());
        }

        private DownloadRejection EvaluateSpec(IDownloadDecisionEngineSpecification spec, RemoteEpisode remoteEpisode, ReleaseDecisionInformation information)
        {
            try
            {
                var result = spec.IsSatisfiedBy(remoteEpisode, information);

                if (!result.Accepted)
                {
                    return new DownloadRejection(result.Reason, result.Message, spec.Type);
                }
            }
            catch (Exception e)
            {
                e.Data.Add("report", remoteEpisode.Release.ToJson());
                e.Data.Add("parsed", remoteEpisode.ParsedEpisodeInfo.ToJson());
                _logger.Error(e, "Couldn't evaluate decision on {0}", remoteEpisode.Release.Title);
                return new DownloadRejection(DownloadRejectionReason.DecisionError, $"{spec.GetType().Name}: {e.Message}");
            }

            return null;
        }

        private ReleaseSourceType GetReleaseSource(bool pushedRelease, SearchCriteriaBase searchCriteria = null)
        {
            if (searchCriteria == null)
            {
                return pushedRelease ? ReleaseSourceType.ReleasePush : ReleaseSourceType.Rss;
            }

            if (searchCriteria.InteractiveSearch)
            {
                return ReleaseSourceType.InteractiveSearch;
            }
            else if (searchCriteria.UserInvokedSearch)
            {
                return ReleaseSourceType.UserInvokedSearch;
            }
            else
            {
                return ReleaseSourceType.Search;
            }
        }
    }
}
