using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Extras.MovieExtras
{
    public interface IMovieSubtitleFileRepository : IMovieExtraFileRepository<MovieSubtitleFile>
    {
    }

    public class MovieSubtitleFileRepository : MovieExtraFileRepository<MovieSubtitleFile>, IMovieSubtitleFileRepository
    {
        public MovieSubtitleFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
