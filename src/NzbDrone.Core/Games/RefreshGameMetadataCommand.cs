using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Games
{
    /// <summary>Looks up cover art and details for games that have none.</summary>
    public class RefreshGameMetadataCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
