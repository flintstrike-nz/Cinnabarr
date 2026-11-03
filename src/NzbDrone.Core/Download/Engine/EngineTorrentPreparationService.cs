using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.Tv;

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
        public const string NoVideoMessage = "The torrent has no video file for the grabbed episodes";

        private static readonly HashSet<string> HeldStates = new(StringComparer.OrdinalIgnoreCase)
        {
            "stoppedDL", "pausedDL", "stoppedUP", "pausedUP"
        };

        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly IHistoryService _historyService;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IEngineLinkService _engineLinkService;
        private readonly IEngineFileMapper _engineFileMapper;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly Logger _logger;

        public EngineTorrentPreparationService(IQBittorrentProxySelector proxySelector,
                                               IHistoryService historyService,
                                               IFailedDownloadService failedDownloadService,
                                               IDownloadHistoryService downloadHistoryService,
                                               IEngineLinkService engineLinkService,
                                               IEngineFileMapper engineFileMapper,
                                               ISeriesService seriesService,
                                               IEpisodeService episodeService,
                                               Logger logger)
        {
            _proxySelector = proxySelector;
            _historyService = historyService;
            _failedDownloadService = failedDownloadService;
            _downloadHistoryService = downloadHistoryService;
            _engineLinkService = engineLinkService;
            _engineFileMapper = engineFileMapper;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _logger = logger;
        }

        public void Prepare(IDownloadClient downloadClient)
        {
            if (downloadClient.Definition.Settings is not QBittorrentSettings settings)
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

        // Only torrents the engine was told to hold (the held tag, added with the torrent) are prepared, so
        // torrents grabbed before preparation existed, or stopped later, are never restarted or rejected.
        private static bool NeedsPreparation(QBittorrentTorrent torrent)
        {
            var tags = (torrent.Tags ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            return torrent.HasMetadata &&
                   HeldStates.Contains(torrent.State ?? string.Empty) &&
                   tags.Contains(EngineTorrentTags.Held) &&
                   !tags.Contains(EngineTorrentTags.Prepared);
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

            var downloadId = torrent.Hash.ToUpperInvariant();
            var files = proxy.GetTorrentFiles(hash, settings);
            var link = _engineLinkService.Get(downloadId);

            // Map the files to the library when Embarr has the torrent's link (written at grab time). Without it the
            // torrent downloads to the engine's folder and is imported the old way.
            var map = link == null ? null : link.Files != null ? new EngineFileMap { SavePath = link.SavePath, Files = link.Files } : MapToLibrary(downloadId, grab, files);

            // With a mapping, nothing matched means no video for the grabbed episodes. Without one, the whitelist
            // decides. (Once mapped, files may already carry their pending names, so the whitelist can't be re-run.)
            var noVideo = link != null ? map == null : !files.Any(f => EngineFileWhitelist.IsVideo(f.Name));

            if (noVideo)
            {
                Reject(proxy, settings, torrent, grab, files);
                return;
            }

            var kept = map?.Files.Select(f => f.Index).ToHashSet();

            var excluded = files.Select((file, index) => (file, index))
                .Where(f => kept == null ? !EngineFileWhitelist.IsWanted(f.file.Name) : !kept.Contains(f.index))
                .Select(f => f.index)
                .ToList();

            if (excluded.Any())
            {
                _logger.Debug("'{0}': not downloading {1} file(s)", torrent.Name, excluded.Count);
                proxy.SetFilePriority(hash, excluded, 0, settings);
            }

            if (map != null)
            {
                // Saved before renaming, so a retry after a failed rename reuses the same mapping.
                if (link.Files == null)
                {
                    link.SavePath = map.SavePath;
                    link.Files = map.Files;
                    _engineLinkService.Save(downloadId, link);
                }

                MoveToLibrary(proxy, settings, hash, files, map);
            }

            // Start first, then tag: if tagging fails the torrent is already running and isn't held again.
            proxy.StartTorrent(hash, settings);

            if ((QBittorrentState)settings.InitialState == QBittorrentState.ForceStart)
            {
                proxy.SetForceStart(hash, true, settings);
            }

            proxy.AddTags(hash, new[] { EngineTorrentTags.Prepared }, settings);

            _logger.Info("Prepared '{0}': {1} of {2} file(s) will be downloaded{3}", torrent.Name, files.Count - excluded.Count, files.Count, map == null ? string.Empty : $" into '{map.SavePath}'");
        }

        private EngineFileMap MapToLibrary(string downloadId, EpisodeHistory grab, List<QBittorrentTorrentFile> files)
        {
            var series = _seriesService.GetSeries(grab.SeriesId);

            var episodeIds = _historyService.FindByDownloadId(downloadId)
                .Where(h => h.EventType == EpisodeHistoryEventType.Grabbed)
                .Select(h => h.EpisodeId)
                .ToHashSet();

            var episodes = _episodeService.GetEpisodeBySeries(series.Id).Where(e => episodeIds.Contains(e.Id)).ToList();

            return episodes.Any() ? _engineFileMapper.Map(series, episodes, grab.Quality, grab.SourceTitle, files) : null;
        }

        // Downloads straight into the library: the engine saves into the lowest library folder that holds every
        // file, and each file gets its hidden pending name next to its final path (roadmap §4.8 steps 5-6).
        private static void MoveToLibrary(IQBittorrentProxy proxy, QBittorrentSettings settings, string hash, List<QBittorrentTorrentFile> files, EngineFileMap map)
        {
            proxy.SetLocation(hash, map.SavePath, settings);

            foreach (var file in map.Files)
            {
                var current = files[file.Index].Name.Replace('\\', '/');

                if (current != file.PendingPath)
                {
                    proxy.RenameFile(hash, current, file.PendingPath, settings);
                }
            }
        }

        private void Reject(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent, EpisodeHistory grab, List<QBittorrentTorrentFile> files)
        {
            // Record the failure once per grab; if removal failed last time, only retry the removal.
            if (_downloadHistoryService.GetLatestDownloadHistoryItem(torrent.Hash.ToUpperInvariant()) is not { EventType: DownloadHistoryEventType.DownloadFailed })
            {
                _logger.Warn("'{0}' has no video file for the grabbed episodes ({1}); rejecting it and searching again", torrent.Name, files.Select(f => f.Name).ConcatToString());

                _failedDownloadService.MarkAsFailed(grab.Id, NoVideoMessage, "Embarr");
            }

            // Nothing was downloaded: the torrent was held before any data.
            proxy.RemoveTorrent(torrent.Hash.ToLowerInvariant(), true, settings);
        }
    }
}
