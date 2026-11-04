using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineOrphanServiceFixture : CoreTest<EngineOrphanService>
    {
        private const string Owned = "1234567890abcdef1234567890abcdef12345678";
        private const string Gone = "abcdef0123456789abcdef0123456789abcdef01";

        private QBittorrentSettings _settings;
        private Mock<IQBittorrentProxy> _proxy;
        private List<QBittorrentTorrent> _torrents;
        private string _root;
        private Dictionary<string, DateTime> _files;

        [SetUp]
        public void Setup()
        {
            _settings = new QBittorrentSettings();
            _proxy = new Mock<IQBittorrentProxy>();
            _torrents = new List<QBittorrentTorrent> { new QBittorrentTorrent { Hash = Owned } };
            _root = @"C:\TV".AsOsAgnostic();
            _files = new Dictionary<string, DateTime>();

            var client = new Mock<IDownloadClient>();
            client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 3, Settings = _settings });

            Mocker.GetMock<IProvideDownloadClient>().Setup(s => s.GetDownloadClients(false)).Returns(new[] { client.Object });
            Mocker.GetMock<IEngineClientRegistry>().Setup(s => s.IsEngineClient(3)).Returns(true);
            Mocker.GetMock<IEngineSupervisor>().SetupGet(s => s.Status).Returns(EngineStatus.Running);
            Mocker.GetMock<IQBittorrentProxySelector>().Setup(s => s.GetProxy(_settings, false)).Returns(_proxy.Object);
            _proxy.Setup(p => p.GetTorrents(_settings)).Returns(() => _torrents);

            Mocker.GetMock<IConfigService>().SetupGet(c => c.EngineOrphanGraceDays).Returns(7);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder> { new RootFolder { Path = _root } });

            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(s => s.FolderExists(_root)).Returns(true);
            disk.Setup(s => s.GetFiles(_root, true)).Returns(() => _files.Keys.ToList());
            disk.Setup(s => s.FileGetLastWrite(It.IsAny<string>())).Returns<string>(p => _files[p]);
            disk.Setup(s => s.GetFileSize(It.IsAny<string>())).Returns(100);
        }

        private string GivenFile(string relativePath, int daysOld = 30)
        {
            var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            _files[path] = DateTime.UtcNow.AddDays(-daysOld);

            return path;
        }

        [Test]
        public void should_list_partial_files_of_torrents_no_longer_in_the_engine()
        {
            var pending = GivenFile("Show/Season 01/.Show - S01E01.mkv.abcdef01.pending");
            var incomplete = GivenFile("Show/Season 01/.Show - S01E02.mkv.abcdef01.pending.!qB");
            var parts = GivenFile($"Show/Season 01/.{Gone}.parts");
            var staged = GivenFile(".embarr-staging/abcdef01/0/.Show - S01E03.mkv.abcdef01.pending.!qB");

            Subject.Scan().Select(o => o.Path).Should().BeEquivalentTo(new[] { pending, incomplete, parts, staged });
        }

        [Test]
        public void should_not_list_files_of_torrents_still_in_the_engine()
        {
            GivenFile("Show/Season 01/.Show - S01E01.mkv.12345678.pending");
            GivenFile("Show/Season 01/.Show - S01E01.mkv.12345678.pending.!qB");
            GivenFile($"Show/Season 01/.{Owned}.parts");

            Subject.Scan().Should().BeEmpty();
        }

        [Test]
        public void should_match_part_files_named_by_a_v2_info_hash()
        {
            var v2 = new string('c', 64);
            var v2Only = new string('d', 64);
            _torrents.Add(new QBittorrentTorrent { Hash = new string('b', 40), InfoHashV1 = new string('b', 40), InfoHashV2 = v2 });

            // A v2-only torrent's id in the engine is its v2 hash cut to 40 digits.
            _torrents.Add(new QBittorrentTorrent { Hash = v2Only.Substring(0, 40) });
            GivenFile($"Show/.{v2}.parts");
            GivenFile($"Show/.{v2Only}.parts");

            Subject.Scan().Should().BeEmpty();
        }

        [Test]
        public void should_not_list_files_written_to_within_the_grace_period()
        {
            GivenFile("Show/Season 01/.Show - S01E01.mkv.abcdef01.pending.!qB", daysOld: 2);

            Subject.Scan().Should().BeEmpty();
        }

        [Test]
        public void should_ignore_every_other_file()
        {
            GivenFile("Show/Season 01/Show - S01E01.mkv");
            GivenFile("Show/Season 01/Show - S01E01.mkv.!qB");
            GivenFile("Show/Season 01/.hidden");
            GivenFile("Show/Season 01/Show - S01E01.mkv.abcdef01.pending");
            GivenFile($"Show/Season 01/{Gone}.parts");

            Subject.Scan().Should().BeEmpty();
        }

        [Test]
        public void should_not_scan_while_the_engine_is_not_running()
        {
            Mocker.GetMock<IEngineSupervisor>().SetupGet(s => s.Status).Returns(EngineStatus.Starting);
            GivenFile("Show/.Show - S01E01.mkv.abcdef01.pending");

            Assert.Throws<EngineOrphanException>(() => Subject.Scan());
        }

        [Test]
        public void should_not_scan_when_the_engine_cannot_list_its_torrents()
        {
            _proxy.Setup(p => p.GetTorrents(_settings)).Throws(new DownloadClientUnavailableException("down"));
            GivenFile("Show/.Show - S01E01.mkv.abcdef01.pending");

            Assert.Throws<EngineOrphanException>(() => Subject.Scan());

            Subject.Execute(new EngineOrphanCleanupCommand());

            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_only_list_orphans_until_a_list_is_approved()
        {
            GivenFile("Show/.Show - S01E01.mkv.abcdef01.pending");
            GivenFile($"Show/.{Gone}.parts");

            Subject.Execute(new EngineOrphanCleanupCommand());

            Subject.AwaitingReview.Should().Be(2);
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_orphans_once_approved()
        {
            var pending = GivenFile("Show/.Show - S01E01.mkv.abcdef01.pending");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EngineOrphanCleanupApproved).Returns(true);

            Subject.Execute(new EngineOrphanCleanupCommand());

            Subject.AwaitingReview.Should().Be(0);
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(pending), Times.Once());
        }

        [Test]
        public void should_delete_only_approved_files_that_are_still_orphans()
        {
            var approved = GivenFile(".embarr-staging/abcdef01/0/.Show - S01E01.mkv.abcdef01.pending.!qB");
            var notApproved = GivenFile("Show/.Show - S01E02.mkv.abcdef01.pending");
            var nowOwned = GivenFile("Show/.Show - S01E03.mkv.12345678.pending");
            Mocker.GetMock<IDiskProvider>().Setup(s => s.TryDeleteEmptyFolder(It.IsAny<string>())).Returns(true);

            var deleted = Subject.Approve(new[] { approved, nowOwned }, false);

            deleted.Select(d => d.Path).Should().Equal(approved);
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(approved), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(notApproved), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(nowOwned), Times.Never());
            Mocker.GetMock<IConfigService>().VerifySet(c => c.EngineOrphanCleanupApproved = false);
            Subject.AwaitingReview.Should().Be(1);

            // The torrent's and the file's staging folders go; the staging folder itself stays.
            var staging = Path.Combine(_root, ".embarr-staging");
            Mocker.GetMock<IDiskProvider>().Verify(s => s.TryDeleteEmptyFolder(Path.Combine(staging, "abcdef01", "0")), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.TryDeleteEmptyFolder(Path.Combine(staging, "abcdef01")), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.TryDeleteEmptyFolder(staging), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.TryDeleteEmptyFolder(_root), Times.Never());
        }

        [Test]
        public void should_turn_on_automatic_cleanup_when_approved()
        {
            Subject.Approve(Array.Empty<string>(), true);

            Mocker.GetMock<IConfigService>().VerifySet(c => c.EngineOrphanCleanupApproved = true);
            Subject.AwaitingReview.Should().Be(0);
        }
    }
}
