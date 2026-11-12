using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.IndexerEngine.Indexers;
using NzbDrone.Core.Parser.Model;
using EngineRelease = NzbDrone.Core.IndexerEngine.Parser.Model.ReleaseInfo;
using EngineTorrent = NzbDrone.Core.IndexerEngine.Parser.Model.TorrentInfo;

namespace NzbDrone.Core.Indexers.Engine
{
    /// <summary>Turns releases found by the indexer engine into Cinnabarr's own release model.</summary>
    public static class EngineReleaseMapper
    {
        public static ReleaseInfo Map(EngineRelease source)
        {
            ReleaseInfo release;

            if (source is EngineTorrent torrent)
            {
                release = new TorrentInfo
                {
                    MagnetUrl = torrent.MagnetUrl,
                    InfoHash = torrent.InfoHash,
                    Seeders = torrent.Seeders,
                    Peers = torrent.Peers
                };
            }
            else
            {
                release = new ReleaseInfo();
            }

            release.Guid = source.Guid;
            release.Title = source.Title;
            release.Size = source.Size ?? 0;
            release.DownloadUrl = source.DownloadUrl;
            release.InfoUrl = source.InfoUrl;
            release.CommentUrl = source.CommentUrl;
            release.PublishDate = source.PublishDate.ToUniversalTime();
            release.TvdbId = source.TvdbId;
            release.TvRageId = source.TvRageId;
            release.TmdbId = source.TmdbId;
            release.ImdbId = source.ImdbId > 0 ? $"tt{source.ImdbId:D7}" : null;
            release.Origin = source.Origin;
            release.Source = source.Source;
            release.Container = source.Container;
            release.Codec = source.Codec;
            release.Resolution = source.Resolution;
            release.IndexerFlags = MapFlags(source.IndexerFlags);

            return release;
        }

        public static IndexerFlags MapFlags(IEnumerable<IndexerFlag> flags)
        {
            IndexerFlags result = 0;

            foreach (var flag in flags ?? Enumerable.Empty<IndexerFlag>())
            {
                result |= flag.Name?.ToLowerInvariant() switch
                {
                    "freeleech" => IndexerFlags.Freeleech,
                    "halfleech" => IndexerFlags.Halfleech,
                    "doubleupload" => IndexerFlags.DoubleUpload,
                    "internal" => IndexerFlags.Internal,
                    "scene" => IndexerFlags.Scene,
                    _ => 0
                };
            }

            return result;
        }
    }
}
