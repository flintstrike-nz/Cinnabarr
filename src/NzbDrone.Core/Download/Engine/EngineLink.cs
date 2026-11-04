using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Download.Engine
{
    // Link intent stored next to the engine's own state (roadmap §4.4 rule 5), one file per
    // torrent at <AppData>/engine/embarr/<info-hash>.json. It holds external ids only, never
    // database ids, so a database restored from an older backup can rebuild the link.
    public class EngineLink
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public string MediaType { get; set; }
        public int TvdbId { get; set; }
        public List<EngineLinkEpisode> Episodes { get; set; } = new();
        public string SourceTitle { get; set; }
        public string Indexer { get; set; }

        // The grabbed quality; null in sidecars written before it was added, which fall back to parsing the title.
        public EngineLinkQuality Quality { get; set; }
        public DateTime Grabbed { get; set; }

        // Written when the torrent is prepared (roadmap §4.8): the folder the engine saves into and, for each kept
        // file, its hidden pending name and final library name, both relative to that folder.
        public string SavePath { get; set; }
        public List<EngineLinkFile> Files { get; set; }

        // Set once every video in the torrent has been imported or rejected and the download is recorded as done.
        public bool Completed { get; set; }
    }

    public enum EngineLinkFileState
    {
        Pending = 0,
        Renamed = 1,
        Imported = 2,
        Rejected = 3,

        // An imported file that a newer file for the same episodes replaced; no longer downloaded or seeded.
        Replaced = 4
    }

    public class EngineLinkFile
    {
        public int Index { get; set; }
        public bool IsSubtitle { get; set; }

        // For a subtitle: the index of the video it belongs to.
        public int? VideoIndex { get; set; }
        public List<EngineLinkEpisode> Episodes { get; set; } = new();
        public string PendingPath { get; set; }
        public string FinalPath { get; set; }
        public EngineLinkFileState State { get; set; }

        // For a video: the original title and file name its final name was built from, so the name can be
        // rebuilt once media info is known.
        public string SceneName { get; set; }
        public string OriginalName { get; set; }

        // For a video that upgrades existing episode files: the files it replaces, and the engine torrents (info-hashes)
        // they were detached from, which are rechecked once the new file is in the library.
        public List<int> ReplacesFileIds { get; set; }
        public List<string> DetachedFrom { get; set; }

        // For a subtitle: what its name says (language id, tags such as forced or sdh), and the number that keeps it
        // apart from other subtitles of its video named alike (saved as the subtitle's title, which is what library
        // renames keep). Recorded with it once it is published.
        public int LanguageId { get; set; }
        public List<string> LanguageTags { get; set; }
        public string SubtitleTitle { get; set; }

        // For a video: when it was imported, which is when its seeding time starts.
        public DateTime? ImportedAt { get; set; }
    }

    public class EngineLinkQuality
    {
        public int Id { get; set; }
        public int Version { get; set; } = 1;
        public int Real { get; set; }
        public bool IsRepack { get; set; }
    }

    public class EngineLinkEpisode
    {
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
    }
}
