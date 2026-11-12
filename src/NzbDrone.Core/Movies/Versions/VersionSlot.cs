using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Movies.Versions
{
    // An extra named version of a movie (for example "4K") with its own quality profile and monitored state. The movie's
    // own profile, monitored state and file are its default slot, which has no row here and the id DefaultSlotId.
    public class VersionSlot : ModelBase
    {
        public const int DefaultSlotId = 0;

        public int MovieId { get; set; }
        public string Label { get; set; }
        public int QualityProfileId { get; set; }
        public bool Monitored { get; set; }
        public DateTime Added { get; set; }
    }
}
