using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Movies.Events;

namespace NzbDrone.Core.Movies
{
    // Moves a movie's folder when its path is changed with "move files" (the movie already holds the new path).
    public class MoveMovieService : IExecute<MoveMovieCommand>
    {
        private readonly IMovieService _movieService;
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public MoveMovieService(IMovieService movieService,
                                 IDiskProvider diskProvider,
                                 IDiskTransferService diskTransferService,
                                 IEventAggregator eventAggregator,
                                 IManageCommandQueue commandQueueManager,
                                 Logger logger)
        {
            _movieService = movieService;
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        private bool MoveSingleMovie(Movie movie, string sourcePath, string destinationPath, int? index = null, int? total = null)
        {
            if (!sourcePath.IsPathValid(PathValidationType.CurrentOs))
            {
                _logger.Warn("Folder '{0}' for '{1}' is invalid, unable to move movie. Try moving files manually", sourcePath, movie.Title);
                return false;
            }

            if (!_diskProvider.FolderExists(sourcePath))
            {
                _logger.Debug("Folder '{0}' for '{1}' does not exist, not moving.", sourcePath, movie.Title);
                return false;
            }

            if (index != null && total != null)
            {
                _logger.ProgressInfo("Moving {0} from '{1}' to '{2}' ({3}/{4})", movie.Title, sourcePath, destinationPath, index + 1, total);
            }
            else
            {
                _logger.ProgressInfo("Moving {0} from '{1}' to '{2}'", movie.Title, sourcePath, destinationPath);
            }

            if (sourcePath.PathEquals(destinationPath))
            {
                _logger.ProgressInfo("{0} is already in the specified location '{1}'.", movie, destinationPath);
                return false;
            }

            try
            {
                _diskProvider.CreateFolder(new DirectoryInfo(destinationPath).Parent.FullName);
                _diskTransferService.TransferFolder(sourcePath, destinationPath, TransferMode.Move);

                _logger.ProgressInfo("{0} moved successfully to {1}", movie.Title, destinationPath);

                _eventAggregator.PublishEvent(new MovieMovedEvent(movie, sourcePath, destinationPath));

                return true;
            }
            catch (IOException ex)
            {
                _logger.Error(ex, "Unable to move movie from '{0}' to '{1}'. Try moving files manually", sourcePath, destinationPath);

                RevertPath(movie.Id, sourcePath);

                return false;
            }
        }

        private void RevertPath(int movieId, string path)
        {
            var movie = _movieService.GetMovie(movieId);

            movie.Path = path;
            _movieService.UpdateMovie(movie);
        }

        public void Execute(MoveMovieCommand message)
        {
            var movie = _movieService.GetMovie(message.MovieId);

            var moved = MoveSingleMovie(movie, message.SourcePath, message.DestinationPath);

            if (moved && message.RenameFileIds is { Count: > 0 })
            {
                _commandQueueManager.Push(new RenameMovieFilesCommand(movie.Id, message.RenameFileIds));
            }
        }
    }
}
