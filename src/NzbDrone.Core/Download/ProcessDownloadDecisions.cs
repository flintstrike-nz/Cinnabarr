using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.Download
{
    public interface IProcessDownloadDecisions
    {
        Task<ProcessedDecisions> ProcessDecisions(List<DownloadDecision> decisions);
        Task<ProcessedDecisionResult> ProcessDecision(DownloadDecision decision, int? downloadClientId);
    }

    public class ProcessDownloadDecisions : IProcessDownloadDecisions
    {
        private readonly IDownloadService _downloadService;
        private readonly IPrioritizeDownloadDecision _prioritizeDownloadDecision;
        private readonly IPendingReleaseService _pendingReleaseService;
        private readonly IMovieRacePolicy _racePolicy;
        private readonly IQueueService _queueService;
        private readonly Logger _logger;

        public ProcessDownloadDecisions(IDownloadService downloadService,
                                        IPrioritizeDownloadDecision prioritizeDownloadDecision,
                                        IPendingReleaseService pendingReleaseService,
                                        IMovieRacePolicy racePolicy,
                                        IQueueService queueService,
                                        Logger logger)
        {
            _downloadService = downloadService;
            _prioritizeDownloadDecision = prioritizeDownloadDecision;
            _pendingReleaseService = pendingReleaseService;
            _racePolicy = racePolicy;
            _queueService = queueService;
            _logger = logger;
        }

        public async Task<ProcessedDecisions> ProcessDecisions(List<DownloadDecision> decisions)
        {
            var qualifiedReports = GetQualifiedReports(decisions);
            var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(qualifiedReports);
            var grabbed = new List<DownloadDecision>();
            var pending = new List<DownloadDecision>();
            var rejected = decisions.Where(d => d.Rejected).ToList();

            var pendingAddQueue = new List<Tuple<DownloadDecision, PendingReleaseReason>>();

            var usenetFailed = false;
            var torrentFailed = false;

            foreach (var report in prioritizedDecisions)
            {
                var downloadProtocol = report.Remote.Release.DownloadProtocol;

                // Skip if already grabbed
                if (IsMediaProcessed(grabbed, report) && !MayRace(grabbed, report))
                {
                    continue;
                }

                if (report.TemporarilyRejected)
                {
                    PreparePending(pendingAddQueue, grabbed, pending, report, PendingReleaseReason.Delay);
                    continue;
                }

                if ((downloadProtocol == DownloadProtocol.Usenet && usenetFailed) ||
                    (downloadProtocol == DownloadProtocol.Torrent && torrentFailed))
                {
                    PreparePending(pendingAddQueue, grabbed, pending, report, PendingReleaseReason.DownloadClientUnavailable);
                    continue;
                }

                var result = await ProcessDecisionInternal(report);

                switch (result)
                {
                    case ProcessedDecisionResult.Grabbed:
                        {
                            grabbed.Add(report);
                            break;
                        }

                    case ProcessedDecisionResult.Pending:
                        {
                            PreparePending(pendingAddQueue, grabbed, pending, report, PendingReleaseReason.Delay);
                            break;
                        }

                    case ProcessedDecisionResult.Rejected:
                        {
                            rejected.Add(report);
                            break;
                        }

                    case ProcessedDecisionResult.Failed:
                        {
                            PreparePending(pendingAddQueue, grabbed, pending, report, PendingReleaseReason.DownloadClientUnavailable);

                            if (downloadProtocol == DownloadProtocol.Usenet)
                            {
                                usenetFailed = true;
                            }
                            else if (downloadProtocol == DownloadProtocol.Torrent)
                            {
                                torrentFailed = true;
                            }

                            break;
                        }

                    case ProcessedDecisionResult.Skipped:
                        {
                            break;
                        }
                }
            }

            if (pendingAddQueue.Any())
            {
                _pendingReleaseService.AddMany(pendingAddQueue);
            }

            return new ProcessedDecisions(grabbed, pending, rejected);
        }

        public async Task<ProcessedDecisionResult> ProcessDecision(DownloadDecision decision, int? downloadClientId)
        {
            if (decision == null)
            {
                return ProcessedDecisionResult.Skipped;
            }

            if (!IsQualifiedReport(decision))
            {
                return ProcessedDecisionResult.Rejected;
            }

            if (decision.TemporarilyRejected)
            {
                if (decision.RemoteMovie != null || decision.RemoteEpisode?.Series?.VersionSlotId > 0)
                {
                    return ProcessedDecisionResult.Skipped;
                }

                _pendingReleaseService.Add(decision, PendingReleaseReason.Delay);

                return ProcessedDecisionResult.Pending;
            }

            var result = await ProcessDecisionInternal(decision, downloadClientId);

            if (result == ProcessedDecisionResult.Failed && decision.RemoteMovie == null && !(decision.RemoteEpisode?.Series?.VersionSlotId > 0))
            {
                _pendingReleaseService.Add(decision, PendingReleaseReason.DownloadClientUnavailable);
            }

            return result;
        }

        internal List<DownloadDecision> GetQualifiedReports(IEnumerable<DownloadDecision> decisions)
        {
            return decisions.Where(IsQualifiedReport).ToList();
        }

        internal bool IsQualifiedReport(DownloadDecision decision)
        {
            // Process both approved and temporarily rejected
            if (!decision.Approved && !decision.TemporarilyRejected)
            {
                return false;
            }

            return decision.RemoteMovie != null ? decision.RemoteMovie.Movie != null : decision.RemoteEpisode.Episodes.Any();
        }

        // Race mode (roadmap §4.9): a movie version that already has a release grabbed may take more, up to the limit,
        // when every indexer involved allows it. The queue counts too: releases grabbed earlier are still downloading.
        private bool MayRace(List<DownloadDecision> grabbed, DownloadDecision report)
        {
            if (report.RemoteMovie?.Movie == null)
            {
                return false;
            }

            var movie = report.RemoteMovie.Movie;
            var release = report.RemoteMovie.Release;

            bool SameRelease(ReleaseInfo other) => !string.IsNullOrEmpty(release.Guid) && other.Guid == release.Guid && other.IndexerId == release.IndexerId;
            bool SameVersion(RemoteMovie other) => other?.Movie != null && other.Movie.Id == movie.Id && other.Movie.VersionSlotId == movie.VersionSlotId;

            if (grabbed.Any(d => d.RemoteMovie != null && SameRelease(d.RemoteMovie.Release)))
            {
                return false;
            }

            var others = grabbed.Where(d => SameVersion(d.RemoteMovie)).Select(d => d.RemoteMovie).ToList();

            var queued = _queueService.GetQueue()
                                      .Where(q => SameVersion(q.RemoteMovie) && q.TrackedDownloadState != TrackedDownloadState.FailedPending)
                                      .Select(q => q.RemoteMovie)
                                      .Where(q => !others.Any(o => o.Release.Guid == q.Release.Guid && o.Release.IndexerId == q.Release.IndexerId))
                                      .ToList();

            return _racePolicy.MayJoin(report.RemoteMovie, others.Concat(queued).ToList());
        }

        private bool IsMediaProcessed(List<DownloadDecision> decisions, DownloadDecision report)
        {
            if (report.RemoteMovie != null)
            {
                var movieId = report.RemoteMovie.Movie.Id;
                var slotId = report.RemoteMovie.Movie.VersionSlotId;
                var release = report.RemoteMovie.Release;

                // One movie version takes one release, and one release goes to one version.
                return decisions.Any(d => d.RemoteMovie != null &&
                                          ((d.RemoteMovie.Movie.Id == movieId && d.RemoteMovie.Movie.VersionSlotId == slotId) ||
                                           (!string.IsNullOrEmpty(release.Guid) && d.RemoteMovie.Release.Guid == release.Guid && d.RemoteMovie.Release.IndexerId == release.IndexerId)));
            }

            var episodeIds = report.RemoteEpisode.Episodes.Select(e => e.Id).ToList();
            var seriesSlotId = report.RemoteEpisode.Series?.VersionSlotId ?? 0;
            var episodeRelease = report.RemoteEpisode.Release;

            // One version of an episode takes one release, and (when the release was decided for several versions of
            // the series) one release goes to one version.
            return decisions.Where(r => r.RemoteEpisode != null)
                            .Any(r => (!string.IsNullOrEmpty(episodeRelease?.Guid) &&
                                       r.RemoteEpisode.Release?.Guid == episodeRelease.Guid &&
                                       r.RemoteEpisode.Release.IndexerId == episodeRelease.IndexerId &&
                                       r.RemoteEpisode.Series?.Id == report.RemoteEpisode.Series?.Id &&
                                       (r.RemoteEpisode.Series?.VersionSlotId ?? 0) != seriesSlotId) ||
                                      ((r.RemoteEpisode.Series?.VersionSlotId ?? 0) == seriesSlotId &&
                                       r.RemoteEpisode.Episodes.Any(e => episodeIds.Contains(e.Id))));
        }

        private void PreparePending(List<Tuple<DownloadDecision, PendingReleaseReason>> queue, List<DownloadDecision> grabbed, List<DownloadDecision> pending, DownloadDecision report, PendingReleaseReason reason)
        {
            // If a release was already grabbed with matching episodes we should store it as a fallback
            // and filter it out the next time it is processed.
            // If a higher quality release failed to add to the download client, but a lower quality release
            // was sent to another client we still list it normally so it apparent that it'll grab next time.
            // Delayed is treated the same, but only the first is listed the subsequent items as stored as Fallback.

            // Movies are not held back: there are no delay profiles for them yet, and a release the client could not
            // take is found again by the next search or RSS sync.
            if (report.RemoteMovie != null || report.RemoteEpisode?.Series?.VersionSlotId > 0)
            {
                return;
            }

            if (IsMediaProcessed(grabbed, report) ||
                IsMediaProcessed(pending, report))
            {
                reason = PendingReleaseReason.Fallback;
            }

            queue.Add(Tuple.Create(report, reason));
            pending.Add(report);
        }

        private async Task<ProcessedDecisionResult> ProcessDecisionInternal(DownloadDecision decision, int? downloadClientId = null)
        {
            var remote = decision.Remote;
            var remoteIndexer = remote.Release.Indexer;

            try
            {
                _logger.Trace("Grabbing release '{0}' from Indexer {1} at priority {2}.", remote, remoteIndexer, remote.Release.IndexerPriority);
                await _downloadService.DownloadReport(remote, downloadClientId);

                return ProcessedDecisionResult.Grabbed;
            }
            catch (ReleaseUnavailableException)
            {
                _logger.Warn("Failed to download release '{0}' from Indexer {1}. Release not available", remote, remoteIndexer);
                return ProcessedDecisionResult.Rejected;
            }
            catch (Exception ex)
            {
                if (ex is DownloadClientUnavailableException || ex is DownloadClientAuthenticationException)
                {
                    _logger.Debug(ex, "Failed to send release '{0}' from Indexer {1} to download client, storing until later.", remote, remoteIndexer);
                    return ProcessedDecisionResult.Failed;
                }
                else
                {
                    _logger.Warn(ex, "Couldn't add release '{0}' from Indexer {1} to download queue.", remote, remoteIndexer);
                    return ProcessedDecisionResult.Skipped;
                }
            }
        }
    }
}
