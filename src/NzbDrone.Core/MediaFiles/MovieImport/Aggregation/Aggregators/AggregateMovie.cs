using NzbDrone.Core.Download;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators
{
    public class AggregateMovie : IAggregateLocalMovie
    {
        public int Order => 1;

        private readonly IMovieService _movieService;
        private readonly IVersionSlotService _versionSlotService;

        public AggregateMovie(IMovieService movieService, IVersionSlotService versionSlotService)
        {
            _movieService = movieService;
            _versionSlotService = versionSlotService;
        }

        public LocalMovie Aggregate(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            // Read again, as the version of the movie the file is for (the default one is the movie itself).
            localMovie.Movie = _versionSlotService.GetView(_movieService.GetMovie(localMovie.Movie.Id), localMovie.Movie.VersionSlotId);

            return localMovie;
        }
    }
}
