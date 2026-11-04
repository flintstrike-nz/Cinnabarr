using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Download.Clients.QBittorrent;

namespace NzbDrone.Core.Download.Engine
{
    public class EngineSeedingGoal
    {
        public EngineSeedingMode Mode { get; set; }
        public int Minutes { get; set; }
        public double Ratio { get; set; }

        // Where the goal came from: episode, season, series, global or tracker.
        public string Source { get; set; }
        public bool IsMet { get; set; }
    }

    public class EngineDownloadFile
    {
        public int Index { get; set; }

        // The file's library path once published (its final name in the save folder).
        public string Path { get; set; }
        public bool IsSubtitle { get; set; }
        public EngineLinkFileState State { get; set; }

        // Download progress (0-1); null when the engine doesn't have the torrent.
        public double? Progress { get; set; }
        public DateTime? ImportedAt { get; set; }
        public List<EngineSeedingGoal> Goals { get; set; } = new();
    }

    public class EngineDownloadFiles
    {
        public bool InEngine { get; set; }

        // The torrent's state in the engine, for its controls (all false when the engine doesn't have it).
        public bool IsPaused { get; set; }
        public bool IsForceStarted { get; set; }
        public bool IsAwaitingPreparation { get; set; }

        public double? Ratio { get; set; }
        public List<EngineDownloadFile> Files { get; set; } = new();
    }

    public interface IEngineDownloadFilesService
    {
        // Each file of a mapped engine download (roadmap §6.2): where it goes in the library, its state and progress,
        // and the seeding goals it has to meet, each with where it came from. Null for a download without a file map.
        EngineDownloadFiles Get(string downloadId);
    }

    public class EngineDownloadFilesService : IEngineDownloadFilesService
    {
        private readonly IEngineLinkService _engineLinkService;
        private readonly IEngineSeedingService _engineSeedingService;
        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly Logger _logger;

        public EngineDownloadFilesService(IEngineLinkService engineLinkService,
                                          IEngineSeedingService engineSeedingService,
                                          IEngineClientRegistry engineClientRegistry,
                                          IProvideDownloadClient downloadClientProvider,
                                          IQBittorrentProxySelector proxySelector,
                                          Logger logger)
        {
            _engineLinkService = engineLinkService;
            _engineSeedingService = engineSeedingService;
            _engineClientRegistry = engineClientRegistry;
            _downloadClientProvider = downloadClientProvider;
            _proxySelector = proxySelector;
            _logger = logger;
        }

        public EngineDownloadFiles Get(string downloadId)
        {
            if (!EngineLinkService.IsInfoHash(downloadId))
            {
                return null;
            }

            downloadId = downloadId.ToUpperInvariant();

            var link = _engineLinkService.Get(downloadId);

            if (link?.Files == null)
            {
                return null;
            }

            var (torrent, engineFiles) = GetFromEngine(downloadId);
            var ratio = torrent == null ? (double?)null : Math.Max(0, (double)torrent.Ratio);
            var rulesByFile = _engineSeedingService.GetRulesByFile(downloadId, link);
            var now = DateTime.UtcNow;

            return new EngineDownloadFiles
            {
                InEngine = torrent != null,
                IsPaused = torrent?.State is "pausedDL" or "pausedUP" or "stoppedDL" or "stoppedUP",
                IsForceStarted = torrent?.State?.StartsWith("forced", StringComparison.Ordinal) == true,
                IsAwaitingPreparation = torrent != null && EngineTorrentTags.IsAwaitingPreparation(torrent.Tags),
                Ratio = ratio,
                Files = link.Files
                    .OrderBy(f => f.IsSubtitle ? f.VideoIndex ?? f.Index : f.Index)
                    .ThenBy(f => f.IsSubtitle)
                    .Select(f => new EngineDownloadFile
                    {
                        Index = f.Index,
                        Path = Path.Combine(link.SavePath, f.FinalPath),
                        IsSubtitle = f.IsSubtitle,
                        State = f.State,
                        Progress = engineFiles != null && f.Index < engineFiles.Count ? engineFiles[f.Index].Progress : null,
                        ImportedAt = f.ImportedAt,
                        Goals = rulesByFile.GetValueOrDefault(f.Index, new List<EngineSeedingRule>())
                            .Select(r => new EngineSeedingGoal
                            {
                                Mode = r.Mode,
                                Minutes = r.Minutes,
                                Ratio = r.Ratio,
                                Source = r.Source,
                                IsMet = f.ImportedAt.HasValue && ratio.HasValue && r.IsMet(now - f.ImportedAt.Value, ratio.Value)
                            })
                            .ToList()
                    })
                    .ToList()
            };
        }

        // The torrent and its files (by index) as the engine has them, or nulls when it doesn't, or can't be asked.
        private (QBittorrentTorrent Torrent, List<QBittorrentTorrentFile> Files) GetFromEngine(string downloadId)
        {
            var client = _downloadClientProvider.GetDownloadClients().FirstOrDefault(c => _engineClientRegistry.IsEngineClient(c.Definition.Id));

            if (client?.Definition.Settings is not QBittorrentSettings settings)
            {
                return (null, null);
            }

            try
            {
                var proxy = _proxySelector.GetProxy(settings);
                var torrent = proxy.GetTorrents(settings).FirstOrDefault(t => string.Equals(t.Hash, downloadId, StringComparison.OrdinalIgnoreCase));

                return torrent == null ? (null, null) : (torrent, proxy.GetTorrentFiles(torrent.Hash.ToLowerInvariant(), settings));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to read '{0}' from the engine", downloadId);

                return (null, null);
            }
        }
    }
}
