using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Organizer
{
    public interface IBuildMovieFolderName
    {
        string GetMovieFolder(Movie movie);
    }

    // The folder a new movie is put in, from the movie folder format in the naming settings.
    public class MovieFolderNameBuilder : IBuildMovieFolderName
    {
        private readonly IBuildMovieFileNames _fileNameBuilder;

        public MovieFolderNameBuilder(IBuildMovieFileNames fileNameBuilder)
        {
            _fileNameBuilder = fileNameBuilder;
        }

        public string GetMovieFolder(Movie movie)
        {
            return _fileNameBuilder.GetMovieFolder(movie);
        }
    }
}
