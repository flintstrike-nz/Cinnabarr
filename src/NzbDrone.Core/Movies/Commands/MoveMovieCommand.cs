using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Movies.Commands
{
    public class MoveMovieCommand : Command
    {
        public int MovieId { get; set; }
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }

        // Movie files to rename once the folder has moved (the rename needs the new path, so it cannot be queued beside the move).
        public List<int> RenameFileIds { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
    }
}
