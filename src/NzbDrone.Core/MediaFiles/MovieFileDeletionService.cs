using System;
using System.IO;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDeleteMovieFiles
    {
        void DeleteMovieFile(Movie movie, MovieFile movieFile);
    }

    // Removes a movie's files from disk (through the recycle bin): one file on request, or the movie's whole folder when
    // the movie is deleted with its files.
    public class MovieFileDeletionService : IDeleteMovieFiles,
                                            IHandleAsync<MoviesDeletedEvent>,
                                            IHandle<MovieFileDeletedEvent>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMovieFileService _movieFileService;
        private readonly IMovieService _movieService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MovieFileDeletionService(IDiskProvider diskProvider,
                                        IRecycleBinProvider recycleBinProvider,
                                        IMovieFileService movieFileService,
                                        IMovieService movieService,
                                        IRootFolderService rootFolderService,
                                        IConfigService configService,
                                        IEventAggregator eventAggregator,
                                        Logger logger)
        {
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _movieFileService = movieFileService;
            _movieService = movieService;
            _rootFolderService = rootFolderService;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void DeleteMovieFile(Movie movie, MovieFile movieFile)
        {
            var fullPath = Path.Combine(movie.Path, movieFile.RelativePath);
            var rootFolder = _rootFolderService.GetBestRootFolderPath(movie.Path);

            if (!_diskProvider.FolderExists(rootFolder))
            {
                _logger.Warn("Movie's root folder ({0}) doesn't exist.", rootFolder);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Movie's root folder ({0}) doesn't exist.", rootFolder);
            }

            if (_diskProvider.GetDirectories(rootFolder).Empty())
            {
                _logger.Warn("Movie's root folder ({0}) is empty.", rootFolder);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Movie's root folder ({0}) is empty.", rootFolder);
            }

            if (_diskProvider.FolderExists(movie.Path) && _diskProvider.FileExists(fullPath))
            {
                _logger.Info("Deleting movie file: {0}", fullPath);

                var subfolder = _diskProvider.GetParentFolder(movie.Path).GetRelativePath(_diskProvider.GetParentFolder(fullPath));

                try
                {
                    _recycleBinProvider.DeleteFile(fullPath, subfolder);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Unable to delete movie file");
                    throw new NzbDroneClientException(HttpStatusCode.InternalServerError, "Unable to delete movie file");
                }
            }

            // Delete the movie file from the database to clean it up even if the file was already deleted
            _movieFileService.Delete(movieFile, DeleteMediaFileReason.Manual);

            _eventAggregator.PublishEvent(new DeleteCompletedEvent());
        }

        public void HandleAsync(MoviesDeletedEvent message)
        {
            if (!message.DeleteFiles)
            {
                return;
            }

            var allPaths = _movieService.AllMoviePaths();

            foreach (var movie in message.Movies)
            {
                if (movie.Path.IsNullOrWhiteSpace())
                {
                    continue;
                }

                // The movies are already gone from the library, so any path left belongs to another movie.
                if (allPaths.Values.Any(p => movie.Path.PathEquals(p) || movie.Path.IsParentPath(p)))
                {
                    _logger.Error("Movie path: '{0}' is the same as, or a parent of, another movie, not deleting files.", movie.Path);
                    continue;
                }

                if (_diskProvider.FolderExists(movie.Path))
                {
                    _recycleBinProvider.DeleteFolder(movie.Path);
                }

                _eventAggregator.PublishEvent(new DeleteCompletedEvent());
            }
        }

        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(MovieFileDeletedEvent message)
        {
            if (!_configService.DeleteEmptyFolders || message.Reason == DeleteMediaFileReason.MissingFromDisk)
            {
                return;
            }

            var movie = message.MovieFile.Movie;

            if (movie == null || movie.Path.IsNullOrWhiteSpace() || !_diskProvider.FolderExists(movie.Path))
            {
                return;
            }

            _diskProvider.RemoveEmptySubfolders(movie.Path);

            if (_diskProvider.FolderEmpty(movie.Path))
            {
                _diskProvider.DeleteFolder(movie.Path, true);
            }
        }
    }
}
