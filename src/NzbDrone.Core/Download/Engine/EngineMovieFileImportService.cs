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
using NzbDrone.Core.Extras.MovieExtras;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineMovieFileImportService
    {
        // Imports a built-in engine torrent's movie as soon as it completes (roadmap §4.8 "Import is per file").
        void Process(IDownloadClient downloadClient);
    }

    // The movie side of EngineFileImportService, with the same states and rules: the video is checked under its
    // pending name; if it passes, the engine renames it to its final name (an instant rename in the movie's folder, so
    // it keeps seeding); once the rename is on disk the file is recorded in place. Every transition is saved to the link
    // as it happens, and on each poll the recorded state is reconciled with the engine, the disk and the library, so a
    // failure or restart never imports a file twice. A movie is one video, with the subtitles that came with it.
    public class EngineMovieFileImportService : IEngineMovieFileImportService
    {
        // Rejections that mean the file itself is unusable. Anything else (an unverified title, free space, a check that
        // errored) can pass later, so the file waits instead of being deleted.
        private static readonly HashSet<ImportRejectionReason> PermanentRejections = new()
        {
            ImportRejectionReason.Sample,
            ImportRejectionReason.NoAudio,
            ImportRejectionReason.DangerousFile,
            ImportRejectionReason.ExecutableFile,
            ImportRejectionReason.ArchiveFile,
            ImportRejectionReason.UnsupportedExtension
        };

        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly IEngineLinkService _engineLinkService;
        private readonly IMovieHistoryService _historyService;
        private readonly IMovieService _movieService;
        private readonly IMovieFileService _movieFileService;
        private readonly IAggregationService _aggregationService;
        private readonly ILocalMovieCustomFormatCalculationService _formatCalculator;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedMovie _importApprovedMovies;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDiskProvider _diskProvider;
        private readonly IEngineFileMapper _fileMapper;
        private readonly IEngineFileReplacementService _replacementService;
        private readonly IEngineSubtitleValidator _subtitleValidator;
        private readonly IMovieSubtitleFileService _subtitleFileService;
        private readonly Logger _logger;

        // Links checked against the library since startup (a restored database can have lost what a link records).
        private readonly ConcurrentDictionary<string, bool> _reconciled = new();

        // The last reason logged for each waiting file, so a file that keeps waiting doesn't log on every poll.
        private readonly ConcurrentDictionary<string, string> _waiting = new();

        public EngineMovieFileImportService(IQBittorrentProxySelector proxySelector,
                                            IEngineLinkService engineLinkService,
                                            IMovieHistoryService historyService,
                                            IMovieService movieService,
                                            IMovieFileService movieFileService,
                                            IAggregationService aggregationService,
                                            ILocalMovieCustomFormatCalculationService formatCalculator,
                                            IMakeImportDecision importDecisionMaker,
                                            IImportApprovedMovie importApprovedMovies,
                                            IFailedDownloadService failedDownloadService,
                                            ITrackedDownloadService trackedDownloadService,
                                            IEventAggregator eventAggregator,
                                            IDiskProvider diskProvider,
                                            IEngineFileMapper fileMapper,
                                            IEngineFileReplacementService replacementService,
                                            IEngineSubtitleValidator subtitleValidator,
                                            IMovieSubtitleFileService subtitleFileService,
                                            Logger logger)
        {
            _proxySelector = proxySelector;
            _engineLinkService = engineLinkService;
            _historyService = historyService;
            _movieService = movieService;
            _movieFileService = movieFileService;
            _aggregationService = aggregationService;
            _formatCalculator = formatCalculator;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedMovies = importApprovedMovies;
            _failedDownloadService = failedDownloadService;
            _trackedDownloadService = trackedDownloadService;
            _eventAggregator = eventAggregator;
            _diskProvider = diskProvider;
            _fileMapper = fileMapper;
            _replacementService = replacementService;
            _subtitleValidator = subtitleValidator;
            _subtitleFileService = subtitleFileService;
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

                if (link?.Files == null || link.MediaType != EngineLinkService.MovieMediaType)
                {
                    continue;
                }

                try
                {
                    // The video was rejected or replaced but the torrent is still here: remove it (this also retries a
                    // removal that failed).
                    if (link.Completed && NoVideosLeft(link))
                    {
                        RemoveTorrent(proxy, settings, torrent);
                        continue;
                    }

                    // Done once the download is recorded, no subtitle is still waiting to be published, and the link
                    // has been checked against the library since startup.
                    if (link.Completed &&
                        link.Files.All(f => f.State is not (EngineLinkFileState.Pending or EngineLinkFileState.Renamed) && (f.DetachedFrom == null || f.DetachedFrom.Empty())) &&
                        _reconciled.ContainsKey(downloadId))
                    {
                        continue;
                    }

                    // Only downloads Cinnabarr is tracking: a download the user ignored is left alone.
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
            var grabs = history.Where(h => h.EventType == MovieHistoryEventType.Grabbed).ToList();

            if (grabs.Empty())
            {
                return;
            }

            // Failures recorded since the latest grab. Those Cinnabarr recorded for a rejected file don't stop the rest
            // of the torrent; any other failure (the engine reported an error, or someone marked it failed) does.
            var latestGrab = grabs.Max(g => g.Date);
            var failures = history.Where(h => h.EventType == MovieHistoryEventType.DownloadFailed && h.Date >= latestGrab).ToList();
            var failedForRejection = failures.Any() && failures.All(IsRejectionFailure);

            if (trackedDownload.State == TrackedDownloadState.FailedPending ||
                (trackedDownload.State == TrackedDownloadState.Failed && !failedForRejection))
            {
                return;
            }

            var engineFiles = proxy.GetTorrentFiles(hash, settings);
            var movieId = grabs.First().MovieId;

            void Save() => _engineLinkService.Save(downloadId, link);

            var movie = _movieService.GetMovie(movieId);

            if (!_reconciled.ContainsKey(downloadId))
            {
                Reconcile(link, movie, Save);
                _reconciled[downloadId] = true;
                movie = _movieService.GetMovie(movieId);
            }

            // Shared by every file, so the blocklist and search happen once per grab.
            var failedSinceGrab = new StrongBox<bool>(failures.Any());

            foreach (var file in link.Files.Where(f => !f.IsSubtitle))
            {
                ProcessVideo(proxy, settings, hash, downloadId, link, file, engineFiles, movie, trackedDownload, grabs, failedSinceGrab, Save);
            }

            // The video's import changed the movie's file: read it again before recording the subtitles.
            movie = _movieService.GetMovie(movieId);

            foreach (var file in link.Files.Where(f => f.IsSubtitle))
            {
                ProcessSubtitle(proxy, settings, hash, link, file, engineFiles, movie, Save);
            }

            var videos = link.Files.Where(f => !f.IsSubtitle).ToList();

            // Completion can let the download be removed, so it waits until no subtitle rename is still outstanding.
            if (!link.Completed &&
                videos.All(f => f.State is EngineLinkFileState.Imported or EngineLinkFileState.Rejected or EngineLinkFileState.Replaced) &&
                link.Files.All(f => !(f.IsSubtitle && f.State == EngineLinkFileState.Renamed)))
            {
                Complete(link, movie, grabs, trackedDownload);
                Save();

                // The video was rejected (and deleted) or replaced: nothing is left to download or seed.
                if (NoVideosLeft(link))
                {
                    RemoveTorrent(proxy, settings, torrent);
                }
            }

            // Once nothing is left in the torrent's staging folder (the last subtitle can follow later), it goes.
            if (link.Completed)
            {
                _engineLinkService.RemoveEmptyStagingFolder(link);
            }
        }

        // After a database restore, a file the link records as imported may no longer be in the library: record it
        // again (in place) and report the download as completed again.
        private void Reconcile(EngineLink link, Movie movie, Action save)
        {
            var lost = link.Files
                .Where(f => !f.IsSubtitle && f.State == EngineLinkFileState.Imported)
                .Where(f =>
                {
                    var finalPath = Path.Combine(link.SavePath, f.FinalPath);

                    return _diskProvider.FileExists(finalPath) && !IsRecorded(movie, finalPath, f);
                })
                .ToList();

            // Published subtitles the library doesn't record (a restore) are recorded again; those of a video being
            // recorded again follow once it is.
            foreach (var subtitle in link.Files.Where(f => f.IsSubtitle && f.State == EngineLinkFileState.Imported))
            {
                var video = link.Files.FirstOrDefault(f => f.Index == subtitle.VideoIndex && f.State == EngineLinkFileState.Imported && !lost.Contains(f));
                var subtitlePath = Path.Combine(link.SavePath, subtitle.FinalPath);

                if (video != null && _diskProvider.FileExists(subtitlePath))
                {
                    RecordSubtitle(movie, link, video, subtitle, subtitlePath);
                }
            }

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

        private static bool NoVideosLeft(EngineLink link)
        {
            var videos = link.Files.Where(f => !f.IsSubtitle).ToList();

            return videos.Any() && videos.All(f => f.State is EngineLinkFileState.Rejected or EngineLinkFileState.Replaced);
        }

        private static bool IsRejectionFailure(MovieHistory failure)
        {
            return failure.Data != null && failure.Data.TryGetValue("Message", out var message) && message == EngineFileImportService.RejectedMessage;
        }

        private void ProcessVideo(IQBittorrentProxy proxy,
                                  QBittorrentSettings settings,
                                  string hash,
                                  string downloadId,
                                  EngineLink link,
                                  EngineLinkFile file,
                                  List<QBittorrentTorrentFile> engineFiles,
                                  Movie movie,
                                  TrackedDownload trackedDownload,
                                  List<MovieHistory> grabs,
                                  StrongBox<bool> failedSinceGrab,
                                  Action save)
        {
            var downloadItem = trackedDownload.DownloadItem;
            var waitingKey = downloadId + ":" + file.Index;
            var pendingPath = Path.Combine(link.SavePath, file.PendingPath);
            var finalPath = Path.Combine(link.SavePath, file.FinalPath);

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

                    // When the movie already has a file, the checks include whether this one is an upgrade.
                    var decision = GetDecision(pendingPath, file, movie, trackedDownload, link);

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
                    if (_fileMapper.UpdateFinalPaths(link, file, movie, decision.LocalMovie))
                    {
                        finalPath = Path.Combine(link.SavePath, file.FinalPath);
                        save();
                    }

                    var existingFile = GetExistingFile(movie);

                    // Never rename over a file that isn't the one being replaced (checked before anything is detached).
                    if (_diskProvider.FileExists(finalPath) && !(existingFile != null && Path.Combine(movie.Path, existingFile.RelativePath).PathEquals(finalPath)))
                    {
                        LogWaiting(waitingKey, "'{0}' is ready, but a file already exists at its final path; it stays under its pending name", file.FinalPath);
                        return;
                    }

                    // Another download already replacing the same file, or headed for the same path, goes first.
                    if (existingFile != null && _replacementService.IsReserved(proxy, settings, downloadId, finalPath, existingFile))
                    {
                        LogWaiting(waitingKey, "'{0}' is ready, but another download is replacing the same file; it stays under its pending name", file.FinalPath);
                        return;
                    }

                    if (existingFile != null)
                    {
                        // The torrents to recheck are saved before any is changed, so a detachment cut short is still
                        // rechecked once the replacement is in.
                        file.ReplacesFileIds = new List<int> { existingFile.Id };
                        file.DetachedFrom = (file.DetachedFrom ?? new List<string>()).Union(_replacementService.FindHolders(downloadId, movie, existingFile)).ToList();
                        save();

                        _replacementService.Detach(proxy, settings, downloadId, movie, existingFile, finalPath);
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

                    // Already recorded (the import finished but its state wasn't saved): never import it again, which
                    // would treat the file as an upgrade of itself.
                    if (IsRecorded(movie, finalPath, file))
                    {
                        file.State = EngineLinkFileState.Imported;
                        file.ImportedAt ??= DateTime.UtcNow;
                        save();

                        RecheckDetached(proxy, settings, file, save);

                        return;
                    }

                    // Another file was recorded for the movie since this one was renamed: never replace it here.
                    if (HasOtherFile(movie, finalPath, file))
                    {
                        LogWaiting(waitingKey, "'{0}' can't be recorded: the movie already has another file", file.FinalPath);
                        return;
                    }

                    var decision = GetDecision(finalPath, file, movie, trackedDownload, link);

                    if (!decision.Approved)
                    {
                        // It already passed under its pending name; leave it for the next poll rather than force it in.
                        LogWaiting(waitingKey, "'{0}' can't be recorded yet: {1}", file.FinalPath, string.Join(", ", decision.Rejections.Select(r => r.Message)));
                        return;
                    }

                    var result = _importApprovedMovies.Import(new List<ImportDecision> { decision }, true, downloadItem, ImportMode.Move).FirstOrDefault();

                    if (result?.Result != ImportResultType.Imported)
                    {
                        _logger.Warn("Unable to record '{0}': {1}", file.FinalPath, string.Join(", ", result?.Errors ?? new List<string>()));
                        return;
                    }

                    _waiting.TryRemove(waitingKey, out _);
                    file.State = EngineLinkFileState.Imported;
                    file.ImportedAt ??= DateTime.UtcNow;
                    save();

                    RecheckDetached(proxy, settings, file, save);

                    return;
                }
            }
        }

        // Records a published subtitle as an extra file of the movie's file. False while the video's file isn't
        // recorded (the subtitle is then recorded on a later poll).
        private bool RecordSubtitle(Movie movie, EngineLink link, EngineLinkFile video, EngineLinkFile file, string finalPath)
        {
            var videoPath = video == null ? null : Path.Combine(link.SavePath, video.FinalPath);
            var movieFile = GetExistingFile(movie);

            if (videoPath == null || movieFile == null || !Path.Combine(movie.Path, movieFile.RelativePath).PathEquals(videoPath))
            {
                _logger.Debug("'{0}' is published, but its video isn't recorded; recording it later", file.FinalPath);
                return false;
            }

            var relativePath = movie.Path.GetRelativePath(finalPath);

            if (_subtitleFileService.FindByPath(movie.Id, relativePath) == null)
            {
                _subtitleFileService.Upsert(new MovieSubtitleFile
                {
                    MovieId = movie.Id,
                    MovieFileId = movieFile.Id,
                    RelativePath = relativePath,
                    Extension = Path.GetExtension(finalPath),
                    Language = file.LanguageTags == null ? EngineFileMapper.ParseSubtitleLanguage(EngineFileMapper.GetFileName(file.FinalPath)) : EngineFileMapper.NormalizeSubtitleLanguage(Language.FindById(file.LanguageId)),
                    LanguageTags = file.LanguageTags ?? EngineFileMapper.ParseSubtitleTags(EngineFileMapper.GetFileName(file.FinalPath)),
                    Title = file.SubtitleTitle
                });
            }

            return true;
        }

        // The file the replaced file's torrents held is gone: they stop offering it. Cleared only once done, so a
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

        private void ProcessSubtitle(IQBittorrentProxy proxy,
                                     QBittorrentSettings settings,
                                     string hash,
                                     EngineLink link,
                                     EngineLinkFile file,
                                     List<QBittorrentTorrentFile> engineFiles,
                                     Movie movie,
                                     Action save)
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
                // Recorded with its video's file, so renames and deletions in the library carry it along. Recording
                // is idempotent, so a restart before the state is saved records it once.
                if (!RecordSubtitle(movie, link, video, file, finalPath))
                {
                    return;
                }

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

            // A subtitle is published once its video is in the library, and only if it passes its own checks.
            if (video?.State != EngineLinkFileState.Imported || !IsComplete(engineFiles, file) || !_diskProvider.FileExists(pendingPath))
            {
                return;
            }

            // Another file already has its name (the subtitle never renames over it): it is discarded like a bad one,
            // so it doesn't hold the download back.
            var rejection = _diskProvider.FileExists(finalPath)
                ? "a file with its name is already there"
                : _subtitleValidator.Validate(pendingPath, file.FinalPath);

            if (rejection != null)
            {
                // Only this subtitle goes: a bad subtitle never fails or blocklists the release.
                _logger.Warn("Subtitle '{0}' was discarded: {1}", file.FinalPath, rejection);
                proxy.SetFilePriority(hash, new[] { file.Index }, 0, settings);
                DeletePending(pendingPath);
                file.State = EngineLinkFileState.Rejected;
                save();

                return;
            }

            proxy.RenameFile(hash, file.PendingPath, file.FinalPath, settings);
            file.State = EngineLinkFileState.Renamed;
            save();
        }

        // A rename the engine hasn't carried out (it failed, or was lost in a restart) is asked for again.
        private void RetryRename(IQBittorrentProxy proxy, QBittorrentSettings settings, string hash, EngineLinkFile file, List<QBittorrentTorrentFile> engineFiles, string pendingPath, string finalPath)
        {
            if (IsRenamedInEngine(engineFiles, file) || !_diskProvider.FileExists(pendingPath) || _diskProvider.FileExists(finalPath))
            {
                return;
            }

            _logger.Debug("'{0}' hasn't been renamed yet; asking the engine again", file.FinalPath);
            proxy.RenameFile(hash, file.PendingPath, file.FinalPath, settings);
        }

        private void RejectFile(IQBittorrentProxy proxy, QBittorrentSettings settings, string hash, EngineLinkFile file, string pendingPath, List<MovieHistory> grabs, StrongBox<bool> failedSinceGrab)
        {
            // The file was never published under its final name; stop downloading it and delete it.
            proxy.SetFilePriority(hash, new[] { file.Index }, 0, settings);
            DeletePending(pendingPath);
            file.State = EngineLinkFileState.Rejected;

            // Blocklist the release and search again, once per grab.
            if (!failedSinceGrab.Value)
            {
                failedSinceGrab.Value = true;
                _failedDownloadService.MarkMovieAsFailed(grabs.MaxBy(g => g.Date).Id, EngineFileImportService.RejectedMessage, "Cinnabarr");
            }
        }

        private void Complete(EngineLink link, Movie movie, List<MovieHistory> grabs, TrackedDownload trackedDownload)
        {
            link.Completed = true;

            // A file replaced by a newer one was imported first, so it counts.
            var imported = link.Files.Where(f => !f.IsSubtitle && f.State is EngineLinkFileState.Imported or EngineLinkFileState.Replaced).ToList();

            if (imported.Empty())
            {
                return;
            }

            var movieFile = GetExistingFile(movie);

            // Records the download as imported, like the folder import does.
            trackedDownload.State = TrackedDownloadState.Imported;
            _eventAggregator.PublishEvent(new MovieDownloadCompletedEvent(trackedDownload, movie, movieFile, new GrabbedReleaseInfo(grabs)));
        }

        private ImportDecision GetDecision(string path, EngineLinkFile file, Movie movie, TrackedDownload trackedDownload, EngineLink link)
        {
            var downloadItem = trackedDownload.DownloadItem;

            var localMovie = new LocalMovie
            {
                Movie = movie,
                Path = path,
                Size = _diskProvider.GetFileSize(path),

                // Parse the file's name in the torrent (it carries tags such as the release group or edition), or the
                // name it will have; never its hidden pending name.
                FileMovieInfo = (file.OriginalName == null ? null : MovieParser.ParseMoviePath(file.OriginalName)) ?? MovieParser.ParseMoviePath(file.FinalPath),
                DownloadClientMovieInfo = MovieParser.ParseMovieTitle(link.SourceTitle),
                DownloadItem = downloadItem,
                SceneSource = true,
                OtherVideoFiles = link.Files.Count(f => !f.IsSubtitle) > 1,

                // Already in its final folder: checks such as free space must not count it twice.
                InPlace = true
            };

            try
            {
                _aggregationService.Augment(localMovie, downloadItem);
            }
            catch (AugmentingFailedException)
            {
                return new ImportDecision(localMovie, new ImportRejection(ImportRejectionReason.UnableToParse, "Unable to parse file"));
            }

            // The info-hash link decides the movie, not the parsed name (roadmap §4.4 rule 1).
            localMovie.Movie = movie;
            localMovie.IndexerFlags = trackedDownload.RemoteMovie?.Release?.IndexerFlags ?? 0;
            _formatCalculator.UpdateMovieCustomFormats(localMovie);

            return _importDecisionMaker.GetDecision(localMovie, downloadItem);
        }

        // The movie's current file, if it has one.
        private MovieFile GetExistingFile(Movie movie)
        {
            return movie.MovieFileId > 0 ? movie.MovieFile ?? _movieFileService.GetMovie(movie.MovieFileId) : null;
        }

        // True when this file is what the movie records (a file it replaces at the same path doesn't count).
        private bool IsRecorded(Movie movie, string finalPath, EngineLinkFile file = null)
        {
            var existing = GetExistingFile(movie);

            return existing != null &&
                   Path.Combine(movie.Path, existing.RelativePath).PathEquals(finalPath) &&
                   file?.ReplacesFileIds?.Contains(existing.Id) != true;
        }

        // True when the movie has a file other than the one at this final path.
        private bool HasOtherFile(Movie movie, string finalPath, EngineLinkFile file)
        {
            var existing = GetExistingFile(movie);

            return existing != null &&
                   !Path.Combine(movie.Path, existing.RelativePath).PathEquals(finalPath) &&
                   file?.ReplacesFileIds?.Contains(existing.Id) != true;
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
