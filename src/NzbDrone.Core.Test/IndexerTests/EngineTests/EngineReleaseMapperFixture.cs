using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.IndexerEngine.Indexers;
using NzbDrone.Core.Indexers.Engine;
using NzbDrone.Core.Parser.Model;
using EngineRelease = NzbDrone.Core.IndexerEngine.Parser.Model.ReleaseInfo;
using EngineTorrent = NzbDrone.Core.IndexerEngine.Parser.Model.TorrentInfo;

namespace NzbDrone.Core.Test.IndexerTests.EngineTests
{
    [TestFixture]
    public class EngineReleaseMapperFixture
    {
        [Test]
        public void maps_a_torrent_release()
        {
            var source = new EngineTorrent
            {
                Guid = "g1",
                Title = "The.Show.S02E05.1080p.WEB-DL",
                Size = 1234,
                DownloadUrl = "http://x/dl",
                InfoUrl = "http://x/info",
                PublishDate = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                TvdbId = 1234,
                ImdbId = 42,
                MagnetUrl = "magnet:?xt=urn:btih:abc",
                InfoHash = "abc",
                Seeders = 10,
                Peers = 12,
                IndexerFlags = new HashSet<IndexerFlag> { IndexerFlag.FreeLeech, IndexerFlag.Scene }
            };

            var release = EngineReleaseMapper.Map(source).Should().BeOfType<TorrentInfo>().Subject;

            release.Guid.Should().Be("g1");
            release.Title.Should().Be(source.Title);
            release.Size.Should().Be(1234);
            release.DownloadUrl.Should().Be("http://x/dl");
            release.TvdbId.Should().Be(1234);
            release.ImdbId.Should().Be("tt0000042");
            release.MagnetUrl.Should().Be("magnet:?xt=urn:btih:abc");
            release.InfoHash.Should().Be("abc");
            release.Seeders.Should().Be(10);
            release.Peers.Should().Be(12);
            release.IndexerFlags.Should().Be(IndexerFlags.Freeleech | IndexerFlags.Scene);
            release.PublishDate.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void leaves_missing_values_empty()
        {
            var release = EngineReleaseMapper.Map(new EngineRelease { Title = "x" });

            release.Should().NotBeOfType<TorrentInfo>();
            release.Size.Should().Be(0);
            release.ImdbId.Should().BeNull();
            ((int)release.IndexerFlags).Should().Be(0);
        }
    }
}
