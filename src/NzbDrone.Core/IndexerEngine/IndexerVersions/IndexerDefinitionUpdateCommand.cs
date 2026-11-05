using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerEngine.IndexerVersions
{
    public class IndexerDefinitionUpdateCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
