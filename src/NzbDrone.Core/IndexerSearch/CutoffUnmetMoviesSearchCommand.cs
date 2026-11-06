using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class CutoffUnmetMoviesSearchCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
