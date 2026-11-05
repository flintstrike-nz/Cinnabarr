using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Cardigann;
using NzbDrone.Core.Indexers.Engine;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using EngineCapabilities = NzbDrone.Core.IndexerEngine.Indexers.IndexerCapabilities;
using EngineDownloadResponse = NzbDrone.Core.IndexerEngine.Indexers.IndexerDownloadResponse;
using EngineIndexer = NzbDrone.Core.IndexerEngine.Indexers.IIndexer;
using EngineIndexerDefinition = NzbDrone.Core.IndexerEngine.Indexers.IndexerDefinition;
using EnginePrivacy = NzbDrone.Core.IndexerEngine.Indexers.IndexerPrivacy;
using EngineQueryResult = NzbDrone.Core.IndexerEngine.Indexers.IndexerPageableQueryResult;
using EngineRelease = NzbDrone.Core.IndexerEngine.Parser.Model.ReleaseInfo;
using EngineTorrent = NzbDrone.Core.IndexerEngine.Parser.Model.TorrentInfo;
using EngineTvCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.TvSearchCriteria;
using EngineTvParam = NzbDrone.Core.IndexerEngine.Indexers.TvSearchParam;

namespace NzbDrone.Core.Test.IndexerTests.EngineTests
{
    [TestFixture]
    public class CardigannIndexerFixture : CoreTest<Cardigann>
    {
        private Mock<EngineIndexer> _engine;
        private List<EngineTvCriteria> _queries;

        [SetUp]
        public void SetUp()
        {
            _queries = new List<EngineTvCriteria>();

            _engine = new Mock<EngineIndexer>();
            _engine.Setup(e => e.GetCapabilities()).Returns(new EngineCapabilities
            {
                TvSearchParams = new List<EngineTvParam> { EngineTvParam.Q, EngineTvParam.Season, EngineTvParam.Ep, EngineTvParam.TvdbId }
            });

            Mocker.GetMock<IEngineIndexerProvider>()
                .Setup(p => p.Get(It.IsAny<IndexerDefinition>()))
                .Returns(_engine.Object);

            Subject.Definition = new IndexerDefinition
            {
                Id = 9,
                Name = "My Tracker",
                Settings = new CardigannSettings { DefinitionFile = "mytracker", MultiLanguages = Array.Empty<int>() }
            };
        }

        private void GivenQueryReturns(Func<EngineTvCriteria, List<EngineRelease>> releases)
        {
            _engine.Setup(e => e.Fetch(It.IsAny<EngineTvCriteria>()))
                .Returns((EngineTvCriteria c) =>
                {
                    _queries.Add(c);

                    return Task.FromResult(new EngineQueryResult { Releases = releases(c) });
                });
        }

        private static SingleEpisodeSearchCriteria Search() => new()
        {
            Series = new Series { Title = "The Show", TvdbId = 1234 },
            SceneTitles = new List<string> { "The Show" },
            SeasonNumber = 2,
            EpisodeNumber = 5
        };

        [Test]
        public async Task should_return_mapped_releases_tagged_with_the_indexer()
        {
            GivenQueryReturns(_ => new List<EngineRelease>
            {
                new EngineTorrent { Guid = "a", Title = "The.Show.S02E05", Size = 10, MagnetUrl = "magnet:?xt=urn:btih:abc", Seeders = 3 }
            });

            var releases = await Subject.Fetch(Search());

            var release = releases.Should().ContainSingle().Which.Should().BeOfType<TorrentInfo>().Subject;
            release.Title.Should().Be("The.Show.S02E05");
            release.Seeders.Should().Be(3);
            release.IndexerId.Should().Be(9);
            release.Indexer.Should().Be("My Tracker");
            release.DownloadProtocol.Should().Be(DownloadProtocol.Torrent);
        }

        [Test]
        public async Task should_not_run_the_title_fallback_when_the_id_search_finds_releases()
        {
            GivenQueryReturns(c => c.TvdbId.HasValue ? new List<EngineRelease> { new EngineTorrent { Guid = "a", Title = "x" } } : new List<EngineRelease>());

            await Subject.Fetch(Search());

            _queries.Should().ContainSingle().Which.TvdbId.Should().Be(1234);
        }

        [Test]
        public async Task should_run_the_title_fallback_when_the_id_search_finds_nothing()
        {
            GivenQueryReturns(c => c.SearchTerm == "Show" ? new List<EngineRelease> { new EngineTorrent { Guid = "a", Title = "x" } } : new List<EngineRelease>());

            var releases = await Subject.Fetch(Search());

            _queries.Should().HaveCount(2);
            releases.Should().ContainSingle();
        }

        [Test]
        public async Task should_run_a_single_rss_query_for_recent_releases()
        {
            GivenQueryReturns(_ => new List<EngineRelease>());

            await Subject.FetchRecent();

            _queries.Should().ContainSingle().Which.IsRssSearch.Should().BeTrue();
        }

        [Test]
        public async Task should_download_the_torrent_file_through_the_engine()
        {
            _engine.Setup(e => e.Download(new Uri("http://x/dl"))).ReturnsAsync(new EngineDownloadResponse(new byte[] { 1, 2, 3 }));

            var data = await Subject.DownloadTorrentFile("http://x/dl");

            data.Should().Equal(1, 2, 3);
        }

        [Test]
        public void should_turn_saved_json_values_into_plain_values_for_the_engine()
        {
            var data = new Dictionary<string, object>
            {
                { "username", System.Text.Json.JsonDocument.Parse("\"me\"").RootElement },
                { "remember", System.Text.Json.JsonDocument.Parse("true").RootElement },
                { "limit", System.Text.Json.JsonDocument.Parse("5").RootElement }
            };

            var result = EngineIndexerProvider.NormalizeExtraFields(data);

            result["username"].Should().Be("me");
            result["remember"].Should().Be(true);
            result["limit"].Should().Be(5L);
        }

        [Test]
        public void should_not_offer_itself_as_a_generic_default_indexer()
        {
            Subject.DefaultDefinitions.Should().BeEmpty();
        }

        [Test]
        public void should_build_an_engine_definition_from_the_saved_one()
        {
            var settings = new CardigannSettings
            {
                DefinitionFile = "mytracker",
                BaseUrl = "https://tracker.example",
                MinimumSeeders = 4,
                ExtraFieldData = new Dictionary<string, object> { { "username", "me" } }
            };

            var saved = new IndexerDefinition { Id = 9, Name = "My Tracker", Settings = settings, Tags = new HashSet<int> { 3 }, Priority = 10 };
            var template = new EngineIndexerDefinition { IndexerUrls = new[] { "https://tracker.example" }, Privacy = EnginePrivacy.Private, Capabilities = new EngineCapabilities() };

            var built = EngineIndexerProvider.Build(saved, settings, template);

            built.Id.Should().Be(9);
            built.Tags.Should().BeEquivalentTo(new[] { 3 });
            built.Privacy.Should().Be(EnginePrivacy.Private);
            built.Capabilities.Should().BeSameAs(template.Capabilities);

            var engineSettings = built.Settings.Should().BeOfType<NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann.CardigannSettings>().Subject;
            engineSettings.DefinitionFile.Should().Be("mytracker");
            engineSettings.BaseUrl.Should().Be("https://tracker.example");
            engineSettings.ExtraFieldData["username"].Should().Be("me");
            engineSettings.TorrentBaseSettings.AppMinimumSeeders.Should().Be(4);
        }
    }
}
