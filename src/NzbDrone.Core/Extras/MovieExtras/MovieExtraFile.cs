using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Extras.MovieExtras
{
    public abstract class MovieExtraFile : ModelBase
    {
        public int MovieId { get; set; }
        public int? MovieFileId { get; set; }
        public string RelativePath { get; set; }
        public DateTime Added { get; set; }
        public DateTime LastUpdated { get; set; }
        public string Extension { get; set; }

        public override string ToString()
        {
            return $"[{Id}] {RelativePath}";
        }
    }
}
