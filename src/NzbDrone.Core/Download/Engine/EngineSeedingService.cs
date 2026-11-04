using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineSeedingService
    {
        // Applies the seeding goals (roadmap §4.10) to every finished built-in engine torrent: the engine's own share
        // limits are kept in step, and once every file still in the torrent has met its goal the torrent is removed
        // from the engine, keeping its files.
        void Process(IDownloadClient downloadClient);

        // The goals a file of the download has to meet, most specific last.
        List<EngineSeedingRule> GetRules(string downloadId);
    }

    public class EngineSeedingService : IEngineSeedingService
    {
        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly IEngineLinkService _engineLinkService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly ISeedConfigProvider _seedConfigProvider;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public EngineSeedingService(IQBittorrentProxySelector proxySelector,
                                    IEngineLinkService engineLinkService,
                                    IDownloadHistoryService downloadHistoryService,
                                    ISeedConfigProvider seedConfigProvider,
                                    IConfigService configService,
                                    Logger logger)
        {
            _proxySelector = proxySelector;
            _engineLinkService = engineLinkService;
            _downloadHistoryService = downloadHistoryService;
            _seedConfigProvider = seedConfigProvider;
            _configService = configService;
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

        public List<EngineSeedingRule> GetRules(string downloadId)
        {
            var rules = new List<EngineSeedingRule>
            {
                new EngineSeedingRule
                {
                    Mode = (EngineSeedingMode)_configService.EngineSeedingMode,
                    Minutes = _configService.EngineSeedingMinutes,
                    Ratio = _configService.EngineSeedingRatio,
                    Source = "global"
                }
            };

            // A tracker's minimum (its indexer's seed ratio and time) is a floor the other goals can't go below.
            var trackerRule = GetTrackerRule(downloadId);

            if (trackerRule != null)
            {
                rules.Add(trackerRule);
            }

            return rules;
        }

        private void ProcessTorrent(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent, string downloadId)
        {
            var link = EngineLinkService.IsInfoHash(downloadId) ? _engineLinkService.Get(downloadId) : null;

            // Only a torrent whose files are all in the library: one still downloading or importing isn't done.
            if (link?.Files == null || !link.Completed)
            {
                return;
            }

            var videos = link.Files.Where(f => !f.IsSubtitle && f.State == EngineLinkFileState.Imported).ToList();

            // A torrent with no video left is removed by the per-file import.
            if (videos.Empty() || link.Files.Any(f => f.State is EngineLinkFileState.Pending or EngineLinkFileState.Renamed))
            {
                return;
            }

            var rules = GetRules(downloadId);
            var now = DateTime.UtcNow;
            var ratio = Math.Max(0, (double)torrent.Ratio);

            if (videos.All(v => EngineSeedingRule.AreMet(rules, now - (v.ImportedAt ?? now), ratio)))
            {
                // Seeding never costs a file: the torrent goes, its files stay in the library.
                _logger.Info("'{0}' has met its seeding goals; removing it from the engine and keeping its files", torrent.Name);
                proxy.RemoveTorrent(torrent.Hash.ToLowerInvariant(), false, settings);

                return;
            }

            UpdateShareLimits(proxy, settings, torrent, rules);
        }

        // The engine's own share limits, so the goals still apply while Embarr is down. The engine stops a torrent as
        // soon as either limit is reached, so limits are only written when the goals amount to one "time or ratio"
        // goal; otherwise the torrent is left unlimited and Embarr removes it. The engine counts seeding time from when
        // the whole torrent finished, never before any file did, so its limit is never reached early.
        private void UpdateShareLimits(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent, List<EngineSeedingRule> rules)
        {
            var active = rules.Where(r => r.Mode != EngineSeedingMode.None).ToList();
            double ratioLimit = -1;
            long minutesLimit = -1;

            if (active.Count == 1 && active[0].Mode != EngineSeedingMode.Both)
            {
                ratioLimit = active[0].UsesRatio ? active[0].Ratio : -1;
                minutesLimit = active[0].UsesTime ? active[0].Minutes : -1;
            }

            if (Math.Abs(torrent.RatioLimit - ratioLimit) < 0.001 && torrent.SeedingTimeLimit == minutesLimit)
            {
                return;
            }

            proxy.SetTorrentSeedingConfiguration(torrent.Hash.ToLowerInvariant(),
                                                 new TorrentSeedConfiguration
                                                 {
                                                     Ratio = ratioLimit,
                                                     SeedTime = TimeSpan.FromMinutes(minutesLimit)
                                                 },
                                                 settings);
        }

        private EngineSeedingRule GetTrackerRule(string downloadId)
        {
            var grab = _downloadHistoryService.GetLatestGrab(downloadId);
            var indexerId = grab?.Release?.IndexerId ?? 0;

            if (indexerId == 0)
            {
                return null;
            }

            var fullSeason = Parser.Parser.ParseTitle(grab.SourceTitle)?.FullSeason ?? false;
            var seedConfig = _seedConfigProvider.GetSeedConfiguration(indexerId, fullSeason);

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
