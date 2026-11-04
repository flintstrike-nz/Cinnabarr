using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test.ApiTests
{
    // Embarr downloads only with its built-in engine (roadmap §4.4): download clients can be listed and edited, but not
    // added.
    [TestFixture]
    public class DownloadClientFixture : IntegrationTest
    {
        [Test]
        public void add_downloadclient_should_return_badrequest()
        {
            var schema = DownloadClients.Schema().First(v => v.Implementation == "QBittorrent");

            schema.Enable = true;
            schema.Name = "Test qBittorrent";

            DownloadClients.InvalidPost(schema);

            DownloadClients.All().Should().NotContain(c => c.Name == "Test qBittorrent");
        }

        [Test]
        public void get_all_downloadclients()
        {
            DownloadClients.All().Should().NotBeNull();
        }

        [Test]
        public void get_downloadclient_by_unknown_id_should_return_404()
        {
            DownloadClients.InvalidGet(1000000);
        }
    }
}
