using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class EpisodeSearchCommand : Command
    {
        public List<int> EpisodeIds { get; set; }

        // Search for one version of the series (0 is the series' default version).
        public int VersionSlotId { get; set; }

        public override bool SendUpdatesToClient => true;

        public EpisodeSearchCommand()
        {
        }

        public EpisodeSearchCommand(List<int> episodeIds)
        {
            EpisodeIds = episodeIds;
        }
    }
}
