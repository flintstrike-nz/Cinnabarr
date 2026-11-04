using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineDownloadFilesServiceFixture : CoreTest<EngineDownloadFilesService>
    {
        private const string Hash = "abcdef0123456789abcdef0123456789abcdef01";

        private QBittorrentSettings _settings;
        private Mock<IQBittorrentProxy> _proxy;
        private EngineLink _link;
        private List<QBittorrentTorrent> _torrents;

        [SetUp]
        public void Setup()
        {
            _settings = new QBittorrentSettings();
            _proxy = new Mock<IQBittorrentProxy>();
            _torrents = new List<QBittorrentTorrent> { new QBittorrentTorrent { Hash = Hash, Ratio = 1.5f } };

            _link = new EngineLink
            {
                SavePath = @"C:\TV\Show\Season 01".AsOsAgnostic(),
                Files = new List<EngineLinkFile>
                {
                    new EngineLinkFile { Index = 1, IsSubtitle = true, VideoIndex = 0, FinalPath = "Show - S01E01.en.srt", State = EngineLinkFileState.Imported },
                    new EngineLinkFile { Index = 0, FinalPath = "Show - S01E01.mkv", State = EngineLinkFileState.Imported, ImportedAt = DateTime.UtcNow.AddDays(-3) },
                    new EngineLinkFile { Index = 2, FinalPath = "Show - S01E02.mkv", State = EngineLinkFileState.Pending }
                }
            };

            var client = new Mock<IDownloadClient>();
            client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 3, Settings = _settings });

            Mocker.GetMock<IProvideDownloadClient>().Setup(s => s.GetDownloadClients(false)).Returns(new[] { client.Object });
            Mocker.GetMock<IEngineClientRegistry>().Setup(s => s.IsEngineClient(3)).Returns(true);
            Mocker.GetMock<IQBittorrentProxySelector>().Setup(s => s.GetProxy(_settings, false)).Returns(_proxy.Object);
            Mocker.GetMock<IEngineLinkService>().Setup(s => s.Get(Hash.ToUpperInvariant())).Returns(() => _link);

            _proxy.Setup(p => p.GetTorrents(_settings)).Returns(() => _torrents);
            _proxy.Setup(p => p.GetTorrentFiles(Hash, _settings)).Returns(new List<QBittorrentTorrentFile>
            {
                new QBittorrentTorrentFile { Progress = 1 },
                new QBittorrentTorrentFile { Progress = 1 },
                new QBittorrentTorrentFile { Progress = 0.4 }
            });

            Mocker.GetMock<IEngineSeedingService>().Setup(s => s.GetRulesByFile(Hash.ToUpperInvariant(), _link)).Returns(new Dictionary<int, List<EngineSeedingRule>>
            {
                {
                    0, new List<EngineSeedingRule>
                    {
                        new EngineSeedingRule { Mode = EngineSeedingMode.Time, Minutes = 14 * 24 * 60, Source = "series" },
                        new EngineSeedingRule { Mode = EngineSeedingMode.Ratio, Ratio = 1.0, Source = "tracker" }
                    }
                }
            });
        }

        [Test]
        public void should_list_files_with_library_paths_progress_and_goals()
        {
            var result = Subject.Get(Hash.ToUpperInvariant());

            result.InEngine.Should().BeTrue();
            result.Ratio.Should().Be(1.5);

            // Each video is followed by its subtitles.
            result.Files.Select(f => f.Index).Should().Equal(0, 1, 2);
            result.Files[0].Path.Should().Be(Path.Combine(_link.SavePath, "Show - S01E01.mkv"));
            result.Files[2].Progress.Should().Be(0.4);

            result.Files[0].Goals.Should().HaveCount(2);
            result.Files[0].Goals[0].Source.Should().Be("series");
            result.Files[0].Goals[0].IsMet.Should().BeFalse();
            result.Files[0].Goals[1].IsMet.Should().BeTrue();
            result.Files[1].Goals.Should().BeEmpty();
        }

        [Test]
        public void should_still_list_files_when_the_engine_does_not_have_the_torrent()
        {
            _torrents.Clear();

            var result = Subject.Get(Hash);

            result.InEngine.Should().BeFalse();
            result.Ratio.Should().BeNull();
            result.Files.Should().OnlyContain(f => f.Progress == null);
            result.Files[0].Goals.Should().OnlyContain(g => !g.IsMet);
        }

        [Test]
        public void should_return_null_for_a_download_without_a_file_map()
        {
            _link.Files = null;

            Subject.Get(Hash).Should().BeNull();
            Subject.Get("123").Should().BeNull();
        }
    }
}
