using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class SeasonSearchCommand : Command
    {
        public int SeriesId { get; set; }
        public int SeasonNumber { get; set; }

        // Search for one version of the series (0 is the series' default version).
        public int VersionSlotId { get; set; }

        public override bool SendUpdatesToClient => true;
    }
}
