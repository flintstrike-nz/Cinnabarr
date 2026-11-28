using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.EpisodeImport.Manual;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;
using MovieImportDecision = NzbDrone.Core.MediaFiles.MovieImport.ImportDecision;

namespace NzbDrone.Core.Reorganise
{
    public class ReorganisePreviewItem
    {
        public string Kind { get; set; } = ReorganiseKinds.Rename;
        public string MediaType { get; set; }
        public int MediaId { get; set; }
        public string Title { get; set; }
        public int FileId { get; set; }
        public string From { get; set; }
        public string To { get; set; }

        // Why the item won't be done, in words for the user; null when it will be.
        public string Problem { get; set; }

        // What a loose file is imported with (only one of them is set, and only when it can be imported).
        public ManualImportItem EpisodeImport { get; set; }
        public MovieImportDecision MovieImport { get; set; }
    }

    public class ReorganiseRun
    {
        public string RunId { get; set; }
        public DateTime Applied { get; set; }
        public int Files { get; set; }
        public int Undone { get; set; }
    }

    public interface IReorganiseService
    {
        // Everything a run would do in a folder (a root folder, any folder inside one, or every root folder when none
        // is given): the library files whose names don't follow the naming format, the loose files that can go into a
        // series or movie, and, when asked for, the series and movie folders whose names don't follow it. Each item
        // that won't be done says why.
        List<ReorganisePreviewItem> Preview(string folder = null, bool includeFolders = false);

        List<ReorganiseRun> GetRuns();
        List<ReorganiseJournalEntry> GetRun(string runId);
    }

    public class ReorganiseLibraryCommand : Command
    {
        // A root folder or a folder inside one; every root folder when it's empty.
        public string Folder { get; set; }

        // Rename series and movie folders too.
        public bool IncludeFolders { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsLongRunning => true;
        public override string CompletionMessage => "Reorganise finished";
    }

    public class ReorganiseUndoCommand : Command
    {
        public string RunId { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override string CompletionMessage => "Undo finished";
    }

    /// <summary>
    /// Roadmap §4.12 "Library reorganisation": shows which files don't follow the naming format, renames them with the same
    /// code the Organize buttons use, journals each rename, and can undo a whole run. Video files are journaled; subtitles
    /// and other extras follow their video on the way there and are picked up again by the next refresh on the way back.
    /// Loose files (ones the library doesn't hold yet) are moved into their series or movie; those moves aren't undone.
    /// </summary>
    public class ReorganiseService : IReorganiseService,
                                     IExecute<ReorganiseLibraryCommand>,
                                     IExecute<ReorganiseUndoCommand>,
                                     IHandle<MovieFileRenamedEvent>,
                                     IHandle<EpisodeFileRenamedEvent>
    {
        // The run in progress on this thread (event handlers run on the thread that publishes the event).
        private static readonly AsyncLocal<string> CurrentRun = new();

        private readonly IMovieService _movieService;
        private readonly IMovieFileService _movieFileService;
        private readonly IRenameMovieFileService _renameMovieFileService;
        private readonly IExecute<RenameMovieFilesCommand> _renameMovieFiles;
        private readonly IExecute<MoveMovieCommand> _moveMovie;
        private readonly IBuildMovieFolderName _movieFolderNameBuilder;
        private readonly ISeriesService _seriesService;
        private readonly IMediaFileService _episodeFileService;
        private readonly IRenameEpisodeFileService _renameEpisodeFileService;
        private readonly IExecute<RenameFilesCommand> _renameEpisodeFiles;
        private readonly IExecute<MoveSeriesCommand> _moveSeries;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IRootFolderService _rootFolderService;
        private readonly ILooseMediaService _looseMediaService;
        private readonly IReorganiseJournalRepository _journal;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public ReorganiseService(IMovieService movieService,
                                 IMovieFileService movieFileService,
                                 IRenameMovieFileService renameMovieFileService,
                                 IExecute<RenameMovieFilesCommand> renameMovieFiles,
                                 IExecute<MoveMovieCommand> moveMovie,
                                 IBuildMovieFolderName movieFolderNameBuilder,
                                 ISeriesService seriesService,
                                 IMediaFileService episodeFileService,
                                 IRenameEpisodeFileService renameEpisodeFileService,
                                 IExecute<RenameFilesCommand> renameEpisodeFiles,
                                 IExecute<MoveSeriesCommand> moveSeries,
                                 IBuildFileNames fileNameBuilder,
                                 IRootFolderService rootFolderService,
                                 ILooseMediaService looseMediaService,
                                 IReorganiseJournalRepository journal,
                                 IDiskProvider diskProvider,
                                 Logger logger)
        {
            _movieService = movieService;
            _movieFileService = movieFileService;
            _renameMovieFileService = renameMovieFileService;
            _renameMovieFiles = renameMovieFiles;
            _moveMovie = moveMovie;
            _movieFolderNameBuilder = movieFolderNameBuilder;
            _seriesService = seriesService;
            _episodeFileService = episodeFileService;
            _renameEpisodeFileService = renameEpisodeFileService;
            _renameEpisodeFiles = renameEpisodeFiles;
            _moveSeries = moveSeries;
            _fileNameBuilder = fileNameBuilder;
            _rootFolderService = rootFolderService;
            _looseMediaService = looseMediaService;
            _journal = journal;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public List<ReorganisePreviewItem> Preview(string folder = null, bool includeFolders = false)
        {
            return BuildPreview(folder, includeFolders, true);
        }

        private List<ReorganisePreviewItem> BuildPreview(string folder, bool includeFolders, bool includeLoose)
        {
            var items = new List<ReorganisePreviewItem>();
            var reachable = new Dictionary<string, bool>();

            var movies = _movieService.GetAllMovies().Where(m => Overlaps(m.Path, folder)).ToDictionary(m => m.Id);
            var series = _seriesService.GetAllSeries().Where(s => Overlaps(s.Path, folder)).ToDictionary(s => s.Id);

            if (includeFolders)
            {
                items.AddRange(movies.Values.Select(MovieFolderItem).Where(i => i != null));
                items.AddRange(series.Values.Select(SeriesFolderItem).Where(i => i != null));
            }

            if (movies.Any())
            {
                items.AddRange(_renameMovieFileService.GetRenamePreviews(movies.Keys.ToList()).Select(p => new ReorganisePreviewItem
                {
                    MediaType = ReorganiseMediaTypes.Movie,
                    MediaId = p.MovieId,
                    Title = movies[p.MovieId].Title,
                    FileId = p.MovieFileId,
                    From = Path.Combine(movies[p.MovieId].Path, p.ExistingPath),
                    To = Path.Combine(movies[p.MovieId].Path, p.NewPath),
                    Problem = IsReachable(movies[p.MovieId].Path, reachable) ? null : Unreachable(movies[p.MovieId].Path)
                }));
            }

            if (series.Any())
            {
                items.AddRange(_renameEpisodeFileService.GetRenamePreviews(series.Keys.ToList()).Select(p => new ReorganisePreviewItem
                {
                    MediaType = ReorganiseMediaTypes.Episode,
                    MediaId = p.SeriesId,
                    Title = series[p.SeriesId].Title,
                    FileId = p.EpisodeFileId,
                    From = Path.Combine(series[p.SeriesId].Path, p.ExistingPath),
                    To = Path.Combine(series[p.SeriesId].Path, p.NewPath),
                    Problem = IsReachable(series[p.SeriesId].Path, reachable) ? null : Unreachable(series[p.SeriesId].Path)
                }));
            }

            // A folder inside a series or movie only holds some of its files.
            items.RemoveAll(i => i.Kind == ReorganiseKinds.Rename && folder.IsNotNullOrWhiteSpace() && !folder.PathEquals(i.From) && !folder.IsParentPath(i.From));

            foreach (var item in items.Where(i => i.Kind == ReorganiseKinds.Rename && i.Problem == null))
            {
                item.Problem = RenameProblem(item);
            }

            if (includeLoose)
            {
                items.AddRange(_looseMediaService.Find(folder));
            }

            // A name another file has (or will have, earlier in the run) gets a number, as the move gives it: nothing is
            // skipped or written over, and Doctor flags the worse of the copies afterwards.
            var taken = new HashSet<string>(PathEqualityComparer.Instance);

            foreach (var item in items.Where(i => i.Problem == null && i.To != null && i.Kind != ReorganiseKinds.Folder))
            {
                item.To = FreeFilePath.Find(_diskProvider, item.To, item.From, taken);
                taken.Add(item.To);
            }

            return items;
        }

        public List<ReorganiseRun> GetRuns()
        {
            return _journal.All()
                .GroupBy(e => e.RunId)
                .Select(g => new ReorganiseRun
                {
                    RunId = g.Key,
                    Applied = g.Min(e => e.Applied),
                    Files = g.Count(),
                    Undone = g.Count(e => e.Undone != null)
                })
                .OrderByDescending(r => r.Applied)
                .ToList();
        }

        public List<ReorganiseJournalEntry> GetRun(string runId)
        {
            return _journal.GetByRun(runId);
        }

        public void Execute(ReorganiseLibraryCommand message)
        {
            var runId = Guid.NewGuid().ToString("N");
            var items = Preview(message.Folder, message.IncludeFolders);
            var skipped = items.Where(i => i.Problem != null).ToList();

            _logger.ProgressInfo("Reorganising {0} item(s); {1} can't be done", items.Count - skipped.Count, skipped.Count);

            foreach (var item in skipped)
            {
                _logger.Info("Not reorganised: '{0}': {1}", item.From, item.Problem);
            }

            CurrentRun.Value = runId;

            try
            {
                var folders = items.Where(i => i.Kind == ReorganiseKinds.Folder && i.Problem == null).ToList();

                foreach (var item in folders)
                {
                    TryRun(item.Title, () => MoveFolder(item, runId));
                }

                // Moving a folder changes where its files are, so the renames are worked out again after.
                var renames = folders.Any()
                    ? BuildPreview(message.Folder, false, false).Where(i => i.Kind == ReorganiseKinds.Rename && i.Problem == null).ToList()
                    : items.Where(i => i.Kind == ReorganiseKinds.Rename && i.Problem == null).ToList();

                foreach (var movie in renames.Where(i => i.MediaType == ReorganiseMediaTypes.Movie).GroupBy(i => i.MediaId))
                {
                    TryRun(movie.First().Title, () => _renameMovieFiles.Execute(new RenameMovieFilesCommand(movie.Key, movie.Select(i => i.FileId).ToList())));
                }

                foreach (var series in renames.Where(i => i.MediaType == ReorganiseMediaTypes.Episode).GroupBy(i => i.MediaId))
                {
                    TryRun(series.First().Title, () => _renameEpisodeFiles.Execute(new RenameFilesCommand(series.Key, series.Select(i => i.FileId).ToList())));
                }

                var imported = _looseMediaService.Import(items.Where(i => i.Kind == ReorganiseKinds.Import && i.Problem == null).ToList());

                var journaled = _journal.GetByRun(runId);
                var renamed = journaled.Count(e => e.FileId != 0);
                var moved = journaled.Count(e => e.FileId == 0);
                var notDone = renames.Count - renamed;

                _logger.ProgressInfo("Reorganise finished: {0} renamed, {1} folder(s) moved, {2} loose file(s) moved in, {3} skipped{4} (run {5})",
                    renamed,
                    moved,
                    imported,
                    skipped.Count,
                    notDone > 0 ? $", {notDone} failed (see the log)" : string.Empty,
                    runId);
            }
            finally
            {
                CurrentRun.Value = null;
            }
        }

        public void Execute(ReorganiseUndoCommand message)
        {
            var entries = _journal.GetByRun(message.RunId).Where(e => e.Undone == null).OrderByDescending(e => e.Id).ToList();

            _logger.ProgressInfo("Undoing {0} rename(s) of run {1}", entries.Count, message.RunId);

            foreach (var entry in entries)
            {
                try
                {
                    if (Undo(entry))
                    {
                        entry.Undone = DateTime.UtcNow;
                        _journal.Update(entry);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to undo the rename of '{0}'", entry.ToPath);
                }
            }
        }

        public void Handle(MovieFileRenamedEvent message)
        {
            Record(ReorganiseMediaTypes.Movie, message.Movie.Id, message.MovieFile.Id, message.Movie.Title, message.OriginalPath, Path.Combine(message.Movie.Path, message.MovieFile.RelativePath));
        }

        public void Handle(EpisodeFileRenamedEvent message)
        {
            Record(ReorganiseMediaTypes.Episode, message.Series.Id, message.EpisodeFile.Id, message.Series.Title, message.OriginalPath, Path.Combine(message.Series.Path, message.EpisodeFile.RelativePath));
        }

        private void Record(string mediaType, int mediaId, int fileId, string title, string from, string to)
        {
            var runId = CurrentRun.Value;

            if (runId == null || from == to)
            {
                return;
            }

            _journal.Insert(new ReorganiseJournalEntry
            {
                RunId = runId,
                MediaType = mediaType,
                MediaId = mediaId,
                FileId = fileId,
                Title = title,
                FromPath = from,
                ToPath = to,
                Applied = DateTime.UtcNow
            });
        }

        private void TryRun(string title, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to rename the files of '{0}'", title);
            }
        }

        // Media whose folder is in the folder asked for, or holds it.
        private static bool Overlaps(string path, string folder)
        {
            return folder.IsNullOrWhiteSpace() || folder.PathEquals(path) || folder.IsParentPath(path) || path.IsParentPath(folder);
        }

        private bool IsReachable(string path, Dictionary<string, bool> reachable)
        {
            if (!reachable.TryGetValue(path, out var isReachable))
            {
                isReachable = _diskProvider.FolderExists(path);
                reachable[path] = isReachable;
            }

            return isReachable;
        }

        private static string Unreachable(string path)
        {
            return $"Its folder {path} can't be reached. Is its drive connected?";
        }

        private string RenameProblem(ReorganisePreviewItem item)
        {
            if (!_diskProvider.FileExists(item.From))
            {
                return "The file isn't there any more. Refresh the series or movie to update the library";
            }

            return null;
        }

        private ReorganisePreviewItem SeriesFolderItem(Series series)
        {
            var rootFolder = _rootFolderService.GetBestRootFolderPath(series.Path);

            if (rootFolder.IsNullOrWhiteSpace())
            {
                return null;
            }

            var target = Path.Combine(rootFolder, _fileNameBuilder.GetSeriesFolder(series));

            return FolderItem(ReorganiseMediaTypes.SeriesFolder, series.Id, series.Title, series.Path, target);
        }

        private ReorganisePreviewItem MovieFolderItem(Movie movie)
        {
            var rootFolder = _rootFolderService.GetBestRootFolderPath(movie.Path);

            if (rootFolder.IsNullOrWhiteSpace())
            {
                return null;
            }

            var target = Path.Combine(rootFolder, _movieFolderNameBuilder.GetMovieFolder(movie));

            return FolderItem(ReorganiseMediaTypes.MovieFolder, movie.Id, movie.Title, movie.Path, target);
        }

        private ReorganisePreviewItem FolderItem(string mediaType, int mediaId, string title, string from, string to)
        {
            // Folders differing only in case are left alone: drives that ignore case can't tell them apart.
            if (from.PathEquals(to, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var item = new ReorganisePreviewItem
            {
                Kind = ReorganiseKinds.Folder,
                MediaType = mediaType,
                MediaId = mediaId,
                Title = title,
                From = from,
                To = to
            };

            if (!_diskProvider.FolderExists(from))
            {
                item.Problem = Unreachable(from);
            }
            else if (_diskProvider.FolderExists(to))
            {
                item.Problem = $"A folder named {Path.GetFileName(to)} is already there. Merge it into the series or movie first";
            }

            return item;
        }

        private void MoveFolder(ReorganisePreviewItem item, string runId)
        {
            if (item.MediaType == ReorganiseMediaTypes.SeriesFolder)
            {
                _moveSeries.Execute(new MoveSeriesCommand { SeriesId = item.MediaId, SourcePath = item.From, DestinationPath = item.To });

                if (!_seriesService.GetSeries(item.MediaId).Path.PathEquals(item.To))
                {
                    return;
                }
            }
            else
            {
                // The movie holds its new path before its folder is moved; the move puts the old one back if it fails.
                var movie = _movieService.GetMovie(item.MediaId);
                movie.Path = item.To;
                _movieService.UpdateMovie(movie);

                _moveMovie.Execute(new MoveMovieCommand { MovieId = item.MediaId, SourcePath = item.From, DestinationPath = item.To });

                if (!_movieService.GetMovie(item.MediaId).Path.PathEquals(item.To) || !_diskProvider.FolderExists(item.To))
                {
                    return;
                }
            }

            _journal.Insert(new ReorganiseJournalEntry
            {
                RunId = runId,
                MediaType = item.MediaType,
                MediaId = item.MediaId,
                Title = item.Title,
                FromPath = item.From,
                ToPath = item.To,
                Applied = DateTime.UtcNow
            });
        }

        // Puts a renamed video file back under its old name. Not done (and left in the journal as it was) when the file
        // is no longer where the run put it, or something else is at the old name now.
        private bool Undo(ReorganiseJournalEntry entry)
        {
            if (entry.MediaType is ReorganiseMediaTypes.SeriesFolder or ReorganiseMediaTypes.MovieFolder)
            {
                return UndoFolder(entry);
            }

            if (!_diskProvider.FileExists(entry.ToPath))
            {
                _logger.Warn("'{0}' is no longer there; its rename isn't undone", entry.ToPath);

                return false;
            }

            if (_diskProvider.FileExists(entry.FromPath))
            {
                _logger.Warn("'{0}' exists again; the rename to '{1}' isn't undone", entry.FromPath, entry.ToPath);

                return false;
            }

            if (entry.MediaType == ReorganiseMediaTypes.Movie)
            {
                var movie = _movieService.GetMovie(entry.MediaId);
                var file = _movieFileService.GetMovie(entry.FileId);

                if (file == null || Path.Combine(movie.Path, file.RelativePath) != entry.ToPath)
                {
                    _logger.Warn("The library no longer has '{0}' as it was renamed; it isn't undone", entry.ToPath);

                    return false;
                }

                Move(entry);
                file.RelativePath = movie.Path.GetRelativePath(entry.FromPath);
                _movieFileService.Update(file);
            }
            else
            {
                var series = _seriesService.GetSeries(entry.MediaId);
                var file = _episodeFileService.Get(entry.FileId);

                if (file == null || Path.Combine(series.Path, file.RelativePath) != entry.ToPath)
                {
                    _logger.Warn("The library no longer has '{0}' as it was renamed; it isn't undone", entry.ToPath);

                    return false;
                }

                Move(entry);
                file.RelativePath = series.Path.GetRelativePath(entry.FromPath);
                _episodeFileService.Update(file);
            }

            return true;
        }

        // Moves a series' or movie's folder back, with the same move the run used.
        private bool UndoFolder(ReorganiseJournalEntry entry)
        {
            if (!_diskProvider.FolderExists(entry.ToPath))
            {
                _logger.Warn("'{0}' is no longer there; its rename isn't undone", entry.ToPath);

                return false;
            }

            if (_diskProvider.FolderExists(entry.FromPath))
            {
                _logger.Warn("'{0}' exists again; the rename to '{1}' isn't undone", entry.FromPath, entry.ToPath);

                return false;
            }

            if (entry.MediaType == ReorganiseMediaTypes.SeriesFolder)
            {
                if (!_seriesService.GetSeries(entry.MediaId).Path.PathEquals(entry.ToPath))
                {
                    _logger.Warn("{0} is no longer in '{1}'; it isn't moved back", entry.Title, entry.ToPath);

                    return false;
                }

                _moveSeries.Execute(new MoveSeriesCommand { SeriesId = entry.MediaId, SourcePath = entry.ToPath, DestinationPath = entry.FromPath });

                return _seriesService.GetSeries(entry.MediaId).Path.PathEquals(entry.FromPath);
            }

            var movie = _movieService.GetMovie(entry.MediaId);

            if (!movie.Path.PathEquals(entry.ToPath))
            {
                _logger.Warn("{0} is no longer in '{1}'; it isn't moved back", entry.Title, entry.ToPath);

                return false;
            }

            movie.Path = entry.FromPath;
            _movieService.UpdateMovie(movie);
            _moveMovie.Execute(new MoveMovieCommand { MovieId = entry.MediaId, SourcePath = entry.ToPath, DestinationPath = entry.FromPath });

            return _diskProvider.FolderExists(entry.FromPath);
        }

        private void Move(ReorganiseJournalEntry entry)
        {
            _diskProvider.EnsureFolder(Path.GetDirectoryName(entry.FromPath));
            _diskProvider.MoveFile(entry.ToPath, entry.FromPath);
        }
    }
}
