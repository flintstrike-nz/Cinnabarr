using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineSeedingService
    {
        // Applies the seeding goals (roadmap §4.10) to every finished built-in engine torrent: the engine's own share
        // limits are kept in step, and once every file still in the torrent has met its goal the torrent is removed
        // from the engine, keeping its files.
        void Process(IDownloadClient downloadClient);

        // The goals the files of the download have to meet: each video's most specific goal (episode, season, series,
        // then global) and the tracker's minimum.
        List<EngineSeedingRule> GetRules(string downloadId, EngineLink link);

        // The goals each imported video of the download has to meet, by its file index.
        Dictionary<int, List<EngineSeedingRule>> GetRulesByFile(string downloadId, EngineLink link);
    }

    public class EngineSeedingService : IEngineSeedingService
    {
        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly IEngineLinkService _engineLinkService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly ISeedConfigProvider _seedConfigProvider;
        private readonly IConfigService _configService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly Logger _logger;

        public EngineSeedingService(IQBittorrentProxySelector proxySelector,
                                    IEngineLinkService engineLinkService,
                                    IDownloadHistoryService downloadHistoryService,
                                    ISeedConfigProvider seedConfigProvider,
                                    IConfigService configService,
                                    ITrackedDownloadService trackedDownloadService,
                                    ISeriesService seriesService,
                                    IEpisodeService episodeService,
                                    Logger logger)
        {
            _proxySelector = proxySelector;
            _engineLinkService = engineLinkService;
            _downloadHistoryService = downloadHistoryService;
            _seedConfigProvider = seedConfigProvider;
            _configService = configService;
            _trackedDownloadService = trackedDownloadService;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _logger = logger;
        }

        public void Process(IDownloadClient downloadClient)
        {
            if (downloadClient.Definition.Settings is not QBittorrentSettings settings)
            {
                return;
            }

            var proxy = _proxySelector.GetProxy(settings);

            foreach (var torrent in proxy.GetTorrents(settings))
            {
                var downloadId = torrent.Hash.ToUpperInvariant();

                try
                {
                    ProcessTorrent(proxy, settings, torrent, downloadId);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to apply seeding goals to '{0}'", torrent.Name);
                }
            }
        }

        public List<EngineSeedingRule> GetRules(string downloadId, EngineLink link)
        {
            var videos = link?.Files?.Where(f => !f.IsSubtitle && f.State == EngineLinkFileState.Imported).ToList() ?? new List<EngineLinkFile>();
            var series = link == null ? null : _seriesService.FindByTvdbId(link.TvdbId);
            var episodes = GetEpisodeLookup(series);
            var trackerRule = GetTrackerRule(downloadId, link);

            return videos.SelectMany(v => GetRules(series, episodes, v, trackerRule))
                .DefaultIfEmpty(GetGlobalRule())
                .Distinct()
                .ToList();
        }

        public Dictionary<int, List<EngineSeedingRule>> GetRulesByFile(string downloadId, EngineLink link)
        {
            var videos = link?.Files?.Where(f => !f.IsSubtitle && f.State == EngineLinkFileState.Imported).ToList() ?? new List<EngineLinkFile>();

            if (videos.Empty())
            {
                return new Dictionary<int, List<EngineSeedingRule>>();
            }

            var series = _seriesService.FindByTvdbId(link.TvdbId);
            var episodes = GetEpisodeLookup(series);
            var trackerRule = GetTrackerRule(downloadId, link);

            return videos.ToDictionary(v => v.Index, v => GetRules(series, episodes, v, trackerRule));
        }

        // A video's goals: for each of its episodes, the most specific goal set (episode, season, series, global), and
        // the tracker's minimum, a floor the other goals can't go below.
        private List<EngineSeedingRule> GetRules(Series series, Dictionary<(int SeasonNumber, int EpisodeNumber), Episode> episodes, EngineLinkFile video, EngineSeedingRule trackerRule)
        {
            var rules = new List<EngineSeedingRule>();

            foreach (var linkEpisode in video.Episodes.DefaultIfEmpty())
            {
                var episode = linkEpisode == null ? null : episodes.GetValueOrDefault((linkEpisode.SeasonNumber, linkEpisode.EpisodeNumber));
                var season = series?.Seasons?.FirstOrDefault(s => s.SeasonNumber == linkEpisode?.SeasonNumber);

                var rule = Describe(episode?.SeedingRule, "episode") ??
                           Describe(season?.SeedingRule, "season") ??
                           Describe(series?.SeedingRule, "series") ??
                           GetGlobalRule();

                if (!rules.Contains(rule))
                {
                    rules.Add(rule);
                }
            }

            if (trackerRule != null)
            {
                rules.Add(trackerRule);
            }

            return rules;
        }

        // The series' episodes by season and episode number, read once for all of a torrent's files.
        private Dictionary<(int SeasonNumber, int EpisodeNumber), Episode> GetEpisodeLookup(Series series)
        {
            return series == null
                ? new Dictionary<(int SeasonNumber, int EpisodeNumber), Episode>()
                : _episodeService.GetEpisodeBySeries(series.Id)
                    .GroupBy(e => (e.SeasonNumber, e.EpisodeNumber))
                    .ToDictionary(g => g.Key, g => g.First());
        }

        private EngineSeedingRule GetGlobalRule()
        {
            return new EngineSeedingRule
            {
                Mode = (EngineSeedingMode)_configService.EngineSeedingMode,
                Minutes = _configService.EngineSeedingMinutes,
                Ratio = _configService.EngineSeedingRatio,
                Source = "global"
            };
        }

        private static EngineSeedingRule Describe(EngineSeedingRule rule, string source)
        {
            return rule == null ? null : new EngineSeedingRule { Mode = rule.Mode, Minutes = rule.Minutes, Ratio = rule.Ratio, Source = source };
        }

        private void ProcessTorrent(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent, string downloadId)
        {
            var link = EngineLinkService.IsInfoHash(downloadId) ? _engineLinkService.Get(downloadId) : null;

            // Only a torrent whose files are all in the library: one still downloading or importing isn't done. One the
            // user ignored is left as it is.
            if (link?.Files == null || !link.Completed || _trackedDownloadService.Find(downloadId)?.State == TrackedDownloadState.Ignored)
            {
                return;
            }

            var videos = link.Files.Where(f => !f.IsSubtitle && f.State == EngineLinkFileState.Imported).ToList();

            // A torrent with no video left is removed by the per-file import.
            if (videos.Empty() || link.Files.Any(f => f.State is EngineLinkFileState.Pending or EngineLinkFileState.Renamed))
            {
                return;
            }

            var series = _seriesService.FindByTvdbId(link.TvdbId);
            var episodes = GetEpisodeLookup(series);
            var trackerRule = GetTrackerRule(downloadId, link);
            var rulesByVideo = videos.ToDictionary(v => v, v => GetRules(series, episodes, v, trackerRule));
            var rules = rulesByVideo.Values.SelectMany(r => r).Distinct().ToList();
            var now = DateTime.UtcNow;
            var ratio = Math.Max(0, (double)torrent.Ratio);

            // A video imported before seeding goals existed starts its seeding time now (saved, so it accrues).
            var unstamped = videos.Where(v => v.ImportedAt == null).ToList();

            if (unstamped.Any())
            {
                unstamped.ForEach(v => v.ImportedAt = now);
                _engineLinkService.Save(downloadId, link);
            }

            if (videos.All(v => EngineSeedingRule.AreMet(rulesByVideo[v], now - v.ImportedAt.Value, ratio)))
            {
                // Seeding never costs a file: the torrent goes, its files stay in the library.
                _logger.Info("'{0}' has met its seeding goals; removing it from the engine and keeping its files", torrent.Name);
                proxy.RemoveTorrent(torrent.Hash.ToLowerInvariant(), false, settings);

                return;
            }

            UpdateShareLimits(proxy, settings, torrent, rules, downloadId, link);
        }

        // The engine's own share limits, so the goals still apply while Embarr is down. The engine stops a torrent as
        // soon as either of its limits is reached, so a ratio limit is only written when reaching that ratio meets
        // every goal (a single "ratio" or "time or ratio" goal). Time is never written: the engine counts it from when
        // the torrent finished, which can be well before its files were imported. Otherwise the torrent is left
        // unlimited and Embarr removes it.
        private void UpdateShareLimits(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent, List<EngineSeedingRule> rules, string downloadId, EngineLink link)
        {
            var stopped = torrent.State is "pausedUP" or "stoppedUP";

            // A start Embarr decided on earlier that hasn't happened yet (the request failed) is tried again; once the
            // torrent runs, a later stop is the user's and is left alone.
            if (link.ResumeSeeding)
            {
                if (stopped)
                {
                    proxy.StartTorrent(torrent.Hash.ToLowerInvariant(), settings);
                }

                link.ResumeSeeding = false;
                _engineLinkService.Save(downloadId, link);
            }

            // Goals that only differ in where they came from count once.
            var active = rules.Where(r => r.Mode != EngineSeedingMode.None)
                .DistinctBy(r => (r.Mode, r.Minutes, r.Ratio))
                .ToList();
            double ratioLimit = -1;
            const long minutesLimit = -1;

            if (active.Count == 1 && active[0].Mode is EngineSeedingMode.Ratio or EngineSeedingMode.Either)
            {
                ratioLimit = active[0].Ratio;
            }

            if (Math.Abs(torrent.RatioLimit - ratioLimit) < EngineSeedingRule.RatioTolerance && torrent.SeedingTimeLimit == minutesLimit && torrent.InactiveSeedingTimeLimit == minutesLimit)
            {
                return;
            }

            // A torrent an earlier limit stopped (for example a tracker limit set when it was added) seeds again under
            // the new one. A torrent stopped below its old limits was stopped by hand and stays stopped.
            var stoppedByLimit = stopped &&
                                 ((torrent.RatioLimit >= 0 && torrent.Ratio >= torrent.RatioLimit - EngineSeedingRule.RatioTolerance) ||
                                  torrent.SeedingTimeLimit >= 0 ||
                                  torrent.InactiveSeedingTimeLimit >= 0);

            // Both of the engine's time limits (total and inactive) are cleared, whichever the client settings name.
            proxy.SetShareLimitsWithoutTimeLimits(torrent.Hash.ToLowerInvariant(), ratioLimit, settings);

            if (stoppedByLimit && (ratioLimit < 0 || torrent.Ratio < ratioLimit - EngineSeedingRule.RatioTolerance))
            {
                // Saved first, so a start that fails is tried again on the next poll.
                _logger.Debug("'{0}' was stopped by its earlier share limits; seeding it again", torrent.Name);
                link.ResumeSeeding = true;
                _engineLinkService.Save(downloadId, link);

                proxy.StartTorrent(torrent.Hash.ToLowerInvariant(), settings);

                link.ResumeSeeding = false;
                _engineLinkService.Save(downloadId, link);
            }
        }

        private EngineSeedingRule GetTrackerRule(string downloadId, EngineLink link)
        {
            var grab = _downloadHistoryService.GetLatestGrab(downloadId);
            var indexerId = grab?.Release?.IndexerId ?? 0;

            // The indexer's current settings while it is known; otherwise (a link restored without it, or an indexer
            // since deleted) the minimum saved at grab time.
            TorrentSeedConfiguration seedConfig = null;

            if (indexerId > 0)
            {
                var fullSeason = Parser.Parser.ParseTitle(grab.SourceTitle)?.FullSeason ?? false;
                seedConfig = _seedConfigProvider.GetSeedConfiguration(indexerId, fullSeason);
            }

            if (seedConfig == null)
            {
                seedConfig = link?.TrackerSeedRatio == null && link?.TrackerSeedMinutes == null
                    ? null
                    : new TorrentSeedConfiguration
                    {
                        Ratio = link.TrackerSeedRatio,
                        SeedTime = link.TrackerSeedMinutes.HasValue ? TimeSpan.FromMinutes(link.TrackerSeedMinutes.Value) : null
                    };
            }

            if (seedConfig == null || (seedConfig.Ratio is not > 0 && seedConfig.SeedTime is not { TotalMinutes: > 0 }))
            {
                return null;
            }

            var usesRatio = seedConfig.Ratio is > 0;
            var usesTime = seedConfig.SeedTime is { TotalMinutes: > 0 };

            // Like the engine's own limits, a tracker's minimum is met by whichever of its ratio and time comes first.
            return new EngineSeedingRule
            {
                Mode = usesRatio && usesTime ? EngineSeedingMode.Either : usesRatio ? EngineSeedingMode.Ratio : EngineSeedingMode.Time,
                Minutes = usesTime ? (int)seedConfig.SeedTime.Value.TotalMinutes : 0,
                Ratio = usesRatio ? seedConfig.Ratio.Value : 0,
                Source = "tracker"
            };
        }
    }
}
