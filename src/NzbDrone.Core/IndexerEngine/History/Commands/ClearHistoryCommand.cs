using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerEngine.History
{
    public class ClearHistoryCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
