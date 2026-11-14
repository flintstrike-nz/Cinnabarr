using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.RootFolders
{
    public class RootFolder : ModelBase
    {
        public string Path { get; set; }

        // What the root folder holds; a series can't be added under a movies root folder or the other way round.
        public LibraryType Library { get; set; }
        public bool Accessible { get; set; }
        public bool IsEmpty { get; set; }
        public long? FreeSpace { get; set; }
        public long? TotalSpace { get; set; }

        public List<UnmappedFolder> UnmappedFolders { get; set; }
    }
}
