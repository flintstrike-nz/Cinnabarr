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
    // Every transition is saved to the link sidecar as it happens, and on each poll the recorded state is reconciled
    // with the engine, the disk and the library, so a failure or restart never imports a file twice.
    public class EngineFileImportService : IEngineFileImportService
    {
        public const string RejectedMessage = "A downloaded file failed its import checks";
        private const string IncompleteSuffix = ".!qB";

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

                // Done once the download is recorded and no subtitle is still waiting to be published.
                if (link?.Files == null || (link.Completed && link.Files.All(f => f.State != EngineLinkFileState.Pending)))
                {
                    continue;
                }

                // Only downloads Embarr is tracking and still processing: a download the user ignored (or one that
                // failed) is left alone, as the folder import does.
                var trackedDownload = _trackedDownloadService.Find(downloadId);

                if (trackedDownload == null || trackedDownload.State is TrackedDownloadState.Ignored or TrackedDownloadState.Failed or TrackedDownloadState.FailedPending)
                {
                    continue;
                }

                try
                {
                    ProcessTorrent(proxy, settings, torrent, downloadId, link, trackedDownload);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to import files from '{0}'", torrent.Name);
                }
            }
        }

        private void ProcessTorrent(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent, string downloadId, EngineLink link, TrackedDownload trackedDownload)
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

            void Save() => _engineLinkService.Save(downloadId, link);

            foreach (var file in link.Files.Where(f => !f.IsSubtitle))
            {
                ProcessVideo(proxy, settings, hash, link, file, engineFiles, series, seriesEpisodes, trackedDownload.DownloadItem, grabs, Save);
            }

            foreach (var file in link.Files.Where(f => f.IsSubtitle))
            {
                ProcessSubtitle(proxy, settings, hash, link, file, engineFiles, Save);
            }

            var videos = link.Files.Where(f => !f.IsSubtitle).ToList();

            if (!link.Completed && videos.All(f => f.State is EngineLinkFileState.Imported or EngineLinkFileState.Rejected))
            {
                Complete(link, series, seriesEpisodes, grabs, trackedDownload);
                Save();

                // Every video was rejected (and deleted): nothing is left to download or seed. The data flag stays off,
                // so removing the torrent never touches the library.
                if (videos.All(f => f.State == EngineLinkFileState.Rejected))
                {
                    proxy.RemoveTorrent(hash, false, settings);
                }
            }
        }

        private void ProcessVideo(IQBittorrentProxy proxy,
                                  QBittorrentSettings settings,
                                  string hash,
                                  EngineLink link,
                                  EngineLinkFile file,
                                  List<QBittorrentTorrentFile> engineFiles,
                                  Series series,
                                  List<Episode> seriesEpisodes,
                                  DownloadClientItem downloadItem,
                                  List<EpisodeHistory> grabs,
                                  Action save)
        {
            var pendingPath = Path.Combine(link.SavePath, file.PendingPath);
            var finalPath = Path.Combine(link.SavePath, file.FinalPath);

            // Every mapped episode must still exist; otherwise the file waits rather than import partly.
            var episodes = GetEpisodes(file, seriesEpisodes);

            if (episodes == null)
            {
                _logger.Debug("'{0}': not every mapped episode is in the library; waiting", file.FinalPath);
                return;
            }

            // Recover a rename that happened before its state was saved.
            if (file.State == EngineLinkFileState.Pending && IsRenamedInEngine(engineFiles, file) && _diskProvider.FileExists(finalPath) && !_diskProvider.FileExists(pendingPath))
            {
                file.State = EngineLinkFileState.Renamed;
                save();
            }

            switch (file.State)
            {
                case EngineLinkFileState.Pending:
                {
                    if (!IsComplete(engineFiles, file) || !_diskProvider.FileExists(pendingPath))
                    {
                        return;
                    }

                    var decision = GetDecision(pendingPath, file.FinalPath, series, episodes, downloadItem, link);

                    if (!decision.Approved)
                    {
                        _logger.Warn("'{0}' failed its import checks: {1}", file.FinalPath, string.Join(", ", decision.Rejections.Select(r => r.Message)));
                        RejectFile(proxy, settings, hash, file, pendingPath, grabs);
                        save();

                        return;
                    }

                    // Never replace a library file by renaming over it.
                    if (_diskProvider.FileExists(finalPath))
                    {
                        _logger.Warn("'{0}' is ready, but a file already exists at its final path; it stays under its pending name", file.FinalPath);
                        return;
                    }

                    proxy.RenameFile(hash, file.PendingPath, file.FinalPath, settings);
                    file.State = EngineLinkFileState.Renamed;
                    save();

                    return;
                }

                case EngineLinkFileState.Renamed:
                {
                    // The engine renames asynchronously; record the file once the rename is on disk.
                    if (!_diskProvider.FileExists(finalPath) || _diskProvider.FileExists(pendingPath))
                    {
                        return;
                    }

                    // Already recorded (the import finished but its state wasn't saved): never import it again,
                    // which would treat the file as an upgrade of itself.
                    if (IsRecorded(series, episodes, finalPath))
                    {
                        file.State = EngineLinkFileState.Imported;
                        save();

                        return;
                    }

                    var decision = GetDecision(finalPath, file.FinalPath, series, episodes, downloadItem, link);

                    if (!decision.Approved)
                    {
                        // It already passed under its pending name; leave it for the next poll rather than force it in.
                        _logger.Warn("'{0}' can't be recorded yet: {1}", file.FinalPath, string.Join(", ", decision.Rejections.Select(r => r.Message)));
                        return;
                    }

                    var result = _importApprovedEpisodes.Import(new List<ImportDecision> { decision }, true, downloadItem, ImportMode.Move).FirstOrDefault();

                    if (result?.Result != ImportResultType.Imported)
                    {
                        _logger.Warn("Unable to record '{0}': {1}", file.FinalPath, string.Join(", ", result?.Errors ?? new List<string>()));
                        return;
                    }

                    file.State = EngineLinkFileState.Imported;
                    save();

                    return;
                }
            }
        }

        private void ProcessSubtitle(IQBittorrentProxy proxy, QBittorrentSettings settings, string hash, EngineLink link, EngineLinkFile file, List<QBittorrentTorrentFile> engineFiles, Action save)
        {
            if (file.State != EngineLinkFileState.Pending)
            {
                return;
            }

            var video = link.Files.FirstOrDefault(f => f.Index == file.VideoIndex);
            var pendingPath = Path.Combine(link.SavePath, file.PendingPath);

            if (video?.State == EngineLinkFileState.Rejected)
            {
                // A video's subtitles go with it.
                proxy.SetFilePriority(hash, new[] { file.Index }, 0, settings);
                DeletePending(pendingPath);
                file.State = EngineLinkFileState.Rejected;
                save();

                return;
            }

            // A subtitle is published once its video is in the library (subtitle validation and registration: §4.8).
            if (video?.State != EngineLinkFileState.Imported || !IsComplete(engineFiles, file) || !_diskProvider.FileExists(pendingPath) ||
                _diskProvider.FileExists(Path.Combine(link.SavePath, file.FinalPath)))
            {
                return;
            }

            proxy.RenameFile(hash, file.PendingPath, file.FinalPath, settings);
            file.State = EngineLinkFileState.Imported;
            save();
        }

        private void RejectFile(IQBittorrentProxy proxy, QBittorrentSettings settings, string hash, EngineLinkFile file, string pendingPath, List<EpisodeHistory> grabs)
        {
            // The file was never published under its final name; stop downloading it and delete it.
            proxy.SetFilePriority(hash, new[] { file.Index }, 0, settings);
            DeletePending(pendingPath);
            file.State = EngineLinkFileState.Rejected;

            // Blocklist the release and search again, once per grab.
            if (_downloadHistoryService.GetLatestDownloadHistoryItem(grabs.First().DownloadId) is not { EventType: DownloadHistoryEventType.DownloadFailed })
            {
                _failedDownloadService.MarkAsFailed(grabs.MaxBy(g => g.Date).Id, RejectedMessage, "Embarr");
            }
        }

        private void Complete(EngineLink link, Series series, List<Episode> seriesEpisodes, List<EpisodeHistory> grabs, TrackedDownload trackedDownload)
        {
            link.Completed = true;

            var imported = link.Files.Where(f => !f.IsSubtitle && f.State == EngineLinkFileState.Imported).ToList();

            if (imported.Empty())
            {
                return;
            }

            var episodes = imported.SelectMany(f => GetEpisodes(f, seriesEpisodes) ?? new List<Episode>()).DistinctBy(e => e.Id).ToList();
            var episodeFiles = _episodeService.GetEpisodes(episodes.Select(e => e.Id))
                .Where(e => e.EpisodeFileId > 0)
                .Select(e => e.EpisodeFile.Value)
                .Where(f => f != null)
                .DistinctBy(f => f.Id)
                .ToList();

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
                OtherVideoFiles = link.Files.Count(f => !f.IsSubtitle) > 1,

                // Already in its final folder: checks such as free space must not count it twice.
                InPlace = true
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

        private bool IsRecorded(Series series, List<Episode> episodes, string finalPath)
        {
            var relativePath = series.Path.GetRelativePath(finalPath);

            return episodes.All(e => e.EpisodeFileId > 0 && e.EpisodeFile?.Value?.RelativePath?.PathEquals(relativePath) == true);
        }

        // Null when any mapped episode no longer exists.
        private static List<Episode> GetEpisodes(EngineLinkFile file, List<Episode> seriesEpisodes)
        {
            var episodes = file.Episodes
                .Select(e => seriesEpisodes.FirstOrDefault(s => s.SeasonNumber == e.SeasonNumber && s.EpisodeNumber == e.EpisodeNumber))
                .ToList();

            return episodes.Any(e => e == null) ? null : episodes;
        }

        private static bool IsComplete(List<QBittorrentTorrentFile> engineFiles, EngineLinkFile file)
        {
            return file.Index < engineFiles.Count && engineFiles[file.Index].Progress >= 1.0;
        }

        private static bool IsRenamedInEngine(List<QBittorrentTorrentFile> engineFiles, EngineLinkFile file)
        {
            return file.Index < engineFiles.Count && engineFiles[file.Index].Name.Replace('\\', '/') == file.FinalPath;
        }

        // Removes the pending file, including the engine's incomplete-name variant if it hadn't finished.
        private void DeletePending(string pendingPath)
        {
            foreach (var path in new[] { pendingPath, pendingPath + IncompleteSuffix })
            {
                if (_diskProvider.FileExists(path))
                {
                    _diskProvider.DeleteFile(path);
                }
            }
        }
    }
}
