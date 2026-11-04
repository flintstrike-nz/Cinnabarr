using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Download.Engine
{
    // Lists (or, once approved, deletes) partial download files no engine torrent owns (roadmap §4.13).
    public class EngineOrphanCleanupCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
