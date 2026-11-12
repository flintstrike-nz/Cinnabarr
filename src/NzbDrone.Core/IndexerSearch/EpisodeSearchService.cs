using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Versions;

namespace NzbDrone.Core.IndexerSearch
{
    public class EpisodeSearchService : IExecute<EpisodeSearchCommand>,
                                        IExecute<MissingEpisodeSearchCommand>,
                                        IExecute<CutoffUnmetEpisodeSearchCommand>
    {
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly IEpisodeService _episodeService;
        private readonly IEpisodeCutoffService _episodeCutoffService;
        private readonly IQueueService _queueService;
        private readonly ISeriesVersionSlotService _versionSlotService;
        private readonly Logger _logger;

        public EpisodeSearchService(ISearchForReleases releaseSearchService,
                                    IProcessDownloadDecisions processDownloadDecisions,
                                    IEpisodeService episodeService,
                                    IEpisodeCutoffService episodeCutoffService,
                                    IQueueService queueService,
                                    ISeriesVersionSlotService versionSlotService,
                                    Logger logger)
        {
            _releaseSearchService = releaseSearchService;
            _processDownloadDecisions = processDownloadDecisions;
            _episodeService = episodeService;
            _episodeCutoffService = episodeCutoffService;
            _queueService = queueService;
            _versionSlotService = versionSlotService;
            _logger = logger;
        }

        private async Task SearchForBulkEpisodes(List<Episode> episodes, bool monitoredOnly, bool userInvokedSearch, List<SeriesVersionWanted> versions = null)
        {
            var groups = new List<EpisodeSearchGroup>();

            AddGroups(groups, episodes, 0);

            // The episodes the extra versions of series want are searched for the version.
            foreach (var version in versions ?? new List<SeriesVersionWanted>())
            {
                AddGroups(groups, version.Episodes, version.Series.VersionSlotId);
            }

            var episodeCount = groups.Sum(g => g.Episodes.Count);

            _logger.ProgressInfo("Performing search for {0} episodes", episodeCount);
            var downloadedCount = 0;

            foreach (var group in groups.OrderBy(g => g.Episodes.Min(e => e.LastSearchTime ?? DateTime.MinValue)))
            {
                List<DownloadDecision> decisions;

                var seriesId = group.SeriesId;
                var seasonNumber = group.SeasonNumber;
                var groupEpisodes = group.Episodes;
                var slotId = group.VersionSlotId;

                if (groupEpisodes.Count > 1)
                {
                    try
                    {
                        decisions = slotId == 0
                            ? await _releaseSearchService.SeasonSearch(seriesId, seasonNumber, groupEpisodes, monitoredOnly, userInvokedSearch, false)
                            : await _releaseSearchService.SeasonSearch(seriesId, slotId, seasonNumber, groupEpisodes, monitoredOnly, userInvokedSearch, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Unable to search for episodes in season {0} of [{1}]", seasonNumber, seriesId);
                        continue;
                    }
                }
                else
                {
                    var episode = groupEpisodes.First();

                    try
                    {
                        decisions = slotId == 0
                            ? await _releaseSearchService.EpisodeSearch(episode, userInvokedSearch, false)
                            : await _releaseSearchService.EpisodeSearch(episode.Id, slotId, userInvokedSearch, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Unable to search for episode: [{0}]", episode);
                        continue;
                    }
                }

                var processed = await _processDownloadDecisions.ProcessDecisions(decisions);

                downloadedCount += processed.Grabbed.Count;
            }

            _logger.ProgressInfo("Completed search for {0} episodes. {1} reports downloaded.", episodeCount, downloadedCount);
        }

        private static void AddGroups(List<EpisodeSearchGroup> groups, List<Episode> episodes, int slotId)
        {
            foreach (var series in episodes.GroupBy(e => e.SeriesId))
            {
                foreach (var season in series.GroupBy(e => e.SeasonNumber))
                {
                    groups.Add(new EpisodeSearchGroup
                    {
                        SeriesId = series.Key,
                        SeasonNumber = season.Key,
                        VersionSlotId = slotId,
                        Episodes = season.ToList()
                    });
                }
            }
        }

        private bool IsMonitored(bool episodeMonitored, bool seriesMonitored)
        {
            return episodeMonitored && seriesMonitored;
        }

        public void Execute(EpisodeSearchCommand message)
        {
            foreach (var episodeId in message.EpisodeIds)
            {
                var decisions = _releaseSearchService.EpisodeSearch(episodeId, message.VersionSlotId, message.Trigger == CommandTrigger.Manual, false).GetAwaiter().GetResult();
                var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

                _logger.ProgressInfo("Episode search completed. {0} reports downloaded.", processed.Grabbed.Count);
            }
        }

        public void Execute(MissingEpisodeSearchCommand message)
        {
            var monitored = message.Monitored;
            List<Episode> episodes;

            if (message.SeriesId.HasValue)
            {
                episodes = _episodeService.GetEpisodeBySeries(message.SeriesId.Value)
                                          .Where(e => e.Monitored == monitored &&
                                                 !e.HasFile &&
                                                 e.AirDateUtc.HasValue &&
                                                 e.AirDateUtc.Value.Before(DateTime.UtcNow))
                                          .ToList();
            }
            else
            {
                var pagingSpec = new PagingSpec<Episode>
                                 {
                                     Page = 1,
                                     PageSize = 1000000,
                                     SortDirection = SortDirection.Ascending,
                                     SortKey = "Id"
                                 };

                if (monitored)
                {
                    pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Series.Monitored == true);
                }
                else
                {
                    pagingSpec.FilterExpressions.Add(v => v.Monitored == false || v.Series.Monitored == false);
                }

                if (message.SeriesIds?.Any() == true)
                {
                    pagingSpec.FilterExpressions.Add(e => message.SeriesIds.Contains(e.SeriesId));
                }

                if (message.QualityProfileIds?.Any() == true)
                {
                    pagingSpec.FilterExpressions.Add(e => message.QualityProfileIds.Contains(e.Series.QualityProfileId));
                }

                if (message.SeriesType?.Any() == true)
                {
                    pagingSpec.FilterExpressions.Add(e => message.SeriesType.Contains(e.Series.SeriesType));
                }

                episodes = _episodeService.EpisodesWithoutFiles(pagingSpec, true, message.SeriesTags).Records.ToList();
            }

            var queue = GetQueuedEpisodes();
            var missing = episodes.Where(e => !queue.Contains((e.Id, 0))).ToList();

            // The extra versions of monitored series have their own missing episodes (they follow monitored searches only).
            var versions = monitored ? GetQueueFree(_versionSlotService.GetMissing(GetSeriesFilter(message.SeriesId, message.SeriesIds)), queue) : null;

            SearchForBulkEpisodes(missing, monitored, message.Trigger == CommandTrigger.Manual, versions).GetAwaiter().GetResult();
        }

        public void Execute(CutoffUnmetEpisodeSearchCommand message)
        {
            var monitored = message.Monitored;

            var pagingSpec = new PagingSpec<Episode>
                             {
                                 Page = 1,
                                 PageSize = 100000,
                                 SortDirection = SortDirection.Ascending,
                                 SortKey = "Id"
                             };

            if (message.SeriesId.HasValue)
            {
                var seriesId = message.SeriesId.Value;
                pagingSpec.FilterExpressions.Add(v => v.SeriesId == seriesId);
            }

            if (monitored)
            {
                pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Series.Monitored == true);
            }
            else
            {
                pagingSpec.FilterExpressions.Add(v => v.Monitored == false || v.Series.Monitored == false);
            }

            if (message.SeriesIds?.Any() == true)
            {
                pagingSpec.FilterExpressions.Add(e => message.SeriesIds.Contains(e.SeriesId));
            }

            if (message.QualityProfileIds?.Any() == true)
            {
                pagingSpec.FilterExpressions.Add(e => message.QualityProfileIds.Contains(e.Series.QualityProfileId));
            }

            if (message.SeriesType?.Any() == true)
            {
                pagingSpec.FilterExpressions.Add(e => message.SeriesType.Contains(e.Series.SeriesType));
            }

            var episodes = _episodeCutoffService.EpisodesWhereCutoffUnmet(pagingSpec, message.SeriesTags, message.Quality).Records.ToList();
            var queue = GetQueuedEpisodes();
            var cutoffUnmet = episodes.Where(e => !queue.Contains((e.Id, 0))).ToList();

            var versions = monitored ? GetQueueFree(_versionSlotService.GetCutoffUnmet(GetSeriesFilter(message.SeriesId, message.SeriesIds)), queue) : null;

            SearchForBulkEpisodes(cutoffUnmet, monitored, message.Trigger == CommandTrigger.Manual, versions).GetAwaiter().GetResult();
        }

        // The episodes (and the version of each; 0 is the series' default version) that are already downloading.
        private HashSet<(int EpisodeId, int SlotId)> GetQueuedEpisodes()
        {
            // Rows listed only for visibility (already imported/failed/ignored) don't block searches.
            return _queueService.GetQueue()
                .Where(q => q.IsTrackable && q.Episodes.Any())
                .SelectMany(q => q.Episodes.Select(e => (e.Id, q.Series?.VersionSlotId ?? 0)))
                .ToHashSet();
        }

        private static List<SeriesVersionWanted> GetQueueFree(List<SeriesVersionWanted> versions, HashSet<(int EpisodeId, int SlotId)> queue)
        {
            foreach (var version in versions)
            {
                version.Episodes = version.Episodes.Where(e => !queue.Contains((e.Id, version.Series.VersionSlotId))).ToList();
            }

            return versions.Where(v => v.Episodes.Any()).ToList();
        }

        private static List<int> GetSeriesFilter(int? seriesId, List<int> seriesIds)
        {
            if (seriesId.HasValue)
            {
                return new List<int> { seriesId.Value };
            }

            return seriesIds?.Any() == true ? seriesIds : null;
        }
    }
}
