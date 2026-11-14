using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Games
{
    public interface IGameRepository : IBasicRepository<Game>
    {
    }

    public class GameRepository : BasicRepository<Game>, IGameRepository
    {
        public GameRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
