using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.MovieImport
{
    /// <summary>
    /// Merges folders or loose files that belong to a movie already in the library into that movie: the video files
    /// are imported into the movie's folder (moved), like a Manual Import would, and an emptied folder is removed.
    /// Files that aren't wanted (a sample, not an upgrade) stay where they are, and the command's results say why.
    /// </summary>
    public class MergeIntoMovieCommand : Command
    {
        public int MovieId { get; set; }
        public List<string> Paths { get; set; } = new();

        // What happened to each file (or path that couldn't be merged at all), filled in as the merge runs.
        public List<MergeIntoMovieResult> Results { get; set; } = new();

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsLongRunning => true;
        public override string CompletionMessage => "Merge finished";
    }

    public class MergeIntoMovieResult
    {
        public string Path { get; set; }
        public bool Merged { get; set; }
        public string Reason { get; set; }
    }

    public class MergeIntoMovieService : IExecute<MergeIntoMovieCommand>
    {
        private readonly IDownloadedMovieImportService _importService;
        private readonly IMovieService _movieService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public MergeIntoMovieService(IDownloadedMovieImportService importService,
                                     IMovieService movieService,
                                     IRootFolderService rootFolderService,
                                     IDiskProvider diskProvider,
                                     Logger logger)
        {
            _importService = importService;
            _movieService = movieService;
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Execute(MergeIntoMovieCommand message)
        {
            var movie = _movieService.GetMovie(message.MovieId);
            var results = message.Results;

            foreach (var path in message.Paths.Distinct())
            {
                var problem = WhyNotMergeable(path, movie);

                if (problem != null)
                {
                    Leave(results, path, problem);
                    continue;
                }

                _logger.ProgressInfo("Merging '{0}' into {1}", path, movie.Title);

                var imported = _importService.ProcessPath(path, ImportMode.Move, movie) ?? new List<ImportResult>();

                if (imported.Empty())
                {
                    Leave(results, path, "There's no video file in it");
                }

                foreach (var result in imported)
                {
                    var filePath = result.ImportDecision.LocalMovie?.Path ?? path;

                    if (result.Result == ImportResultType.Imported)
                    {
                        results.Add(new MergeIntoMovieResult { Path = filePath, Merged = true });
                    }
                    else
                    {
                        Leave(results, filePath, string.Join("; ", result.Errors));
                    }
                }

                RemoveFolderIfEmpty(path);
            }

            _logger.ProgressInfo("Merged {0} file(s) into {1}; {2} left in place", results.Count(r => r.Merged), movie.Title, results.Count(r => !r.Merged));
        }

        private void Leave(List<MergeIntoMovieResult> results, string path, string reason)
        {
            _logger.Info("'{0}' isn't merged: {1}", path, reason);
            results.Add(new MergeIntoMovieResult { Path = path, Merged = false, Reason = reason });
        }

        // Only something inside a root folder, and not the root folder or the movie's own folder, is moved.
        private string WhyNotMergeable(string path, Movie movie)
        {
            if (path.IsNullOrWhiteSpace() || (!_diskProvider.FolderExists(path) && !_diskProvider.FileExists(path)))
            {
                return "It isn't there any more";
            }

            var rootFolder = _rootFolderService.GetBestRootFolderPath(path);

            if (rootFolder.IsNullOrWhiteSpace() || path.PathEquals(rootFolder) || !rootFolder.IsParentPath(path))
            {
                return "It isn't inside a root folder";
            }

            if (movie.Path.IsNotNullOrWhiteSpace() && (path.PathEquals(movie.Path) || movie.Path.IsParentPath(path)))
            {
                return $"It's already in the folder of {movie.Title}";
            }

            return null;
        }

        // Nothing is deleted but folders: the folder goes only when no file is left in it.
        private void RemoveFolderIfEmpty(string path)
        {
            if (!_diskProvider.FolderExists(path))
            {
                return;
            }

            _diskProvider.RemoveEmptySubfolders(path);

            if (!_diskProvider.GetFiles(path, true).Any() && _diskProvider.FolderExists(path))
            {
                _diskProvider.DeleteFolder(path, true);
            }
            else
            {
                _logger.Info("'{0}' still holds files, so it stays", path);
            }
        }
    }
}
