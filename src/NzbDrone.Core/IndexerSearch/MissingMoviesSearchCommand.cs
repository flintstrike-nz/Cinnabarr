using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class MissingMoviesSearchCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
