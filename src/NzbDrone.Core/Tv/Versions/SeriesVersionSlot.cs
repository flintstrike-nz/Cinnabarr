using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tv.Versions
{
    // An extra named version of a series (for example "4K") with its own quality profile and monitored state. The series'
    // own profile, monitored state and files are its default slot, which has no row here and the id DefaultSlotId.
    public class SeriesVersionSlot : ModelBase
    {
        public const int DefaultSlotId = 0;

        public int SeriesId { get; set; }
        public string Label { get; set; }
        public int QualityProfileId { get; set; }
        public bool Monitored { get; set; }
        public DateTime Added { get; set; }
    }

    // The file of one version slot for one episode.
    public class EpisodeVersionFile : ModelBase
    {
        public int EpisodeId { get; set; }
        public int VersionSlotId { get; set; }
        public int EpisodeFileId { get; set; }
    }
}
