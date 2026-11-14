using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Storage
{
    /// <summary>Moves series and movies between the drives of a library so free space is even.</summary>
    public class StorageBalanceCommand : Command
    {
        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
    }
}
