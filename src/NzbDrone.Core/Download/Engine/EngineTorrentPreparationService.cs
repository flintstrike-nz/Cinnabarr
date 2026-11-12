using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Versions;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineTorrentPreparationService
    {
        // Applies the file whitelist to torrents Cinnabarr grabbed that the engine is holding, then starts them
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
        private readonly IMovieHistoryService _movieHistoryService;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IEngineLinkService _engineLinkService;
        private readonly IEngineFileMapper _engineFileMapper;
        private readonly ISeriesService _seriesService;
        private readonly IMovieService _movieService;
        private readonly IVersionSlotService _versionSlotService;
        private readonly ISeriesVersionSlotService _seriesVersionSlotService;
        private readonly IEpisodeService _episodeService;
        private readonly Logger _logger;

        public EngineTorrentPreparationService(IQBittorrentProxySelector proxySelector,
                                               IHistoryService historyService,
                                               IMovieHistoryService movieHistoryService,
                                               IFailedDownloadService failedDownloadService,
                                               IDownloadHistoryService downloadHistoryService,
                                               IEngineLinkService engineLinkService,
                                               IEngineFileMapper engineFileMapper,
                                               ISeriesService seriesService,
                                               IMovieService movieService,
                                               IVersionSlotService versionSlotService,
                                               ISeriesVersionSlotService seriesVersionSlotService,
                                               IEpisodeService episodeService,
                                               Logger logger)
        {
            _proxySelector = proxySelector;
            _historyService = historyService;
            _movieHistoryService = movieHistoryService;
            _failedDownloadService = failedDownloadService;
            _downloadHistoryService = downloadHistoryService;
            _engineLinkService = engineLinkService;
            _engineFileMapper = engineFileMapper;
            _seriesService = seriesService;
            _movieService = movieService;
            _versionSlotService = versionSlotService;
            _seriesVersionSlotService = seriesVersionSlotService;
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

            // Only torrents Cinnabarr grabbed, or that were added by hand on the Downloads page, are held for it; anything
            // else in the engine is left alone.
            if (grab == null)
            {
                // A movie's grab is in its own history.
                var movieGrab = _movieHistoryService.FindByDownloadId(torrent.Hash.ToUpperInvariant())
                    .Where(h => h.EventType == MovieHistoryEventType.Grabbed)
                    .MaxBy(h => h.Date);

                if (movieGrab != null)
                {
                    PrepareMovieTorrent(proxy, settings, torrent, hash, movieGrab);
                }
                else if (EngineTorrentTags.HasTag(torrent.Tags, EngineTorrentTags.Manual))
                {
                    PrepareManualTorrent(proxy, settings, torrent, hash);
                }

                return;
            }

            var downloadId = torrent.Hash.ToUpperInvariant();
            var files = proxy.GetTorrentFiles(hash, settings);
            var link = _engineLinkService.Get(downloadId);

            // Map the files to the library when Cinnabarr has the torrent's link (written at grab time). Without it the
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

            FinishPreparation(proxy, settings, torrent, hash, downloadId, link, files, map);
        }

        // Excludes what isn't wanted, moves a mapped torrent into the library, then starts it.
        private void FinishPreparation(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent, string hash, string downloadId, EngineLink link, List<QBittorrentTorrentFile> files, EngineFileMap map)
        {
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
                var infoHashV2 = torrent.InfoHashV2?.ToLowerInvariant();

                // Saved before renaming, so a retry after a failed rename reuses the same mapping.
                if (link.Files == null || link.InfoHashV2 != infoHashV2)
                {
                    link.SavePath = map.SavePath;
                    link.Files = map.Files;
                    link.InfoHashV2 = infoHashV2;
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

        // A grab for a movie. With the grab's link its video goes straight to the movie's folder under a hidden pending
        // name, like an episode's (roadmap §4.8); without it, it is prepared like one added by hand. A torrent with no
        // video at all is rejected (marked failed, which blocklists it and searches again).
        private void PrepareMovieTorrent(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent, string hash, MovieHistory grab)
        {
            var downloadId = torrent.Hash.ToUpperInvariant();
            var files = proxy.GetTorrentFiles(hash, settings);
            var link = _engineLinkService.Get(downloadId);

            var map = link == null
                ? null
                : link.Files != null
                    ? new EngineFileMap { SavePath = link.SavePath, Files = link.Files }
                    : _engineFileMapper.MapMovie(downloadId, _versionSlotService.GetView(_movieService.GetMovie(grab.MovieId), grab.VersionSlotId), grab.Quality, grab.SourceTitle, files);

            // Once mapped, files may already carry their pending names, so the whitelist can't be re-run.
            var noVideo = link != null ? map == null : !files.Any(f => EngineFileWhitelist.IsVideo(f.Name));

            if (noVideo)
            {
                if (_downloadHistoryService.GetLatestDownloadHistoryItem(downloadId) is not { EventType: DownloadHistoryEventType.DownloadFailed })
                {
                    _logger.Warn("'{0}' has no video file ({1}); rejecting it and searching again", torrent.Name, files.Select(f => f.Name).ConcatToString());

                    _failedDownloadService.MarkMovieAsFailed(grab.Id, NoVideoMessage, "Cinnabarr");
                }

                proxy.RemoveTorrent(hash, true, settings);

                return;
            }

            if (link == null)
            {
                PrepareManualTorrent(proxy, settings, torrent, hash);
                return;
            }

            FinishPreparation(proxy, settings, torrent, hash, downloadId, link, files, map);
        }

        // A torrent added by hand: only video and subtitle files are downloaded (roadmap §4.8), into the engine's
        // folder; Manual Import links it to the library. With nothing wanted it isn't started.
        private void PrepareManualTorrent(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent, string hash)
        {
            var files = proxy.GetTorrentFiles(hash, settings);
            var excluded = files.Select((file, index) => (file, index))
                .Where(f => !EngineFileWhitelist.IsWanted(f.file.Name))
                .Select(f => f.index)
                .ToList();

            if (excluded.Any())
            {
                proxy.SetFilePriority(hash, excluded, 0, settings);
            }

            var wanted = files.Count - excluded.Count;

            if (wanted > 0)
            {
                proxy.StartTorrent(hash, settings);
            }
            else
            {
                _logger.Warn("'{0}' has no video or subtitle files, so nothing in it is downloaded", torrent.Name);
            }

            proxy.AddTags(hash, new[] { EngineTorrentTags.Prepared }, settings);

            _logger.Info("Prepared '{0}' (added by hand): {1} of {2} file(s) will be downloaded", torrent.Name, wanted, files.Count);
        }

        private EngineFileMap MapToLibrary(string downloadId, EpisodeHistory grab, List<QBittorrentTorrentFile> files)
        {
            var series = _seriesService.GetSeries(grab.SeriesId);

            var episodeIds = _historyService.FindByDownloadId(downloadId)
                .Where(h => h.EventType == EpisodeHistoryEventType.Grabbed)
                .Select(h => h.EpisodeId)
                .ToHashSet();

            var episodes = _episodeService.GetEpisodeBySeries(series.Id).Where(e => episodeIds.Contains(e.Id)).ToList();

            // A grab for one of the series' extra versions is named and placed as that version (its {Version} label).
            if (grab.VersionSlotId != 0 && episodes.Any())
            {
                series = _seriesVersionSlotService.GetView(series, grab.VersionSlotId);
                episodes = _seriesVersionSlotService.GetEpisodeViews(episodes, grab.VersionSlotId);
            }

            return episodes.Any() ? _engineFileMapper.Map(downloadId, series, episodes, grab.Quality, grab.SourceTitle, files) : null;
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

                _failedDownloadService.MarkAsFailed(grab.Id, NoVideoMessage, "Cinnabarr");
            }

            // Nothing was downloaded: the torrent was held before any data.
            proxy.RemoveTorrent(torrent.Hash.ToLowerInvariant(), true, settings);
        }
    }
}
