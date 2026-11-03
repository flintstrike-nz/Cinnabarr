using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineSupervisorFixture : CoreTest<EngineSupervisor>
    {
        private string _appData;
        private string _keyFile;

        [SetUp]
        public void Setup()
        {
            _appData = @"C:\Test\AppData".AsOsAgnostic();
            _keyFile = Path.Combine(_appData, "engine", "api.key");

            Mocker.GetMock<IAppFolderInfo>()
                .SetupGet(s => s.AppDataFolder)
                .Returns(_appData);

            Mocker.GetMock<IAppFolderInfo>()
                .SetupGet(s => s.StartUpFolder)
                .Returns(@"C:\Test\App".AsOsAgnostic());

            Mocker.GetMock<IDownloadClientFactory>()
                .Setup(s => s.All())
                .Returns(new List<DownloadClientDefinition>());
        }

        [Test]
        public void generated_api_key_should_be_valid_and_unique()
        {
            var first = EngineSupervisor.GenerateApiKey();
            var second = EngineSupervisor.GenerateApiKey();

            first.Should().StartWith("qbt_").And.HaveLength(32);
            EngineSupervisor.IsValidApiKey(first).Should().BeTrue();
            second.Should().NotBe(first);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("qbt_short")]
        [TestCase("abc_AAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("qbt_AAAAAAAAAAAAAAAAAAAAAAAAAA!A")]
        public void invalid_api_keys_should_be_rejected(string key)
        {
            EngineSupervisor.IsValidApiKey(key).Should().BeFalse();
        }

        [Test]
        public void should_create_and_store_api_key_when_missing()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(_keyFile))
                .Returns(false);

            var key = Subject.GetOrCreateApiKey();

            EngineSupervisor.IsValidApiKey(key).Should().BeTrue();
            Mocker.GetMock<IDiskProvider>().Verify(v => v.WriteAllText(_keyFile, key), Times.Once());
        }

        [Test]
        public void should_reuse_valid_stored_api_key()
        {
            var stored = EngineSupervisor.GenerateApiKey();

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(_keyFile)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.ReadAllText(_keyFile)).Returns(stored + "\n");

            Subject.GetOrCreateApiKey().Should().Be(stored);
            Mocker.GetMock<IDiskProvider>().Verify(v => v.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_replace_invalid_stored_api_key()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(_keyFile)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.ReadAllText(_keyFile)).Returns("garbage");

            var key = Subject.GetOrCreateApiKey();

            key.Should().NotBe("garbage");
            EngineSupervisor.IsValidApiKey(key).Should().BeTrue();
            Mocker.GetMock<IDiskProvider>().Verify(v => v.WriteAllText(_keyFile, key), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_find_engine_when_binary_is_missing()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.ResolveEnginePath().Should().BeNull();
        }

        [Test]
        public void should_report_not_found_and_publish_status_when_engine_is_missing()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.Handle(new ApplicationStartedEvent());

            Subject.Status.Should().Be(EngineStatus.NotFound);
            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<EngineStatusChangedEvent>(e => e.Status == EngineStatus.NotFound)), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_find_engine_next_to_app()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(It.Is<string>(p => Path.GetFileNameWithoutExtension(p) == "embarr-engine")))
                .Returns(true);

            var path = Subject.ResolveEnginePath();

            path.Should().NotBeNull();
            Path.GetDirectoryName(path).Should().Be(@"C:\Test\App".AsOsAgnostic());
        }

        [Test]
        public void should_add_built_in_download_client_when_missing()
        {
            var key = EngineSupervisor.GenerateApiKey();

            DownloadClientDefinition created = null;

            Mocker.GetMock<IDownloadClientFactory>()
                .Setup(s => s.Create(It.IsAny<DownloadClientDefinition>()))
                .Callback<DownloadClientDefinition>(d => created = d)
                .Returns<DownloadClientDefinition>(d => d);

            Subject.EnsureDownloadClient(51234, key);

            created.Should().NotBeNull();
            created.Name.Should().Be(EngineSupervisor.EngineDefinitionName);
            created.Implementation.Should().Be(nameof(QBittorrent));
            created.ConfigContract.Should().Be(nameof(QBittorrentSettings));
            created.Protocol.Should().Be(DownloadProtocol.Torrent);
            created.Enable.Should().BeTrue();

            var settings = (QBittorrentSettings)created.Settings;
            settings.Host.Should().Be("127.0.0.1");
            settings.Port.Should().Be(51234);
            settings.ApiKey.Should().Be(key);
            settings.UseSsl.Should().BeFalse();

            Mocker.GetMock<IDownloadClientFactory>().Verify(v => v.Update(It.IsAny<DownloadClientDefinition>()), Times.Never());
        }

        [Test]
        public void should_update_existing_built_in_download_client_and_keep_other_settings()
        {
            var existing = new DownloadClientDefinition
            {
                Id = 7,
                Name = EngineSupervisor.EngineDefinitionName,
                Implementation = nameof(QBittorrent),
                ConfigContract = nameof(QBittorrentSettings),
                Enable = false,
                Settings = new QBittorrentSettings { Host = "10.0.0.5", Port = 8080, Username = "admin", Password = "secret", TvCategory = "custom" }
            };

            Mocker.GetMock<IDownloadClientFactory>()
                .Setup(s => s.All())
                .Returns(new List<DownloadClientDefinition> { existing });

            var key = EngineSupervisor.GenerateApiKey();

            Subject.EnsureDownloadClient(40000, key);

            Mocker.GetMock<IDownloadClientFactory>().Verify(v => v.Create(It.IsAny<DownloadClientDefinition>()), Times.Never());
            Mocker.GetMock<IDownloadClientFactory>().Verify(v => v.Update(existing), Times.Once());

            var settings = (QBittorrentSettings)existing.Settings;
            existing.Enable.Should().BeTrue();
            settings.Host.Should().Be("127.0.0.1");
            settings.Port.Should().Be(40000);
            settings.ApiKey.Should().Be(key);
            settings.Username.Should().BeEmpty();
            settings.Password.Should().BeEmpty();
            settings.TvCategory.Should().Be("custom");
        }
    }
}
