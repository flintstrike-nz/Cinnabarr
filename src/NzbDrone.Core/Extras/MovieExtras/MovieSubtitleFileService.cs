using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Extras.MovieExtras
{
    public interface IMovieSubtitleFileService : IMovieExtraFileService<MovieSubtitleFile>
    {
    }

    public class MovieSubtitleFileService : MovieExtraFileService<MovieSubtitleFile>, IMovieSubtitleFileService
    {
        public MovieSubtitleFileService(IMovieExtraFileRepository<MovieSubtitleFile> repository, IMovieService movieService, IDiskProvider diskProvider, IRecycleBinProvider recycleBinProvider, Logger logger)
            : base(repository, movieService, diskProvider, recycleBinProvider, logger)
        {
        }
    }
}
