using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Extras.MovieExtras;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Operations;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.MovieImport
{
    public interface IImportApprovedMovie
    {
        List<ImportResult> Import(List<ImportDecision> decisions, bool newDownload, DownloadClientItem downloadClientItem = null, ImportMode importMode = ImportMode.Auto);
    }

    public class ImportApprovedMovie : IImportApprovedMovie
    {
        private readonly IUpgradeMovieFiles _movieFileUpgrader;
        private readonly IMovieFileService _mediaFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly IMovieHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IMovieExtraService _extraService;
        private readonly IFileOperationTracker _fileOperations;
        private readonly Logger _logger;

        public ImportApprovedMovie(IUpgradeMovieFiles movieFileUpgrader,
                                   IMovieFileService mediaFileService,
                                   IDiskProvider diskProvider,
                                   IMovieHistoryService historyService,
                                   IEventAggregator eventAggregator,
                                   IMovieExtraService extraService,
                                   IFileOperationTracker fileOperations,
                                   Logger logger)
        {
            _movieFileUpgrader = movieFileUpgrader;
            _mediaFileService = mediaFileService;
            _diskProvider = diskProvider;
            _historyService = historyService;
            _eventAggregator = eventAggregator;
            _extraService = extraService;
            _fileOperations = fileOperations;
            _logger = logger;
        }

        public List<ImportResult> Import(List<ImportDecision> decisions, bool newDownload, DownloadClientItem downloadClientItem = null, ImportMode importMode = ImportMode.Auto)
        {
            _logger.Debug("Decisions: {0}", decisions.Count);

            // I added a null op for the rare case that the quality is null. TODO: find out why that would even happen in the first place.
            var qualifiedImports = decisions
                .Where(decision => decision.Approved)
                .GroupBy(decision => decision.LocalMovie.Movie.Id)
                .SelectMany(group => group
                    .OrderByDescending(decision => decision.LocalMovie.Quality ?? new QualityModel { Quality = Quality.Unknown }, new QualityModelComparer(group.First().LocalMovie.Movie.QualityProfile))
                    .ThenByDescending(decision => decision.LocalMovie.Size))
                .ToList();

            var importResults = new List<ImportResult>();
            var ordered = qualifiedImports.OrderByDescending(e => e.LocalMovie.Size).ToList();

            // The operations queue shows every file to import, and its move into the movie, before any of them runs.
            var operations = ordered.ToDictionary(d => d, d => QueueOperations(d.LocalMovie, newDownload, importMode, downloadClientItem));

            foreach (var importDecision in ordered)
            {
                var localMovie = importDecision.LocalMovie;
                var oldFiles = new List<DeletedMovieFile>();

                _fileOperations.Start(operations[importDecision]);

                try
                {
                    // check if already imported
                    if (importResults.Select(r => r.ImportDecision.LocalMovie.Movie)
                                         .Select(m => (m.Id, m.VersionSlotId)).Contains((localMovie.Movie.Id, localMovie.Movie.VersionSlotId)))
                    {
                        importResults.Add(new ImportResult(importDecision, "Movie has already been imported"));
                        continue;
                    }

                    var movieFile = localMovie.ToMovieFile();
                    movieFile.Size = _diskProvider.GetFileSize(localMovie.Path);

                    if (downloadClientItem?.DownloadId.IsNotNullOrWhiteSpace() == true)
                    {
                        var grabHistory = _historyService.FindByDownloadId(downloadClientItem.DownloadId)
                            .OrderByDescending(h => h.Date)
                            .FirstOrDefault(h => h.EventType == MovieHistoryEventType.Grabbed);

                        if (Enum.TryParse(grabHistory?.Data.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
                        {
                            movieFile.IndexerFlags = flags;
                        }
                    }
                    else
                    {
                        movieFile.IndexerFlags = localMovie.IndexerFlags;
                    }

                    bool copyOnly;
                    switch (importMode)
                    {
                        default:
                        case ImportMode.Auto:
                            copyOnly = downloadClientItem is { CanMoveFiles: false };
                            break;
                        case ImportMode.Move:
                            copyOnly = false;
                            break;
                        case ImportMode.Copy:
                            copyOnly = true;
                            break;
                    }

                    if (newDownload)
                    {
                        if (downloadClientItem is { OutputPath.IsEmpty: false })
                        {
                            var outputDirectory = downloadClientItem.OutputPath.Directory.ToString();
                            if (outputDirectory.IsParentPath(localMovie.Path))
                            {
                                movieFile.OriginalFilePath = outputDirectory.GetRelativePath(localMovie.Path);
                            }
                        }

                        oldFiles = _movieFileUpgrader.UpgradeMovieFile(movieFile, localMovie, copyOnly).OldFiles;
                    }
                    else
                    {
                        // Delete existing files from the DB mapped to this path
                        var previousFiles = _mediaFileService.GetFilesWithRelativePath(localMovie.Movie.Id, movieFile.RelativePath);

                        foreach (var previousFile in previousFiles)
                        {
                            _mediaFileService.Delete(previousFile, DeleteMediaFileReason.ManualOverride);
                        }
                    }

                    movieFile = _mediaFileService.Add(movieFile);
                    importResults.Add(new ImportResult(importDecision));

                    localMovie.Movie.MovieFile = movieFile;

                    // An in-place file's folder is the library folder; Cinnabarr handles its subtitles itself.
                    if (!localMovie.InPlace && (!localMovie.ScriptImported || localMovie.ShouldImportExtras))
                    {
                        _extraService.ImportMovie(localMovie, movieFile, copyOnly);
                    }

                    _eventAggregator.PublishEvent(new MovieFileImportedEvent(localMovie, movieFile, oldFiles, newDownload, downloadClientItem));
                }
                catch (RootFolderNotFoundException e)
                {
                    _logger.Warn(e, "Couldn't import movie " + localMovie);
                    _eventAggregator.PublishEvent(new MovieImportFailedEvent(e, localMovie, newDownload, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, "Failed to import movie, Root folder missing."));
                }
                catch (DestinationAlreadyExistsException e)
                {
                    _logger.Warn(e, "Couldn't import movie " + localMovie);
                    importResults.Add(new ImportResult(importDecision, "Failed to import movie, Destination already exists."));
                }
                catch (RecycleBinException e)
                {
                    _logger.Warn(e, "Couldn't import movie " + localMovie);
                    _eventAggregator.PublishEvent(new MovieImportFailedEvent(e, localMovie, newDownload, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, "Failed to import movie, unable to move existing file to the Recycle Bin."));
                }
                catch (UnauthorizedAccessException e)
                {
                    _logger.Warn(e, "Couldn't import movie " + localMovie);
                    _eventAggregator.PublishEvent(new MovieImportFailedEvent(e, localMovie, newDownload, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, "Failed to import movie due to permission issues.", e.Message));
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't import movie " + localMovie);
                    importResults.Add(new ImportResult(importDecision, "Failed to import movie", e.Message));
                }
                finally
                {
                    FinishOperations(operations[importDecision], importResults.LastOrDefault(r => r.ImportDecision == importDecision));
                }
            }

            // Adding all the rejected decisions
            importResults.AddRange(decisions.Where(c => !c.Approved)
                                            .Select(d => new ImportResult(d, d.Rejections.Select(r => r.Message).ToArray())));

            return importResults;
        }

        private FileOperation QueueOperations(LocalMovie localMovie, bool newDownload, ImportMode importMode, DownloadClientItem downloadClientItem)
        {
            var title = FileOperationTitles.Movie(localMovie.Movie);
            var operation = _fileOperations.Queue(FileOperationKinds.Import, title, localMovie.Path, localMovie.Movie.Path);

            // Only a new file is moved (or copied) into the movie's folder; one already there is just recorded.
            if (newDownload && !localMovie.InPlace)
            {
                var copy = importMode == ImportMode.Copy || (importMode == ImportMode.Auto && downloadClientItem is { CanMoveFiles: false });

                _fileOperations.Queue(copy ? FileOperationKinds.Copy : FileOperationKinds.Move, title, localMovie.Path);
            }

            return operation;
        }

        private void FinishOperations(FileOperation operation, ImportResult result)
        {
            if (operation == null)
            {
                return;
            }

            if (result == null || result.Result == ImportResultType.Imported)
            {
                _fileOperations.Complete(operation);
                _fileOperations.SkipQueued(operation.Path, "Nothing to move");

                return;
            }

            var reason = string.Join("; ", result.Errors);

            if (result.Result == ImportResultType.Skipped)
            {
                _fileOperations.Skip(operation, reason);
            }
            else
            {
                _fileOperations.Fail(operation, reason);
            }

            _fileOperations.SkipQueued(operation.Path, $"Not done: {reason}");
        }
    }
}
