using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
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

        // Rejections that mean the file itself is unusable. Anything else (a title not yet known, an unverified scene
        // mapping, free space, a check that errored) can pass later, so the file waits instead of being deleted.
        private static readonly HashSet<ImportRejectionReason> PermanentRejections = new()
        {
            ImportRejectionReason.Sample,
            ImportRejectionReason.NoAudio,
            ImportRejectionReason.DangerousFile,
            ImportRejectionReason.ExecutableFile,
            ImportRejectionReason.ArchiveFile,
            ImportRejectionReason.UnsupportedExtension,
            ImportRejectionReason.UserRejectedExtension
        };

        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly IEngineLinkService _engineLinkService;
        private readonly IHistoryService _historyService;
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
        private readonly IEngineFileMapper _fileMapper;
        private readonly IEngineFileReplacementService _replacementService;
        private readonly Logger _logger;

        // Links checked against the library since startup (a restored database can have lost what a link records).
        private readonly ConcurrentDictionary<string, bool> _reconciled = new();

        // The last reason logged for each waiting file, so a file that keeps waiting doesn't log on every poll.
        private readonly ConcurrentDictionary<string, string> _waiting = new();

        public EngineFileImportService(IQBittorrentProxySelector proxySelector,
                                       IEngineLinkService engineLinkService,
                                       IHistoryService historyService,
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
                                       IEngineFileMapper fileMapper,
                                       IEngineFileReplacementService replacementService,
                                       Logger logger)
        {
            _proxySelector = proxySelector;
            _engineLinkService = engineLinkService;
            _historyService = historyService;
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
            _fileMapper = fileMapper;
            _replacementService = replacementService;
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

                if (link?.Files == null)
                {
                    continue;
                }

                try
                {
                    // Every video was rejected or replaced but the torrent is still here: remove it (this also retries a
                    // removal that failed).
                    if (link.Completed && NoVideosLeft(link))
                    {
                        RemoveTorrent(proxy, settings, torrent);
                        continue;
                    }

                    // Done once the download is recorded, no subtitle is still waiting to be published, and the
                    // link has been checked against the library since startup.
                    if (link.Completed &&
                        link.Files.All(f => f.State is not (EngineLinkFileState.Pending or EngineLinkFileState.Renamed) && (f.DetachedFrom == null || f.DetachedFrom.Empty())) &&
                        _reconciled.ContainsKey(downloadId))
                    {
                        continue;
                    }

                    // Only downloads Embarr is tracking: a download the user ignored is left alone, as the folder
                    // import does.
                    var trackedDownload = _trackedDownloadService.Find(downloadId);

                    if (trackedDownload == null || trackedDownload.State == TrackedDownloadState.Ignored)
                    {
                        continue;
                    }

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
            var history = _historyService.FindByDownloadId(downloadId);
            var grabs = history.Where(h => h.EventType == EpisodeHistoryEventType.Grabbed).ToList();

            if (grabs.Empty())
            {
                return;
            }

            // Failures recorded since the latest grab. Those Embarr recorded for a rejected file don't stop the rest
            // of the torrent; any other failure (the engine reported an error, or someone marked it failed) does.
            var latestGrab = grabs.Max(g => g.Date);
            var failures = history.Where(h => h.EventType == EpisodeHistoryEventType.DownloadFailed && h.Date >= latestGrab).ToList();
            var failedForRejection = failures.Any() && failures.All(IsRejectionFailure);

            if (trackedDownload.State == TrackedDownloadState.FailedPending ||
                (trackedDownload.State == TrackedDownloadState.Failed && !failedForRejection))
            {
                return;
            }

            var engineFiles = proxy.GetTorrentFiles(hash, settings);
            var series = _seriesService.GetSeries(grabs.First().SeriesId);
            var seriesEpisodes = _episodeService.GetEpisodeBySeries(series.Id);

            void Save() => _engineLinkService.Save(downloadId, link);

            if (!_reconciled.ContainsKey(downloadId))
            {
                Reconcile(link, series, seriesEpisodes, Save);
                _reconciled[downloadId] = true;
            }

            // Shared by every file, so the blocklist and search happen once per grab even when several files are
            // rejected in the same poll.
            var failedSinceGrab = new StrongBox<bool>(failures.Any());

            foreach (var file in link.Files.Where(f => !f.IsSubtitle))
            {
                ProcessVideo(proxy, settings, hash, downloadId, link, file, engineFiles, series, seriesEpisodes, trackedDownload, grabs, failedSinceGrab, Save);
            }

            foreach (var file in link.Files.Where(f => f.IsSubtitle))
            {
                ProcessSubtitle(proxy, settings, hash, link, file, engineFiles, Save);
            }

            var videos = link.Files.Where(f => !f.IsSubtitle).ToList();

            // Completion can let the download be removed, so it waits until no subtitle rename is still outstanding.
            if (!link.Completed &&
                videos.All(f => f.State is EngineLinkFileState.Imported or EngineLinkFileState.Rejected or EngineLinkFileState.Replaced) &&
                link.Files.All(f => !(f.IsSubtitle && f.State == EngineLinkFileState.Renamed)))
            {
                Complete(link, series, seriesEpisodes, grabs, trackedDownload);
                Save();

                // Every video was rejected (and deleted) or replaced: nothing is left to download or seed. Retried
                // on later polls if it fails.
                if (NoVideosLeft(link))
                {
                    RemoveTorrent(proxy, settings, torrent);
                }
            }
        }

        // After a database restore, a file the link records as imported may no longer be in the library: record it
        // again (in place) and report the download as completed again.
        private void Reconcile(EngineLink link, Series series, List<Episode> seriesEpisodes, Action save)
        {
            var lost = link.Files
                .Where(f => !f.IsSubtitle && f.State == EngineLinkFileState.Imported)
                .Where(f =>
                {
                    var episodes = GetEpisodes(f, seriesEpisodes);
                    var finalPath = Path.Combine(link.SavePath, f.FinalPath);

                    return episodes != null && _diskProvider.FileExists(finalPath) && !IsRecorded(series, episodes, finalPath, f);
                })
                .ToList();

            if (lost.Empty())
            {
                return;
            }

            foreach (var file in lost)
            {
                _logger.Info("'{0}' is in the library folder but no longer recorded; recording it again", file.FinalPath);
                file.State = EngineLinkFileState.Renamed;
            }

            link.Completed = false;
            save();
        }

        private void RemoveTorrent(IQBittorrentProxy proxy, QBittorrentSettings settings, QBittorrentTorrent torrent)
        {
            // The data flag stays off, so removing the torrent never touches the library.
            proxy.RemoveTorrent(torrent.Hash.ToLowerInvariant(), false, settings);
        }

        // Every video was rejected or replaced by a newer file: nothing is left to seed.
        private static bool NoVideosLeft(EngineLink link)
        {
            var videos = link.Files.Where(f => !f.IsSubtitle).ToList();

            return videos.Any() && videos.All(f => f.State is EngineLinkFileState.Rejected or EngineLinkFileState.Replaced);
        }

        private static bool IsRejectionFailure(EpisodeHistory failure)
        {
            return failure.Data != null && failure.Data.TryGetValue("Message", out var message) && message == RejectedMessage;
        }

        private void ProcessVideo(IQBittorrentProxy proxy,
                                  QBittorrentSettings settings,
                                  string hash,
                                  string downloadId,
                                  EngineLink link,
                                  EngineLinkFile file,
                                  List<QBittorrentTorrentFile> engineFiles,
                                  Series series,
                                  List<Episode> seriesEpisodes,
                                  TrackedDownload trackedDownload,
                                  List<EpisodeHistory> grabs,
                                  StrongBox<bool> failedSinceGrab,
                                  Action save)
        {
            var downloadItem = trackedDownload.DownloadItem;
            var waitingKey = downloadId + ":" + file.Index;
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
                case EngineLinkFileState.Imported:
                {
                    // A recheck that failed or was cut short by a restart.
                    RecheckDetached(proxy, settings, file, save);
                    return;
                }

                case EngineLinkFileState.Pending:
                {
                    if (!IsComplete(engineFiles, file) || !_diskProvider.FileExists(pendingPath))
                    {
                        return;
                    }

                    // When the episodes already have a file, the checks include whether this one is an upgrade.
                    var decision = GetDecision(pendingPath, file, series, episodes, trackedDownload, link);

                    if (!decision.Approved)
                    {
                        var reasons = string.Join(", ", decision.Rejections.Select(r => r.Message));

                        if (!decision.Rejections.Any(r => PermanentRejections.Contains(r.Reason)))
                        {
                            LogWaiting(waitingKey, "'{0}' can't be imported yet: {1}", file.FinalPath, reasons);
                            return;
                        }

                        _logger.Warn("'{0}' failed its import checks: {1}", file.FinalPath, reasons);
                        RejectFile(proxy, settings, hash, file, pendingPath, grabs, failedSinceGrab);
                        save();

                        return;
                    }

                    // The final name can use what the checks learned, such as media info.
                    if (_fileMapper.UpdateFinalPaths(link, file, series, decision.LocalEpisode))
                    {
                        finalPath = Path.Combine(link.SavePath, file.FinalPath);
                        save();
                    }

                    // An upgrade: detach the files it replaces from any torrent still seeding them, and clear the
                    // final path if one of them holds it. Saved first, so a restart carries on with the replacement.
                    var existingFiles = GetEpisodeFiles(episodes);

                    // Never rename over a file that isn't one being replaced (checked before anything is detached).
                    if (_diskProvider.FileExists(finalPath) && !existingFiles.Any(f => Path.Combine(series.Path, f.RelativePath).PathEquals(finalPath)))
                    {
                        LogWaiting(waitingKey, "'{0}' is ready, but a file already exists at its final path; it stays under its pending name", file.FinalPath);
                        return;
                    }

                    // Another download already replacing the same files, or headed for the same path, goes first: its
                    // new file may already be at the path the old record still points to.
                    if (existingFiles.Any() && _replacementService.IsReserved(downloadId, finalPath, existingFiles))
                    {
                        LogWaiting(waitingKey, "'{0}' is ready, but another download is replacing the same file; it stays under its pending name", file.FinalPath);
                        return;
                    }

                    if (existingFiles.Any())
                    {
                        // The torrents to recheck are saved before any is changed, so a detachment cut short is still
                        // rechecked once the replacement is in.
                        file.ReplacesFileIds = existingFiles.Select(f => f.Id).ToList();
                        file.DetachedFrom = (file.DetachedFrom ?? new List<string>()).Union(_replacementService.FindHolders(downloadId, series, existingFiles)).ToList();
                        save();

                        _replacementService.Detach(proxy, settings, downloadId, series, existingFiles, finalPath);
                    }

                    if (_diskProvider.FileExists(finalPath))
                    {
                        LogWaiting(waitingKey, "'{0}' is ready, but a file already exists at its final path; it stays under its pending name", file.FinalPath);
                        return;
                    }

                    _waiting.TryRemove(waitingKey, out _);
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
                        RetryRename(proxy, settings, hash, file, engineFiles, pendingPath, finalPath);
                        return;
                    }

                    // Already recorded (the import finished but its state wasn't saved): never import it again,
                    // which would treat the file as an upgrade of itself.
                    if (IsRecorded(series, episodes, finalPath, file))
                    {
                        file.State = EngineLinkFileState.Imported;
                        save();

                        RecheckDetached(proxy, settings, file, save);

                        return;
                    }

                    // Another file was recorded for these episodes since this one was renamed: never replace it here.
                    if (HasOtherFile(series, episodes, finalPath, file))
                    {
                        LogWaiting(waitingKey, "'{0}' can't be recorded: its episodes already have another file", file.FinalPath);
                        return;
                    }

                    var decision = GetDecision(finalPath, file, series, episodes, trackedDownload, link);

                    if (!decision.Approved)
                    {
                        // It already passed under its pending name; leave it for the next poll rather than force it in.
                        LogWaiting(waitingKey, "'{0}' can't be recorded yet: {1}", file.FinalPath, string.Join(", ", decision.Rejections.Select(r => r.Message)));
                        return;
                    }

                    var result = _importApprovedEpisodes.Import(new List<ImportDecision> { decision }, true, downloadItem, ImportMode.Move).FirstOrDefault();

                    if (result?.Result != ImportResultType.Imported)
                    {
                        _logger.Warn("Unable to record '{0}': {1}", file.FinalPath, string.Join(", ", result?.Errors ?? new List<string>()));
                        return;
                    }

                    _waiting.TryRemove(waitingKey, out _);
                    file.State = EngineLinkFileState.Imported;
                    save();

                    RecheckDetached(proxy, settings, file, save);

                    return;
                }
            }
        }

        // The files this one replaced are gone: their torrents stop offering them. Cleared only once done, so a
        // failure is retried on the next poll.
        private void RecheckDetached(IQBittorrentProxy proxy, QBittorrentSettings settings, EngineLinkFile file, Action save)
        {
            if (file.DetachedFrom == null || file.DetachedFrom.Empty())
            {
                return;
            }

            _replacementService.Recheck(proxy, settings, file.DetachedFrom);
            file.DetachedFrom = null;
            save();
        }

        private void LogWaiting(string key, string message, params object[] args)
        {
            var text = string.Format(message, args);

            if (_waiting.TryGetValue(key, out var previous) && previous == text)
            {
                _logger.Debug(text);
                return;
            }

            _waiting[key] = text;
            _logger.Warn(text);
        }

        private void ProcessSubtitle(IQBittorrentProxy proxy, QBittorrentSettings settings, string hash, EngineLink link, EngineLinkFile file, List<QBittorrentTorrentFile> engineFiles, Action save)
        {
            if (file.State is not (EngineLinkFileState.Pending or EngineLinkFileState.Renamed))
            {
                return;
            }

            var video = link.Files.FirstOrDefault(f => f.Index == file.VideoIndex);
            var pendingPath = Path.Combine(link.SavePath, file.PendingPath);
            var finalPath = Path.Combine(link.SavePath, file.FinalPath);

            // Published once the engine reports the final name and the rename is on disk (this also recovers a
            // rename whose state wasn't saved).
            if (IsRenamedInEngine(engineFiles, file) && _diskProvider.FileExists(finalPath) && !_diskProvider.FileExists(pendingPath))
            {
                file.State = EngineLinkFileState.Imported;
                save();

                return;
            }

            if (file.State == EngineLinkFileState.Renamed)
            {
                RetryRename(proxy, settings, hash, file, engineFiles, pendingPath, finalPath);
                return;
            }

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
                _diskProvider.FileExists(finalPath))
            {
                return;
            }

            proxy.RenameFile(hash, file.PendingPath, file.FinalPath, settings);
            file.State = EngineLinkFileState.Renamed;
            save();
        }

        // A rename the engine hasn't carried out (it failed, or was lost in a restart) is asked for again. Asking
        // again while the first is still in flight is harmless: the engine skips a rename whose source is gone.
        private void RetryRename(IQBittorrentProxy proxy, QBittorrentSettings settings, string hash, EngineLinkFile file, List<QBittorrentTorrentFile> engineFiles, string pendingPath, string finalPath)
        {
            if (IsRenamedInEngine(engineFiles, file) || !_diskProvider.FileExists(pendingPath) || _diskProvider.FileExists(finalPath))
            {
                return;
            }

            _logger.Debug("'{0}' hasn't been renamed yet; asking the engine again", file.FinalPath);
            proxy.RenameFile(hash, file.PendingPath, file.FinalPath, settings);
        }

        private void RejectFile(IQBittorrentProxy proxy, QBittorrentSettings settings, string hash, EngineLinkFile file, string pendingPath, List<EpisodeHistory> grabs, StrongBox<bool> failedSinceGrab)
        {
            // The file was never published under its final name; stop downloading it and delete it.
            proxy.SetFilePriority(hash, new[] { file.Index }, 0, settings);
            DeletePending(pendingPath);
            file.State = EngineLinkFileState.Rejected;

            // Blocklist the release and search again, once per grab.
            if (!failedSinceGrab.Value)
            {
                failedSinceGrab.Value = true;
                _failedDownloadService.MarkAsFailed(grabs.MaxBy(g => g.Date).Id, RejectedMessage, "Embarr");
            }
        }

        private void Complete(EngineLink link, Series series, List<Episode> seriesEpisodes, List<EpisodeHistory> grabs, TrackedDownload trackedDownload)
        {
            link.Completed = true;

            // A file replaced by a newer one was imported first, so it counts.
            var imported = link.Files.Where(f => !f.IsSubtitle && f.State is EngineLinkFileState.Imported or EngineLinkFileState.Replaced).ToList();

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

        private ImportDecision GetDecision(string path, EngineLinkFile file, Series series, List<Episode> episodes, TrackedDownload trackedDownload, EngineLink link)
        {
            var downloadItem = trackedDownload.DownloadItem;

            var localEpisode = new LocalEpisode
            {
                Series = series,
                Path = path,
                Size = _diskProvider.GetFileSize(path),

                // Parse the file's name in the torrent (it carries tags such as REPACK or the release group), or the
                // name it will have; never its hidden pending name.
                FileEpisodeInfo = (file.OriginalName == null ? null : Parser.Parser.ParsePath(file.OriginalName)) ?? Parser.Parser.ParsePath(file.FinalPath),
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

            // A file of a pack takes its scene name from its name in the torrent, not from its pending or final name.
            if (localEpisode.OtherVideoFiles || localEpisode.DownloadClientEpisodeInfo == null || localEpisode.DownloadClientEpisodeInfo.FullSeason)
            {
                var originalStem = file.OriginalName == null ? null : Path.GetFileNameWithoutExtension(file.OriginalName);

                localEpisode.SceneName = originalStem != null && Parser.SceneChecker.IsSceneTitle(originalStem) ? originalStem : null;
            }

            // The info-hash link decides the episodes, not the parsed name (roadmap §4.4 rule 1).
            localEpisode.Episodes = episodes;
            localEpisode.IndexerFlags = trackedDownload.RemoteEpisode?.Release?.IndexerFlags ?? 0;
            _formatCalculator.UpdateEpisodeCustomFormats(localEpisode);

            return _importDecisionMaker.GetDecision(localEpisode, downloadItem);
        }

        // True when this file is what the episodes record (a file it replaces at the same path doesn't count).
        private static bool IsRecorded(Series series, List<Episode> episodes, string finalPath, EngineLinkFile file = null)
        {
            return episodes.All(e => IsFileAt(series, e, finalPath) && !IsReplaced(file, e));
        }

        private static bool IsReplaced(EngineLinkFile file, Episode episode)
        {
            return file?.ReplacesFileIds?.Contains(episode.EpisodeFileId) == true;
        }

        private static List<EpisodeFile> GetEpisodeFiles(List<Episode> episodes)
        {
            return episodes.Where(e => e.EpisodeFileId > 0)
                .Select(e => e.EpisodeFile?.Value)
                .Where(f => f != null)
                .DistinctBy(f => f.Id)
                .ToList();
        }

        private static bool IsFileAt(Series series, Episode episode, string path)
        {
            var relativePath = episode.EpisodeFileId > 0 ? episode.EpisodeFile?.Value?.RelativePath : null;

            return relativePath != null && Path.Combine(series.Path, relativePath).PathEquals(path);
        }

        // True when any of the episodes has a file other than the one at this final path.
        private static bool HasOtherFile(Series series, List<Episode> episodes, string finalPath, EngineLinkFile file)
        {
            return episodes.Any(e => e.EpisodeFileId > 0 && !IsFileAt(series, e, finalPath) && !IsReplaced(file, e));
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
            foreach (var path in new[] { pendingPath, pendingPath + EngineLinkService.IncompleteSuffix })
            {
                if (_diskProvider.FileExists(path))
                {
                    _diskProvider.DeleteFile(path);
                }
            }
        }
    }
}
