using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public interface ICompletedMovieDownloadService
    {
        void Check(TrackedDownload trackedDownload);
        void Import(TrackedDownload trackedDownload);
    }

    // The movie side of completed download handling: decides when a finished download may be imported and imports it.
    public class CompletedMovieDownloadService : ICompletedMovieDownloadService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IMovieHistoryService _historyService;
        private readonly IProvideImportItemService _provideImportItemService;
        private readonly IDownloadedMovieImportService _downloadedMovieImportService;
        private readonly IMovieFileService _movieFileService;
        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly IEngineLinkService _engineLinkService;
        private readonly Logger _logger;

        public CompletedMovieDownloadService(IEventAggregator eventAggregator,
                                             IMovieHistoryService historyService,
                                             IProvideImportItemService provideImportItemService,
                                             IDownloadedMovieImportService downloadedMovieImportService,
                                             IMovieFileService movieFileService,
                                             IEngineClientRegistry engineClientRegistry,
                                             IEngineLinkService engineLinkService,
                                             Logger logger)
        {
            _eventAggregator = eventAggregator;
            _historyService = historyService;
            _provideImportItemService = provideImportItemService;
            _downloadedMovieImportService = downloadedMovieImportService;
            _movieFileService = movieFileService;
            _engineClientRegistry = engineClientRegistry;
            _engineLinkService = engineLinkService;
            _logger = logger;
        }

        public void Check(TrackedDownload trackedDownload)
        {
            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                return;
            }

            SetImportItem(trackedDownload);

            // Only process tracked downloads that are still downloading or have been blocked for importing due to an issue with matching
            if (trackedDownload.State != TrackedDownloadState.Downloading && trackedDownload.State != TrackedDownloadState.ImportBlocked)
            {
                return;
            }

            if (!ValidatePath(trackedDownload))
            {
                return;
            }

            // The movie comes from the grab (by info-hash for the built-in engine), never from the download's title.
            trackedDownload.State = TrackedDownloadState.ImportPending;
        }

        public void Import(TrackedDownload trackedDownload)
        {
            // Downloaded straight into the library: EngineMovieFileImportService imports it (roadmap §4.8).
            if (_engineClientRegistry.IsEngineClient(trackedDownload.DownloadClient) &&
                _engineLinkService.Get(trackedDownload.DownloadItem.DownloadId)?.Files != null)
            {
                return;
            }

            SetImportItem(trackedDownload);

            if (!ValidatePath(trackedDownload))
            {
                return;
            }

            if (trackedDownload.RemoteMovie?.Movie == null)
            {
                trackedDownload.Warn("Unable to find the movie for this download, automatic import is not possible.");
                SetStateToImportBlocked(trackedDownload);

                return;
            }

            trackedDownload.State = TrackedDownloadState.Importing;

            var outputPath = trackedDownload.ImportItem.OutputPath.FullPath;
            var importResults = _downloadedMovieImportService.ProcessPath(outputPath, ImportMode.Auto, trackedDownload.RemoteMovie.Movie, trackedDownload.ImportItem);

            if (VerifyImport(trackedDownload, importResults))
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;

            if (importResults.Empty())
            {
                trackedDownload.Warn("No files found are eligible for import in {0}", outputPath);

                return;
            }

            var statusMessages = new List<TrackedDownloadStatusMessage>
            {
                new TrackedDownloadStatusMessage("The movie in this release was not imported or is missing from the release", new List<string>())
            };

            statusMessages.AddRange(
                importResults
                    .Where(v => v.Result != ImportResultType.Imported && v.ImportDecision.LocalMovie != null)
                    .OrderBy(v => v.ImportDecision.LocalMovie.Path)
                    .Select(v => new TrackedDownloadStatusMessage(Path.GetFileName(v.ImportDecision.LocalMovie.Path), v.Errors)));

            trackedDownload.Warn(statusMessages.ToArray());
            SetStateToImportBlocked(trackedDownload);
        }

        private bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            var movie = trackedDownload.RemoteMovie.Movie;

            var historyItems = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)
                .OrderByDescending(h => h.Date)
                .ToList();

            var grabbedHistory = historyItems.Where(h => h.EventType == MovieHistoryEventType.Grabbed).ToList();
            var releaseInfo = grabbedHistory.Count > 0 ? new GrabbedReleaseInfo(grabbedHistory) : null;

            var imported = importResults.Any(c => c.Result == ImportResultType.Imported);

            // The decision engine rejects a file that is already imported, so a download whose file was imported before
            // is done as well.
            var importedBefore = historyItems.Any(h => h.EventType == MovieHistoryEventType.DownloadFolderImported);

            if (!imported && !importedBefore)
            {
                _logger.Debug("The movie has not been imported for the release '{0}'", trackedDownload.DownloadItem.Title);

                return false;
            }

            _logger.Debug("The movie was imported for {0}", trackedDownload.DownloadItem.Title);

            var movieFile = movie.MovieFileId > 0 ? _movieFileService.GetMovie(movie.MovieFileId) : null;

            trackedDownload.State = TrackedDownloadState.Imported;
            _eventAggregator.PublishEvent(new MovieDownloadCompletedEvent(trackedDownload, movie, movieFile, releaseInfo));

            return true;
        }

        private void SetStateToImportBlocked(TrackedDownload trackedDownload)
        {
            trackedDownload.State = TrackedDownloadState.ImportBlocked;

            if (!trackedDownload.HasNotifiedManualInteractionRequired)
            {
                _logger.Warn("Import blocked for '{0}': {1}", trackedDownload.DownloadItem.Title, trackedDownload.StatusMessages.SelectMany(m => m.Messages).ConcatToString("; "));

                trackedDownload.HasNotifiedManualInteractionRequired = true;
            }
        }

        private void SetImportItem(TrackedDownload trackedDownload)
        {
            trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
        }

        private static bool ValidatePath(TrackedDownload trackedDownload)
        {
            var outputPath = trackedDownload.ImportItem.OutputPath;

            if (outputPath.IsEmpty)
            {
                trackedDownload.Warn("Download doesn't contain intermediate path, Skipping.");
                return false;
            }

            if ((OsInfo.IsWindows && !outputPath.IsWindowsPath) || (OsInfo.IsNotWindows && !outputPath.IsUnixPath))
            {
                trackedDownload.Warn("[{0}] is not a valid local path.", outputPath);
                return false;
            }

            return true;
        }
    }
}
