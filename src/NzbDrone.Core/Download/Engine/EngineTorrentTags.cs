using System;

namespace NzbDrone.Core.Download.Engine
{
    // Tags Embarr puts on built-in engine torrents. The "embarr-" prefix is reserved: series tags with it are
    // never copied onto torrents, so a user tag can't be mistaken for one of these.
    public static class EngineTorrentTags
    {
        public const string Prefix = "embarr-";

        // Added with every torrent the engine holds for preparation (roadmap §4.8). Torrents without it, such
        // as ones grabbed before preparation existed, are never touched.
        public const string Held = "embarr-held";

        // Added once the file whitelist is applied and the torrent is started.
        public const string Prepared = "embarr-prepared";

        public static bool IsReserved(string tag)
        {
            return tag != null && tag.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
