using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Movies.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Movies
{
    public interface IMakeMovieDownloadDecision
    {
        List<DownloadDecision> GetRssDecision(List<ReleaseInfo> reports, bool pushedRelease = false);
        List<DownloadDecision> GetSearchDecision(List<ReleaseInfo> reports, Movie searchedMovie, bool userInvokedSearch = false, bool interactiveSearch = false);
    }

    /// <summary>Works out, for each release found for a movie, whether it should be downloaded and why not if it should not.</summary>
    public class MovieDownloadDecisionMaker : IMakeMovieDownloadDecision
    {
        private readonly IEnumerable<IMovieDecisionEngineSpecification> _specifications;
        private readonly IMovieParsingService _parsingService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IVersionSlotService _versionSlotService;
        private readonly Logger _logger;

        public MovieDownloadDecisionMaker(IEnumerable<IMovieDecisionEngineSpecification> specifications,
                                          IMovieParsingService parsingService,
                                          ICustomFormatCalculationService formatCalculator,
                                          IVersionSlotService versionSlotService,
                                          Logger logger)
        {
            _specifications = specifications;
            _parsingService = parsingService;
            _formatCalculator = formatCalculator;
            _versionSlotService = versionSlotService;
            _logger = logger;
        }

        public List<DownloadDecision> GetRssDecision(List<ReleaseInfo> reports, bool pushedRelease = false)
        {
            return GetDecisions(reports, pushedRelease, null, false, false).ToList();
        }

        public List<DownloadDecision> GetSearchDecision(List<ReleaseInfo> reports, Movie searchedMovie, bool userInvokedSearch = false, bool interactiveSearch = false)
        {
            return GetDecisions(reports, false, searchedMovie, userInvokedSearch, interactiveSearch).ToList();
        }

        private IEnumerable<DownloadDecision> GetDecisions(List<ReleaseInfo> reports, bool pushedRelease, Movie searchedMovie, bool userInvokedSearch, bool interactiveSearch)
        {
            _logger.ProgressInfo(reports.Any() ? "Processing {0} releases" : "No results found", reports.Count);

            var source = GetReleaseSource(pushedRelease, searchedMovie, userInvokedSearch, interactiveSearch);
            var information = new MovieReleaseDecisionInformation(pushedRelease, searchedMovie, userInvokedSearch);
            var reportNumber = 1;

            foreach (var report in reports)
            {
                DownloadDecision decision = null;
                _logger.ProgressTrace("Processing release {0}/{1}", reportNumber, reports.Count);
                _logger.Debug("Processing release '{0}' from '{1}'", report.Title, report.Indexer);

                try
                {
                    var parsed = MovieParser.ParseMovieTitle(report.Title);

                    if (parsed != null && !parsed.PrimaryMovieTitle.IsNullOrWhiteSpace())
                    {
                        var remoteMovie = _parsingService.Map(parsed, report.ImdbId, report.TmdbId, searchedMovie);
                        remoteMovie.Release = report;
                        remoteMovie.ReleaseSource = source;

                        if (remoteMovie.Movie == null)
                        {
                            decision = new DownloadDecision(remoteMovie, new DownloadRejection(DownloadRejectionReason.UnknownMovie, pushedRelease
                                ? "Unknown Movie. Unable to match to existing movie in Library using release title."
                                : "Unknown Movie. Unable to match to correct movie using release title."));
                        }
                        else
                        {
                            remoteMovie.CustomFormats = _formatCalculator.ParseCustomFormat(remoteMovie, remoteMovie.Release.Size);
                            remoteMovie.CustomFormatScore = remoteMovie.Movie.QualityProfile?.CalculateCustomFormatScore(remoteMovie.CustomFormats) ?? 0;

                            _logger.Trace("Custom Format Score of '{0}' [{1}] calculated for '{2}'", remoteMovie.CustomFormatScore, remoteMovie.CustomFormats?.ConcatToString(), report.Title);

                            remoteMovie.DownloadAllowed = true;
                            decision = GetDecisionForReport(remoteMovie, information);
                        }
                    }
                    else if (searchedMovie != null)
                    {
                        var remoteMovie = new RemoteMovie
                        {
                            Release = report,
                            ReleaseSource = source,
                            ParsedMovieInfo = parsed ?? new ParsedMovieInfo
                            {
                                Languages = MovieLanguageParser.ParseLanguages(report.Title),
                                Quality = MovieQualityParser.ParseQuality(report.Title)
                            }
                        };

                        remoteMovie.Languages = remoteMovie.ParsedMovieInfo.Languages;

                        decision = new DownloadDecision(remoteMovie, new DownloadRejection(DownloadRejectionReason.UnableToParse, "Unable to parse release"));
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't process release.");

                    decision = new DownloadDecision(new RemoteMovie { Release = report, ReleaseSource = source }, new DownloadRejection(DownloadRejectionReason.Error, "Unexpected error processing release"));
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

                    // A release found by RSS is also decided for each of the movie's other versions, each with its own
                    // profile; the default version's decision comes first. When more than one accepts the release, only one
                    // of them is grabbed (the release is not downloaded twice).
                    if (searchedMovie == null && !pushedRelease && decision.RemoteMovie?.Movie != null)
                    {
                        foreach (var versionDecision in GetVersionDecisions(decision.RemoteMovie, information))
                        {
                            yield return versionDecision;
                        }
                    }
                }
            }
        }

        private IEnumerable<DownloadDecision> GetVersionDecisions(RemoteMovie remoteMovie, MovieReleaseDecisionInformation information)
        {
            var decisions = new List<DownloadDecision>();

            try
            {
                foreach (var slot in _versionSlotService.GetByMovie(remoteMovie.Movie.Id))
                {
                    var view = _versionSlotService.GetView(remoteMovie.Movie, slot.Id);
                    var forVersion = new RemoteMovie
                    {
                        Release = remoteMovie.Release,
                        ReleaseSource = remoteMovie.ReleaseSource,
                        ParsedMovieInfo = remoteMovie.ParsedMovieInfo,
                        MovieMatchType = remoteMovie.MovieMatchType,
                        MovieRequested = remoteMovie.MovieRequested,
                        Languages = remoteMovie.Languages,
                        CustomFormats = remoteMovie.CustomFormats,
                        Movie = view,
                        DownloadAllowed = true
                    };

                    forVersion.CustomFormatScore = view.QualityProfile?.CalculateCustomFormatScore(forVersion.CustomFormats) ?? 0;

                    decisions.Add(GetDecisionForReport(forVersion, information));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't process release '{0}' for the versions of '{1}'.", remoteMovie.Release.Title, remoteMovie.Movie.Title);
            }

            return decisions;
        }

        private DownloadDecision GetDecisionForReport(RemoteMovie remoteMovie, MovieReleaseDecisionInformation information)
        {
            var reasons = Array.Empty<DownloadRejection>();

            // Cheap checks run first; the later groups only run if everything before them passed.
            foreach (var specifications in _specifications.GroupBy(v => v.Priority).OrderBy(v => v.Key))
            {
                reasons = specifications.Select(c => EvaluateSpec(c, remoteMovie, information))
                                        .Where(c => c != null)
                                        .ToArray();

                if (reasons.Any())
                {
                    break;
                }
            }

            return new DownloadDecision(remoteMovie, reasons.ToArray());
        }

        private DownloadRejection EvaluateSpec(IMovieDecisionEngineSpecification spec, RemoteMovie remoteMovie, MovieReleaseDecisionInformation information)
        {
            try
            {
                var result = spec.IsSatisfiedBy(remoteMovie, information);

                if (!result.Accepted)
                {
                    return new DownloadRejection(result.Reason, result.Message, spec.Type);
                }
            }
            catch (NotImplementedException)
            {
                _logger.Trace("Spec " + spec.GetType().Name + " does not care about movies.");
            }
            catch (Exception e)
            {
                e.Data.Add("report", remoteMovie.Release.ToJson());
                e.Data.Add("parsed", remoteMovie.ParsedMovieInfo.ToJson());
                _logger.Error(e, "Couldn't evaluate decision on {0}, with spec: {1}", remoteMovie.Release.Title, spec.GetType().Name);

                return new DownloadRejection(DownloadRejectionReason.DecisionError, $"{spec.GetType().Name}: {e.Message}");
            }

            return null;
        }

        private static ReleaseSourceType GetReleaseSource(bool pushedRelease, Movie searchedMovie, bool userInvokedSearch, bool interactiveSearch)
        {
            if (searchedMovie == null)
            {
                return pushedRelease ? ReleaseSourceType.ReleasePush : ReleaseSourceType.Rss;
            }

            if (interactiveSearch)
            {
                return ReleaseSourceType.InteractiveSearch;
            }

            return userInvokedSearch ? ReleaseSourceType.UserInvokedSearch : ReleaseSourceType.Search;
        }
    }
}
