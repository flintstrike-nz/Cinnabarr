using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Games
{
    /// <summary>A game: one ROM or disc image in a Games root folder.</summary>
    public class Game : ModelBase
    {
        public string PlatformKey { get; set; }
        public string Title { get; set; }
        public string SortTitle { get; set; }
        public string Region { get; set; }

        // The Games root folder it is in, and the file (or .cue / .m3u playlist) itself.
        public string RootFolderPath { get; set; }
        public string Path { get; set; }

        // The file's size, plus the disc tracks beside a .cue.
        public long Size { get; set; }
        public DateTime Added { get; set; }

        // From the metadata provider (IGDB).
        public string Overview { get; set; }
        public int? Year { get; set; }
        public string Genres { get; set; }
        public string Developer { get; set; }
        public double? Rating { get; set; }
        public string CoverUrl { get; set; }
        public int? IgdbId { get; set; }

        // When a provider was last asked about this game, found or not, so the misses are not asked again every night.
        public DateTime? MetadataChecked { get; set; }
    }
}
