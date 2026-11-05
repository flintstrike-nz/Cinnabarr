using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerEngine.IndexerVersions;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.IndexerEngineTests.IndexerVersions
{
    [TestFixture]
    public class IndexerDefinitionUpdateServiceFixture : CoreTest<IndexerDefinitionUpdateService>
    {
        private const string Url = "https://example.test/Indexers/master.zip";

        private string _appData;
        private string _startUp;
        private byte[] _zip;

        [SetUp]
        public void Setup()
        {
            _appData = GetTempFilePath();
            _startUp = GetTempFilePath();
            Directory.CreateDirectory(_appData);
            Directory.CreateDirectory(_startUp);

            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.AppDataFolder).Returns(_appData);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.StartUpFolder).Returns(_startUp);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.IndexerDefinitionsUrl).Returns(Url);

            Mocker.GetMock<IDiskProvider>().Setup(d => d.CreateFolder(It.IsAny<string>())).Callback<string>(p => Directory.CreateDirectory(p));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);

            Mocker.GetMock<IHttpClient>()
                .Setup(h => h.DownloadFile(Url, It.IsAny<string>()))
                .Callback<string, string>((_, path) => File.WriteAllBytes(path, _zip));
        }

        private static string Yml(string name)
        {
            return $"---\nid: {name.ToLowerInvariant()}\nname: {name}\ndescription: test\nlanguage: en-US\ntype: public\nencoding: UTF-8\nlinks:\n  - https://example.test/\ncaps:\n  categorymappings: []\n  modes:\n    search: [q]\nsearch:\n  paths:\n    - path: /\n  rows:\n    selector: tr\n  fields: {{}}\n";
        }

        private void GivenZip(params (string Path, string Content)[] entries)
        {
            using var stream = new MemoryStream();

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                foreach (var (path, content) in entries)
                {
                    using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                    writer.Write(content);
                }
            }

            _zip = stream.ToArray();
        }

        private void Update()
        {
            Subject.Execute(new IndexerDefinitionUpdateCommand());
        }

        [Test]
        public void should_keep_the_highest_version_up_to_the_supported_one_for_each_indexer()
        {
            GivenZip(
                ("Indexers-master/definitions/v10/alpha.yml", Yml("Alpha10")),
                ("Indexers-master/definitions/v12/alpha.yml", Yml("Alpha12")),
                ("Indexers-master/definitions/v13/alpha.yml", Yml("Alpha13")),
                ("Indexers-master/definitions/v11/beta.yml", Yml("Beta11")),
                ("Indexers-master/definitions/v13/gamma.yml", Yml("Gamma13")),
                ("Indexers-master/README.md", "readme"));

            Update();

            Subject.All().Select(d => d.Name).Should().BeEquivalentTo("Alpha12", "Beta11");
            Subject.GetCachedDefinition("alpha").Name.Should().Be("Alpha12");
        }

        [Test]
        public void should_not_use_blocklisted_definitions_which_are_native_indexers()
        {
            GivenZip(("r/definitions/v12/hdbits.yml", Yml("HDBits")), ("r/definitions/v12/alpha.yml", Yml("Alpha")));

            Update();

            Subject.All().Select(d => d.File).Should().Equal("alpha");
        }

        [Test]
        public void should_never_write_outside_the_definitions_folder()
        {
            GivenZip(
                ("r/definitions/v12/../../../evil.yml", Yml("Evil")),
                ("r/definitions/v12/sub/dir.yml", Yml("Nested")),
                ("r/definitions/v12/ok.yml", Yml("Ok")));

            Update();

            File.Exists(Path.Combine(_appData, "evil.yml")).Should().BeFalse();
            Directory.GetFiles(_appData, "*.yml", SearchOption.AllDirectories).Select(Path.GetFileName).Should().Equal("ok.yml");
        }

        [Test]
        public void should_keep_existing_definitions_when_the_download_has_none()
        {
            GivenZip(("r/definitions/v12/alpha.yml", Yml("Alpha")));
            Update();

            GivenZip(("r/README.md", "no definitions here"));
            Update();

            Subject.All().Select(d => d.Name).Should().Equal("Alpha");

            // Whether the failure is logged depends on the test run's logging state, so it isn't asserted.
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_use_the_bundled_definitions_when_there_are_none_and_the_download_fails()
        {
            Directory.CreateDirectory(Path.Combine(_startUp, "Definitions"));
            File.WriteAllText(Path.Combine(_startUp, "Definitions", "bundled.yml"), Yml("Bundled"));
            Mocker.GetMock<IHttpClient>().Setup(h => h.DownloadFile(Url, It.IsAny<string>())).Throws(new HttpException(new HttpRequest(Url), new HttpResponse(new HttpRequest(Url), new HttpHeader(), string.Empty, System.Net.HttpStatusCode.NotFound)));

            Update();

            Subject.All().Select(d => d.Name).Should().Equal("Bundled");
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_only_contact_the_configured_source()
        {
            GivenZip(("r/definitions/v12/alpha.yml", Yml("Alpha")));

            Update();
            Subject.All();

            Mocker.GetMock<IHttpClient>().Verify(h => h.DownloadFile(Url, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(h => h.DownloadFile(It.Is<string>(u => u != Url), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(h => h.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_say_when_a_definition_does_not_exist()
        {
            GivenZip(("r/definitions/v12/alpha.yml", Yml("Alpha")));
            Update();

            Assert.Catch(() => Subject.GetCachedDefinition("missing"));
        }
    }
}
