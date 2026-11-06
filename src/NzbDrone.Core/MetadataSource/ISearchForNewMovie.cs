using System.Collections.Generic;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISearchForNewMovie
    {
        // Looks movies up by title (optionally with a year), "imdb:tt1234567" or "tmdb:123"; movies already in the
        // library come back as the library's own.
        List<Movie> SearchForNewMovie(string title);
    }
}
