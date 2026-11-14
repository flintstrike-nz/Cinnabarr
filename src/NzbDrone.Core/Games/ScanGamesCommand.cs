using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Games
{
    /// <summary>Looks through the Games root folders for ROMs and disc images.</summary>
    public class ScanGamesCommand : Command
    {
        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
    }
}
