using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Extras.MovieExtras
{
    public interface IMovieOtherExtraFileService : IMovieExtraFileService<MovieOtherExtraFile>
    {
    }

    public class MovieOtherExtraFileService : MovieExtraFileService<MovieOtherExtraFile>, IMovieOtherExtraFileService
    {
        public MovieOtherExtraFileService(IMovieExtraFileRepository<MovieOtherExtraFile> repository, IMovieService movieService, IDiskProvider diskProvider, IRecycleBinProvider recycleBinProvider, Logger logger)
            : base(repository, movieService, diskProvider, recycleBinProvider, logger)
        {
        }
    }
}
