using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Organizer
{
    public interface IBuildMovieFolderName
    {
        string GetMovieFolder(Movie movie);
    }

    // The movie naming settings (roadmap 4e) will replace this fixed "Title (Year)" folder with the configurable format.
    public class MovieFolderNameBuilder : IBuildMovieFolderName
    {
        public string GetMovieFolder(Movie movie)
        {
            var name = movie.Year > 0 ? $"{movie.Title} ({movie.Year})" : movie.Title;

            return FileNameBuilder.CleanFileName(name);
        }
    }
}
