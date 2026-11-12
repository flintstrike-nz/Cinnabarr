using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class MoviesSearchCommand : Command
    {
        public List<int> MovieIds { get; set; }

        // Search for one version slot of the movie (0 is the movie itself).
        public int VersionSlotId { get; set; }

        public override bool SendUpdatesToClient => true;

        public MoviesSearchCommand()
        {
        }

        public MoviesSearchCommand(List<int> movieIds)
        {
            MovieIds = movieIds;
        }
    }
}
