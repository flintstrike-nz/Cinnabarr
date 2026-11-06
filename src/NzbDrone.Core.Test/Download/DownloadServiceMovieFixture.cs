using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadServiceMovieFixture : CoreTest<DownloadService>
    {
        private RemoteMovie _remoteMovie;
        private Mock<IDownloadClient> _client;

        [SetUp]
        public void SetUp()
        {
            _remoteMovie = new RemoteMovie
            {
                Movie = new Movie { Id = 3, Tags = new HashSet<int> { 4 }, MovieMetadata = new MovieMetadata { Title = "The Matrix", Year = 1999 } },
                Release = new ReleaseInfo { Title = "The.Matrix.1999.1080p.BluRay.x264-GRP", DownloadProtocol = DownloadProtocol.Torrent, DownloadUrl = "magnet:?xt=urn:btih:abc" }
            };

            _client = new Mock<IDownloadClient>();
            _client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 9, Name = "Engine" });
            _client.SetupGet(c => c.Protocol).Returns(DownloadProtocol.Torrent);
            _client.SetupGet(c => c.Name).Returns("qBittorrent");
            _client.Setup(c => c.Download(It.IsAny<RemoteMedia>(), It.IsAny<IIndexer>())).ReturnsAsync("HASH");

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(p => p.GetDownloadClients(DownloadProtocol.Torrent, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                  .Returns(new[] { _client.Object });
        }

        [Test]
        public async Task should_send_the_movie_to_the_client_and_publish_a_movie_grabbed_event()
        {
            await Subject.DownloadReport(_remoteMovie, null);

            _client.Verify(c => c.Download(_remoteMovie, It.IsAny<IIndexer>()), Times.Once());

            Mocker.GetMock<IEventAggregator>().Verify(
                e => e.PublishEvent(It.Is<MovieGrabbedEvent>(g => g.Movie == _remoteMovie && g.DownloadId == "HASH" && g.DownloadClientId == 9 && g.DownloadClientName == "Engine")),
                Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<EpisodeGrabbedEvent>()), Times.Never());
        }

        [Test]
        public async Task should_pick_clients_by_the_tags_of_the_movie()
        {
            await Subject.DownloadReport(_remoteMovie, null);

            Mocker.GetMock<IProvideDownloadClient>()
                  .Verify(p => p.GetDownloadClients(DownloadProtocol.Torrent, It.IsAny<int>(), It.IsAny<bool>(), It.Is<HashSet<int>>(t => t.SequenceEqual(new[] { 4 }))), Times.Once());
        }

        [Test]
        public async Task should_ask_for_the_seed_configuration_of_the_release()
        {
            await Subject.DownloadReport(_remoteMovie, null);

            Mocker.GetMock<ISeedConfigProvider>().Verify(p => p.GetSeedConfiguration(_remoteMovie), Times.Once());
        }

        [Test]
        public void a_movie_has_no_season_pack_seed_goal()
        {
            var provider = new SeedConfigProvider(Mocker.GetMock<ICachedIndexerSettingsProvider>().Object);
            _remoteMovie.Release.IndexerId = 0;

            provider.GetSeedConfiguration(_remoteMovie).Should().BeNull();
        }
    }
}
