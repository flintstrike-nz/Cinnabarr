using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Extras.MovieExtras
{
    public interface IMovieOtherExtraFileRepository : IMovieExtraFileRepository<MovieOtherExtraFile>
    {
    }

    public class MovieOtherExtraFileRepository : MovieExtraFileRepository<MovieOtherExtraFile>, IMovieOtherExtraFileRepository
    {
        public MovieOtherExtraFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
