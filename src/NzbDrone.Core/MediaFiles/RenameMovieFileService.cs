using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.MediaFiles
{
    public interface IRenameMovieFileService
    {
        List<RenameMovieFilePreview> GetRenamePreviews(List<int> movieIds);
    }

    public class RenameMovieFileService : IRenameMovieFileService,
                                          IExecute<RenameMovieFilesCommand>,
                                          IExecute<RenameMovieCommand>
    {
        private readonly IMovieService _movieService;
        private readonly IVersionSlotService _versionSlotService;
        private readonly IMovieFileService _movieFileService;
        private readonly IMoveMovieFiles _movieFileMover;
        private readonly IEventAggregator _eventAggregator;
        private readonly IBuildMovieFileNames _filenameBuilder;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public RenameMovieFileService(IMovieService movieService,
                                      IMovieFileService movieFileService,
                                      IMoveMovieFiles movieFileMover,
                                      IEventAggregator eventAggregator,
                                      IBuildMovieFileNames filenameBuilder,
                                      IDiskProvider diskProvider,
                                      IVersionSlotService versionSlotService,
                                      Logger logger)
        {
            _movieService = movieService;
            _versionSlotService = versionSlotService;
            _movieFileService = movieFileService;
            _movieFileMover = movieFileMover;
            _eventAggregator = eventAggregator;
            _filenameBuilder = filenameBuilder;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public List<RenameMovieFilePreview> GetRenamePreviews(List<int> movieIds)
        {
            var movies = _movieService.GetMovies(movieIds);
            var movieFiles = _movieFileService.GetFilesByMovies(movieIds).ToLookup(f => f.MovieId);

            return movies.SelectMany(movie =>
                {
                    var files = movieFiles[movie.Id].ToList();

                    return GetPreviews(movie, files);
                })
                .ToList();
        }

        private IEnumerable<RenameMovieFilePreview> GetPreviews(Movie movie, List<MovieFile> files)
        {
            foreach (var file in files)
            {
                var movieFilePath = Path.Combine(movie.Path, file.RelativePath);

                // A file of one of the movie's versions is named as that version.
                var named = ViewOf(movie, file);
                var newName = _filenameBuilder.BuildFileName(named, file);
                var newPath = _filenameBuilder.BuildFilePath(named, newName, Path.GetExtension(movieFilePath));

                if (!movieFilePath.PathEquals(newPath, StringComparison.Ordinal))
                {
                    yield return new RenameMovieFilePreview
                    {
                        MovieId = movie.Id,
                        MovieFileId = file.Id,
                        ExistingPath = file.RelativePath,

                        NewPath = movie.Path.GetRelativePath(newPath)
                    };
                }
            }
        }

        private Movie ViewOf(Movie movie, MovieFile file)
        {
            return file.VersionSlotId == 0 ? movie : _versionSlotService.GetView(movie, file.VersionSlotId);
        }

        private List<RenamedMovieFile> RenameFiles(List<MovieFile> movieFiles, Movie movie)
        {
            var renamed = new List<RenamedMovieFile>();

            foreach (var movieFile in movieFiles)
            {
                var previousRelativePath = movieFile.RelativePath;
                var previousPath = Path.Combine(movie.Path, movieFile.RelativePath);

                try
                {
                    _logger.Debug("Renaming movie file: {0}", movieFile);
                    _movieFileMover.MoveMovieFile(movieFile, ViewOf(movie, movieFile));

                    _movieFileService.Update(movieFile);
                    _movieService.UpdateMovie(movie);
                    renamed.Add(new RenamedMovieFile
                                {
                                    MovieFile = movieFile,
                                    PreviousRelativePath = previousRelativePath,
                                    PreviousPath = previousPath
                                });

                    _logger.Debug("Renamed movie file: {0}", movieFile);

                    _eventAggregator.PublishEvent(new MovieFileRenamedEvent(movie, movieFile, previousPath));
                }
                catch (FileAlreadyExistsException ex)
                {
                    _logger.Warn("File not renamed, there is already a file at the destination: {0}", ex.Filename);
                }
                catch (SameFilenameException ex)
                {
                    _logger.Debug("File not renamed, source and destination are the same: {0}", ex.Filename);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to rename file: {0}", previousPath);
                }
            }

            if (renamed.Any())
            {
                _diskProvider.RemoveEmptySubfolders(movie.Path);

                _eventAggregator.PublishEvent(new MovieRenamedEvent(movie, renamed));
            }

            return renamed;
        }

        public void Execute(RenameMovieFilesCommand message)
        {
            var movie = _movieService.GetMovie(message.MovieId);
            var movieFiles = _movieFileService.GetMovies(message.Files);

            _logger.ProgressInfo("Renaming {0} files for {1}", movieFiles.Count, movie.Title);
            var renamedFiles = RenameFiles(movieFiles, movie);
            _logger.ProgressInfo("{0} selected movie files renamed for {1}", renamedFiles.Count, movie.Title);

            _eventAggregator.PublishEvent(new RenameCompletedEvent());
        }

        public void Execute(RenameMovieCommand message)
        {
            _logger.Debug("Renaming movie files for selected movie");
            var moviesToRename = _movieService.GetMovies(message.MovieIds);

            foreach (var movie in moviesToRename)
            {
                var movieFiles = _movieFileService.GetFilesByMovie(movie.Id);
                _logger.ProgressInfo("Renaming movie files for {0}", movie.Title);
                var renamedFiles = RenameFiles(movieFiles, movie);
                _logger.ProgressInfo("{0} movie files renamed for {1}", renamedFiles.Count, movie.Title);
            }

            _eventAggregator.PublishEvent(new RenameCompletedEvent());
        }
    }
}
