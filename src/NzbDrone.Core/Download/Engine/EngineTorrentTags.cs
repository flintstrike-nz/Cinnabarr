using System;

namespace NzbDrone.Core.Download.Engine
{
    // Tags Cinnabarr puts on built-in engine torrents. The "cinnabarr-" prefix is reserved: series tags with it are
    // never copied onto torrents, so a user tag can't be mistaken for one of these.
    public static class EngineTorrentTags
    {
        public const string Prefix = "cinnabarr-";

        // Added with every torrent the engine holds for preparation (roadmap §4.8). Torrents without it, such
        // as ones grabbed before preparation existed, are never touched.
        public const string Held = "cinnabarr-held";

        // Added once the file whitelist is applied and the torrent is started.
        public const string Prepared = "cinnabarr-prepared";

        // Added, with Held, to a torrent added by hand on the Downloads page (roadmap §6.2). Cinnabarr has no grab for it,
        // so preparation only applies the file whitelist; it stays Unlinked until Manual Import links its files.
        public const string Manual = "cinnabarr-manual";

        public static bool HasTag(string tags, string tag)
        {
            return Array.IndexOf((tags ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), tag) >= 0;
        }

        // Held for preparation and not prepared yet: starting it now would download files the whitelist hasn't
        // excluded yet.
        public static bool IsAwaitingPreparation(string tags)
        {
            var list = (tags ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            return Array.IndexOf(list, Held) >= 0 && Array.IndexOf(list, Prepared) < 0;
        }

        public static bool IsReserved(string tag)
        {
            return tag != null && tag.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
