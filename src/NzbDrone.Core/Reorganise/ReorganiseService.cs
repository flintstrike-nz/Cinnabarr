using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Reorganise
{
    public class ReorganisePreviewItem
    {
        public string MediaType { get; set; }
        public int MediaId { get; set; }
        public string Title { get; set; }
        public int FileId { get; set; }
        public string From { get; set; }
        public string To { get; set; }
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
        // Every file whose name differs from what the naming format gives now.
        List<ReorganisePreviewItem> Preview();

        List<ReorganiseRun> GetRuns();
        List<ReorganiseJournalEntry> GetRun(string runId);
    }

    public class ReorganiseLibraryCommand : Command
    {
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
        private readonly ISeriesService _seriesService;
        private readonly IMediaFileService _episodeFileService;
        private readonly IRenameEpisodeFileService _renameEpisodeFileService;
        private readonly IExecute<RenameFilesCommand> _renameEpisodeFiles;
        private readonly IReorganiseJournalRepository _journal;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public ReorganiseService(IMovieService movieService,
                                 IMovieFileService movieFileService,
                                 IRenameMovieFileService renameMovieFileService,
                                 IExecute<RenameMovieFilesCommand> renameMovieFiles,
                                 ISeriesService seriesService,
                                 IMediaFileService episodeFileService,
                                 IRenameEpisodeFileService renameEpisodeFileService,
                                 IExecute<RenameFilesCommand> renameEpisodeFiles,
                                 IReorganiseJournalRepository journal,
                                 IDiskProvider diskProvider,
                                 Logger logger)
        {
            _movieService = movieService;
            _movieFileService = movieFileService;
            _renameMovieFileService = renameMovieFileService;
            _renameMovieFiles = renameMovieFiles;
            _seriesService = seriesService;
            _episodeFileService = episodeFileService;
            _renameEpisodeFileService = renameEpisodeFileService;
            _renameEpisodeFiles = renameEpisodeFiles;
            _journal = journal;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public List<ReorganisePreviewItem> Preview()
        {
            var items = new List<ReorganisePreviewItem>();

            var movies = _movieService.GetAllMovies().ToDictionary(m => m.Id);

            if (movies.Any())
            {
                items.AddRange(_renameMovieFileService.GetRenamePreviews(movies.Keys.ToList()).Select(p => new ReorganisePreviewItem
                {
                    MediaType = ReorganiseMediaTypes.Movie,
                    MediaId = p.MovieId,
                    Title = movies[p.MovieId].Title,
                    FileId = p.MovieFileId,
                    From = p.ExistingPath,
                    To = p.NewPath
                }));
            }

            var series = _seriesService.GetAllSeries().ToDictionary(s => s.Id);

            if (series.Any())
            {
                items.AddRange(_renameEpisodeFileService.GetRenamePreviews(series.Keys.ToList()).Select(p => new ReorganisePreviewItem
                {
                    MediaType = ReorganiseMediaTypes.Episode,
                    MediaId = p.SeriesId,
                    Title = series[p.SeriesId].Title,
                    FileId = p.EpisodeFileId,
                    From = p.ExistingPath,
                    To = p.NewPath
                }));
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
            var items = Preview();
            var runId = Guid.NewGuid().ToString("N");

            _logger.ProgressInfo("Reorganising {0} file(s)", items.Count);

            CurrentRun.Value = runId;

            try
            {
                foreach (var movie in items.Where(i => i.MediaType == ReorganiseMediaTypes.Movie).GroupBy(i => i.MediaId))
                {
                    TryRun(movie.First().Title, () => _renameMovieFiles.Execute(new RenameMovieFilesCommand(movie.Key, movie.Select(i => i.FileId).ToList())));
                }

                foreach (var series in items.Where(i => i.MediaType == ReorganiseMediaTypes.Episode).GroupBy(i => i.MediaId))
                {
                    TryRun(series.First().Title, () => _renameEpisodeFiles.Execute(new RenameFilesCommand(series.Key, series.Select(i => i.FileId).ToList())));
                }
            }
            finally
            {
                CurrentRun.Value = null;
            }

            _logger.ProgressInfo("Reorganise finished: {0} file(s) renamed (run {1})", _journal.GetByRun(runId).Count, runId);
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

        // Puts a renamed video file back under its old name. Not done (and left in the journal as it was) when the file
        // is no longer where the run put it, or something else is at the old name now.
        private bool Undo(ReorganiseJournalEntry entry)
        {
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

        private void Move(ReorganiseJournalEntry entry)
        {
            _diskProvider.EnsureFolder(Path.GetDirectoryName(entry.FromPath));
            _diskProvider.MoveFile(entry.ToPath, entry.FromPath);
        }
    }
}
