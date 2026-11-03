using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.History;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineTorrentPreparationService
    {
        // Applies the file whitelist to torrents Embarr grabbed that the engine is holding, then starts them
        // (roadmap §4.8). Torrents with no video file are rejected, blocklisted and searched for again.
        void Prepare(IDownloadClient downloadClient);
    }

    public class EngineTorrentPreparationService : IEngineTorrentPreparationService
    {
        public const string PreparedTag = "embarr-prepared";
        public const string NoVideoMessage = "The torrent has no video file";

        private static readonly HashSet<string> HeldStates = new(StringComparer.OrdinalIgnoreCase)
        {
            "stoppedDL", "pausedDL", "stoppedUP", "pausedUP"
        };

        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly IHistoryService _historyService;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly Logger _logger;

        public EngineTorrentPreparationService(IQBittorrentProxySelector proxySelector,
                                               IHistoryService historyService,
                                               IFailedDownloadService failedDownloadService,
                                               Logger logger)
        {
            _proxySelector = proxySelector;
            _historyService = historyService;
            _failedDownloadService = failedDownloadService;
            _logger = logger;
        }

        public void Prepare(IDownloadClient downloadClient)
        {
            if (downloadClient.Definition.Settings is not QBittorrentSettings { HoldNewTorrents: true } settings)
            {
                return;
            }

            var proxy = _proxySelector.GetProxy(settings);

            foreach (var torrent in proxy.GetTorrents(settings).Where(NeedsPreparation))
            {
                try
                {
                    PrepareTorrent(proxy, settings, torrent);
                }
                catch (Exception ex)
                {
                    // Left held; the next poll tries again.
                    _logger.Warn(ex, "Unable to prepare '{0}' in the engine", torrent.Name);
                }
            }
        }

        private static bool NeedsPreparation(QBittorrentTorrent torrent)
        {
            return torrent.HasMetadata &&
                   HeldStates.Contains(torrent.State ?? string.Empty) &&
                   !(torrent.Tags ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(PreparedTag);
        }

        private void PrepareTorrent(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent)
        {
            var hash = torrent.Hash.ToLowerInvariant();

            var grab = _historyService.FindByDownloadId(torrent.Hash.ToUpperInvariant())
                .Where(h => h.EventType == EpisodeHistoryEventType.Grabbed)
                .MaxBy(h => h.Date);

            // Only torrents Embarr grabbed are held for it; anything else in the engine is left alone.
            if (grab == null)
            {
                return;
            }

            var files = proxy.GetTorrentFiles(hash, settings);

            if (!files.Any(f => EngineFileWhitelist.IsVideo(f.Name)))
            {
                _logger.Warn("'{0}' has no video file ({1}); rejecting it and searching again", torrent.Name, files.Select(f => f.Name).ConcatToString());

                _failedDownloadService.MarkAsFailed(grab.Id, NoVideoMessage, "Embarr");

                // Nothing was downloaded: the torrent was held before any data.
                proxy.RemoveTorrent(hash, true, settings);

                return;
            }

            var excluded = files.Select((file, index) => (file, index))
                .Where(f => !EngineFileWhitelist.IsWanted(f.file.Name))
                .Select(f => f.index)
                .ToList();

            if (excluded.Any())
            {
                _logger.Debug("'{0}': not downloading {1} file(s) that aren't video or subtitles", torrent.Name, excluded.Count);
                proxy.SetFilePriority(hash, excluded, 0, settings);
            }

            // Start first, then tag: if tagging fails the torrent is already running and isn't held again.
            proxy.StartTorrent(hash, settings);
            proxy.AddTags(hash, new[] { PreparedTag }, settings);

            _logger.Info("Prepared '{0}': {1} of {2} file(s) will be downloaded", torrent.Name, files.Count - excluded.Count, files.Count);
        }
    }
}
