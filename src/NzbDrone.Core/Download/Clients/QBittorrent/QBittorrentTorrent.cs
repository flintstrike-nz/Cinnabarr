using System.Numerics;
using Newtonsoft.Json;

namespace NzbDrone.Core.Download.Clients.QBittorrent
{
    // torrent properties from the list returned by /query/torrents
    public class QBittorrentTorrent
    {
        public string Hash { get; set; } // Torrent hash

        [JsonProperty(PropertyName = "infohash_v1")]
        public string InfoHashV1 { get; set; } // BitTorrent v1 info-hash (qBittorrent 4.4+); empty for a v2-only torrent

        [JsonProperty(PropertyName = "infohash_v2")]
        public string InfoHashV2 { get; set; } // BitTorrent v2 info-hash (qBittorrent 4.4+); empty for a v1-only torrent

        public string Name { get; set; } // Torrent name

        public long Size { get; set; } // Torrent size (bytes)

        public double Progress { get; set; } // Torrent progress (%/100)

        public BigInteger Eta { get; set; } // Torrent ETA (seconds) (QBit contains a bug exceeding ulong limits)

        public string State { get; set; } // Torrent state. See possible values here below

        public string Label { get; set; } // Label of the torrent
        public string Category { get; set; } // Category of the torrent (3.3.5+)

        [JsonProperty(PropertyName = "save_path")]
        public string SavePath { get; set; } // Torrent save path

        [JsonProperty(PropertyName = "root_path")]
        public string RootPath { get; set; } // Torrent root folder (qBittorrent 5.0+). Empty when the torrent has no root folder.

        [JsonProperty(PropertyName = "content_path")]
        public string ContentPath { get; set; } // Torrent content path (folder, or the file for a single-file torrent)

        public float Ratio { get; set; } // Torrent share ratio

        [JsonProperty(PropertyName = "ratio_limit")] // Per torrent seeding ratio limit (-2 = use global, -1 = unlimited)
        public float RatioLimit { get; set; } = -2;

        [JsonProperty(PropertyName = "seeding_time")]
        public long? SeedingTime { get; set; } // Torrent seeding time (in seconds, not provided by the list api)

        [JsonProperty(PropertyName = "seeding_time_limit")] // Per torrent seeding time limit (-2 = use global, -1 = unlimited)
        public long SeedingTimeLimit { get; set; } = -2;

        [JsonProperty(PropertyName = "inactive_seeding_time_limit")] // Per torrent inactive seeding time limit (-2 = use global, -1 = unlimited)
        public long InactiveSeedingTimeLimit { get; set; } = -2;

        [JsonProperty(PropertyName = "last_activity")] // Timestamp in unix seconds when a chunk was last downloaded/uploaded
        public long LastActivity { get; set; }

        [JsonProperty(PropertyName = "has_metadata")] // False while a magnet is still fetching its metadata
        public bool HasMetadata { get; set; } = true;

        [JsonProperty(PropertyName = "dl_limit")] // The torrent's own download limit in bytes per second (0 or -1 = none)
        public long DownloadLimit { get; set; }

        [JsonProperty(PropertyName = "up_limit")] // The torrent's own upload limit in bytes per second (0 or -1 = none)
        public long UploadLimit { get; set; }

        [JsonProperty(PropertyName = "priority")] // The torrent's place in the queue (0 when queueing is off)
        public int QueuePosition { get; set; }

        public string Tags { get; set; } // Comma-separated tags
    }

    public class QBittorrentTorrentProperties
    {
        public string Hash { get; set; } // Torrent hash

        [JsonProperty(PropertyName = "save_path")]
        public string SavePath { get; set; }

        [JsonProperty(PropertyName = "seeding_time")]
        public long SeedingTime { get; set; } // Torrent seeding time (in seconds)
    }

    public class QBittorrentTorrentFile
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public double Progress { get; set; }
        public int Priority { get; set; }
    }

    // Global transfer figures from /transfer/info.
    public class QBittorrentTransferInfo
    {
        [JsonProperty(PropertyName = "dl_info_speed")]
        public long DownloadSpeed { get; set; } // bytes/s

        [JsonProperty(PropertyName = "up_info_speed")]
        public long UploadSpeed { get; set; } // bytes/s
    }
}
