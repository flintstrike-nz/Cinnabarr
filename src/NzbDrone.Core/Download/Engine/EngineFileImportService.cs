using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineFileImportService
    {
        // Imports each file of a built-in engine torrent as soon as it completes (roadmap §4.8 "Import is per file").
        void Process(IDownloadClient downloadClient);
    }

    // Per file: check it under its pending name; if it passes, the engine renames it to its final name (an instant
    // rename in the same folder, so it keeps seeding); once the rename is on disk the file is recorded in place.
    // Every step is recorded in the link sidecar, so a repeated poll or a restart never imports a file twice.
    public class EngineFileImportService : IEngineFileImportService
    {
        public const string RejectedMessage = "A downloaded file failed its import checks";

        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly IEngineLinkService _engineLinkService;
        private readonly IHistoryService _historyService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IAggregationService _aggregationService;
        private readonly ILocalEpisodeCustomFormatCalculationService _formatCalculator;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedEpisodes _importApprovedEpisodes;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public EngineFileImportService(IQBittorrentProxySelector proxySelector,
                                       IEngineLinkService engineLinkService,
                                       IHistoryService historyService,
                                       IDownloadHistoryService downloadHistoryService,
                                       ISeriesService seriesService,
                                       IEpisodeService episodeService,
                                       IAggregationService aggregationService,
                                       ILocalEpisodeCustomFormatCalculationService formatCalculator,
                                       IMakeImportDecision importDecisionMaker,
                                       IImportApprovedEpisodes importApprovedEpisodes,
                                       IFailedDownloadService failedDownloadService,
                                       ITrackedDownloadService trackedDownloadService,
                                       IEventAggregator eventAggregator,
                                       IDiskProvider diskProvider,
                                       Logger logger)
        {
            _proxySelector = proxySelector;
            _engineLinkService = engineLinkService;
            _historyService = historyService;
            _downloadHistoryService = downloadHistoryService;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _aggregationService = aggregationService;
            _formatCalculator = formatCalculator;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedEpisodes = importApprovedEpisodes;
            _failedDownloadService = failedDownloadService;
            _trackedDownloadService = trackedDownloadService;
            _eventAggregator = eventAggregator;
            _diskProvider = diskProvider;
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
                var link = EngineLinkService.IsInfoHash(downloadId) ? _engineLinkService.Get(downloadId) : null;

                if (link?.Files == null || link.Completed)
                {
                    continue;
                }

                try
                {
                    ProcessTorrent(proxy, settings, torrent, downloadId, link);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to import files from '{0}'", torrent.Name);
                }
            }
        }

        private void ProcessTorrent(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent, string downloadId, EngineLink link)
        {
            var hash = torrent.Hash.ToLowerInvariant();
            var engineFiles = proxy.GetTorrentFiles(hash, settings);
            var grabs = _historyService.FindByDownloadId(downloadId).Where(h => h.EventType == EpisodeHistoryEventType.Grabbed).ToList();

            if (grabs.Empty())
            {
                return;
            }

            var series = _seriesService.GetSeries(grabs.First().SeriesId);
            var seriesEpisodes = _episodeService.GetEpisodeBySeries(series.Id);
            var downloadItem = _trackedDownloadService.Find(downloadId)?.DownloadItem;
            var changed = false;

            foreach (var file in link.Files.Where(f => !f.IsSubtitle))
            {
                changed |= ProcessVideo(proxy, settings, hash, link, file, engineFiles, series, seriesEpisodes, downloadItem, grabs);
            }

            foreach (var file in link.Files.Where(f => f.IsSubtitle))
            {
                changed |= ProcessSubtitle(proxy, settings, hash, link, file, engineFiles);
            }

            var videos = link.Files.Where(f => !f.IsSubtitle).ToList();

            if (videos.All(f => f.State is EngineLinkFileState.Imported or EngineLinkFileState.Rejected))
            {
                Complete(downloadId, link, series, seriesEpisodes, grabs);
                changed = true;

                // Every video was rejected (and deleted): nothing is left to download or seed. The data flag stays off,
                // so removing the torrent never touches the library.
                if (videos.All(f => f.State == EngineLinkFileState.Rejected))
                {
                    proxy.RemoveTorrent(hash, false, settings);
                }
            }

            if (changed)
            {
                _engineLinkService.Save(downloadId, link);
            }
        }

        private bool ProcessVideo(IQBittorrentProxy proxy,
                                  QBittorrentSettings settings,
                                  string hash,
                                  EngineLink link,
                                  EngineLinkFile file,
                                  List<QBittorrentTorrentFile> engineFiles,
                                  Series series,
                                  List<Episode> seriesEpisodes,
                                  DownloadClientItem downloadItem,
                                  List<EpisodeHistory> grabs)
        {
            var pendingPath = Path.Combine(link.SavePath, file.PendingPath);
            var finalPath = Path.Combine(link.SavePath, file.FinalPath);
            var episodes = GetEpisodes(file, seriesEpisodes);

            switch (file.State)
            {
                case EngineLinkFileState.Pending:
                {
                    if (!IsComplete(engineFiles, file) || !_diskProvider.FileExists(pendingPath))
                    {
                        return false;
                    }

                    var decision = GetDecision(pendingPath, file.FinalPath, series, episodes, downloadItem, link);

                    if (!decision.Approved)
                    {
                        _logger.Warn("'{0}' failed its import checks: {1}", file.FinalPath, string.Join(", ", decision.Rejections.Select(r => r.Message)));
                        RejectFile(proxy, settings, hash, link, file, pendingPath, grabs);

                        return true;
                    }

                    // Never replace a library file by renaming over it.
                    if (_diskProvider.FileExists(finalPath))
                    {
                        _logger.Warn("'{0}' is ready, but a file already exists at its final path; it stays under its pending name", file.FinalPath);
                        return false;
                    }

                    proxy.RenameFile(hash, file.PendingPath, file.FinalPath, settings);
                    file.State = EngineLinkFileState.Renamed;

                    return true;
                }

                case EngineLinkFileState.Renamed:
                {
                    // The engine renames asynchronously; record the file once the rename is on disk.
                    if (!_diskProvider.FileExists(finalPath) || _diskProvider.FileExists(pendingPath))
                    {
                        return false;
                    }

                    var localEpisode = GetDecision(finalPath, file.FinalPath, series, episodes, downloadItem, link).LocalEpisode;
                    localEpisode.InPlace = true;

                    var result = _importApprovedEpisodes.Import(new List<ImportDecision> { new ImportDecision(localEpisode) }, true, downloadItem, ImportMode.Move).FirstOrDefault();

                    if (result?.Result != ImportResultType.Imported)
                    {
                        _logger.Warn("Unable to record '{0}': {1}", file.FinalPath, string.Join(", ", result?.Errors ?? new List<string>()));
                        return false;
                    }

                    file.State = EngineLinkFileState.Imported;

                    return true;
                }

                default:
                    return false;
            }
        }

        private bool ProcessSubtitle(IQBittorrentProxy proxy, QBittorrentSettings settings, string hash, EngineLink link, EngineLinkFile file, List<QBittorrentTorrentFile> engineFiles)
        {
            if (file.State != EngineLinkFileState.Pending)
            {
                return false;
            }

            var video = link.Files.FirstOrDefault(f => f.Index == file.VideoIndex);
            var pendingPath = Path.Combine(link.SavePath, file.PendingPath);

            if (video?.State == EngineLinkFileState.Rejected)
            {
                // A video's subtitles go with it.
                proxy.SetFilePriority(hash, new[] { file.Index }, 0, settings);
                DeleteIfExists(pendingPath);
                file.State = EngineLinkFileState.Rejected;

                return true;
            }

            // A subtitle is published once its video is in the library (subtitle validation and registration: §4.8).
            if (video?.State != EngineLinkFileState.Imported || !IsComplete(engineFiles, file) || !_diskProvider.FileExists(pendingPath) ||
                _diskProvider.FileExists(Path.Combine(link.SavePath, file.FinalPath)))
            {
                return false;
            }

            proxy.RenameFile(hash, file.PendingPath, file.FinalPath, settings);
            file.State = EngineLinkFileState.Imported;

            return true;
        }

        private void RejectFile(IQBittorrentProxy proxy, QBittorrentSettings settings, string hash, EngineLink link, EngineLinkFile file, string pendingPath, List<EpisodeHistory> grabs)
        {
            // The file was never published under its final name; stop downloading it and delete it.
            proxy.SetFilePriority(hash, new[] { file.Index }, 0, settings);
            DeleteIfExists(pendingPath);
            file.State = EngineLinkFileState.Rejected;

            // Blocklist the release and search again, once per grab.
            if (_downloadHistoryService.GetLatestDownloadHistoryItem(grabs.First().DownloadId) is not { EventType: DownloadHistoryEventType.DownloadFailed })
            {
                _failedDownloadService.MarkAsFailed(grabs.MaxBy(g => g.Date).Id, RejectedMessage, "Embarr");
            }
        }

        private void Complete(string downloadId, EngineLink link, Series series, List<Episode> seriesEpisodes, List<EpisodeHistory> grabs)
        {
            link.Completed = true;

            var imported = link.Files.Where(f => !f.IsSubtitle && f.State == EngineLinkFileState.Imported).ToList();

            if (imported.Empty())
            {
                return;
            }

            var episodes = imported.SelectMany(f => GetEpisodes(f, seriesEpisodes)).DistinctBy(e => e.Id).ToList();
            var episodeFiles = _episodeService.GetEpisodes(episodes.Select(e => e.Id))
                .Where(e => e.EpisodeFileId > 0)
                .Select(e => e.EpisodeFile.Value)
                .Where(f => f != null)
                .DistinctBy(f => f.Id)
                .ToList();

            var trackedDownload = _trackedDownloadService.Find(downloadId);

            if (trackedDownload == null)
            {
                return;
            }

            // Records the download as imported, like the folder import does once it has every episode.
            trackedDownload.State = TrackedDownloadState.Imported;
            _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, series, episodes, episodeFiles, new GrabbedReleaseInfo(grabs)));
        }

        private ImportDecision GetDecision(string path, string finalName, Series series, List<Episode> episodes, DownloadClientItem downloadItem, EngineLink link)
        {
            var localEpisode = new LocalEpisode
            {
                Series = series,
                Path = path,
                Size = _diskProvider.GetFileSize(path),

                // Parse the name the file will have, not its hidden pending name.
                FileEpisodeInfo = Parser.Parser.ParsePath(finalName),
                DownloadClientEpisodeInfo = Parser.Parser.ParseTitle(link.SourceTitle),
                DownloadItem = downloadItem,
                SceneSource = true,
                OtherVideoFiles = link.Files.Count(f => !f.IsSubtitle) > 1
            };

            localEpisode.ReleaseType = localEpisode.DownloadClientEpisodeInfo?.ReleaseType ?? localEpisode.FileEpisodeInfo?.ReleaseType ?? ReleaseType.Unknown;

            try
            {
                _aggregationService.Augment(localEpisode, downloadItem);
            }
            catch (AugmentingFailedException)
            {
                return new ImportDecision(localEpisode, new ImportRejection(ImportRejectionReason.UnableToParse, "Unable to parse file"));
            }

            // The info-hash link decides the episodes, not the parsed name (roadmap §4.4 rule 1).
            localEpisode.Episodes = episodes;
            _formatCalculator.UpdateEpisodeCustomFormats(localEpisode);

            return _importDecisionMaker.GetDecision(localEpisode, downloadItem);
        }

        private static List<Episode> GetEpisodes(EngineLinkFile file, List<Episode> seriesEpisodes)
        {
            return file.Episodes
                .Select(e => seriesEpisodes.FirstOrDefault(s => s.SeasonNumber == e.SeasonNumber && s.EpisodeNumber == e.EpisodeNumber))
                .Where(e => e != null)
                .ToList();
        }

        private static bool IsComplete(List<QBittorrentTorrentFile> engineFiles, EngineLinkFile file)
        {
            return file.Index < engineFiles.Count && engineFiles[file.Index].Progress >= 1.0;
        }

        private void DeleteIfExists(string path)
        {
            if (_diskProvider.FileExists(path))
            {
                _diskProvider.DeleteFile(path);
            }
        }
    }
}
