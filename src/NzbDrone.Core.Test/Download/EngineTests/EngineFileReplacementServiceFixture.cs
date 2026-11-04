using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineFileReplacementServiceFixture : CoreTest<EngineFileReplacementService>
    {
        private const string Replacing = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
        private const string Owner = "1234567890ABCDEF1234567890ABCDEF12345678";

        private QBittorrentSettings _settings;
        private Mock<IQBittorrentProxy> _proxy;
        private Series _series;
        private string _seasonFolder;
        private EngineLink _owner;
        private EpisodeFile _oldFile;
        private List<QBittorrentTorrent> _torrents;

        [SetUp]
        public void Setup()
        {
            _settings = new QBittorrentSettings();
            _proxy = new Mock<IQBittorrentProxy>();
            _series = new Series { Id = 1, Path = @"C:\TV\Show".AsOsAgnostic() };
            _seasonFolder = Path.Combine(_series.Path, "Season 01");
            _oldFile = new EpisodeFile { Id = 7, RelativePath = Path.Combine("Season 01", "Show - S01E01 - HDTV.mkv") };

            _owner = new EngineLink
            {
                SavePath = _seasonFolder,
                Completed = true,
                Files = new List<EngineLinkFile>
                {
                    new EngineLinkFile { Index = 0, FinalPath = "Show - S01E01 - HDTV.mkv", PendingPath = ".Show - S01E01 - HDTV.mkv.12345678.pending", State = EngineLinkFileState.Imported },
                    new EngineLinkFile { Index = 1, IsSubtitle = true, VideoIndex = 0, FinalPath = "Show - S01E01 - HDTV.en.srt", PendingPath = ".Show - S01E01 - HDTV.en.srt.12345678.pending", State = EngineLinkFileState.Imported },
                    new EngineLinkFile { Index = 2, FinalPath = "Show - S01E02 - HDTV.mkv", PendingPath = ".Show - S01E02 - HDTV.mkv.12345678.pending", State = EngineLinkFileState.Imported }
                }
            };

            _torrents = new List<QBittorrentTorrent> { new QBittorrentTorrent { Hash = Owner.ToLowerInvariant() } };

            _proxy.Setup(p => p.GetTorrents(_settings)).Returns(() => _torrents);
            Mocker.GetMock<IEngineLinkService>().Setup(s => s.GetAll()).Returns(() => new Dictionary<string, EngineLink> { { Owner, _owner } });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetParentFolder(It.IsAny<string>())).Returns<string>(p => Path.GetDirectoryName(p));
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(true);
        }

        private void Detach(string finalPath)
        {
            Subject.Detach(_proxy.Object, _settings, Replacing, _series, new List<EpisodeFile> { _oldFile }, finalPath);
        }

        [Test]
        public void should_detach_the_old_file_and_its_subtitles_from_the_torrent_seeding_them()
        {
            Detach(Path.Combine(_seasonFolder, "Show - S01E01 - WEBDL.mkv"));

            _proxy.Verify(p => p.SetFilePriority(Owner.ToLowerInvariant(), new[] { 0, 1 }, 0, _settings), Times.Once());
            _owner.Files[0].State.Should().Be(EngineLinkFileState.Replaced);
            _owner.Files[1].State.Should().Be(EngineLinkFileState.Replaced);
            _owner.Files[2].State.Should().Be(EngineLinkFileState.Imported);
            Mocker.GetMock<IEngineLinkService>().Verify(s => s.Save(Owner, _owner), Times.Once());

            // The old subtitle goes to the recycle bin; the old video is left to the import, which replaces it.
            Mocker.GetMock<IRecycleBinProvider>().Verify(s => s.DeleteFile(Path.Combine(_seasonFolder, "Show - S01E01 - HDTV.en.srt"), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(s => s.DeleteFile(Path.Combine(_seasonFolder, "Show - S01E01 - HDTV.mkv"), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_find_the_torrents_holding_the_old_files()
        {
            Subject.FindHolders(Replacing, _series, new List<EpisodeFile> { _oldFile }).Should().Equal(Owner);
        }

        [Test]
        public void should_carry_over_rechecks_a_holder_still_owes()
        {
            _owner.Files[0].DetachedFrom = new List<string> { "EARLIER" };

            Subject.FindHolders(Replacing, _series, new List<EpisodeFile> { _oldFile }).Should().Equal(Owner, "EARLIER");
        }

        [Test]
        public void should_find_a_holder_whose_import_finished_but_was_not_saved()
        {
            _owner.Files[0].State = EngineLinkFileState.Renamed;

            Subject.FindHolders(Replacing, _series, new List<EpisodeFile> { _oldFile }).Should().Equal(Owner);
        }

        [Test]
        public void should_not_take_a_renamed_file_for_the_holder_of_the_file_it_replaces()
        {
            _owner.Files[0].State = EngineLinkFileState.Renamed;
            _owner.Files[0].ReplacesFileIds = new List<int> { 7 };

            Subject.FindHolders(Replacing, _series, new List<EpisodeFile> { _oldFile }).Should().BeEmpty();
        }

        [Test]
        public void should_reserve_files_another_download_is_replacing()
        {
            _owner.Files[2].State = EngineLinkFileState.Renamed;
            _owner.Files[2].ReplacesFileIds = new List<int> { 7 };

            Subject.IsReserved(_proxy.Object, _settings, Replacing, Path.Combine(_seasonFolder, "Show - S01E01 - WEBDL.mkv"), new List<EpisodeFile> { _oldFile }).Should().BeTrue();
        }

        [Test]
        public void should_not_let_a_download_removed_from_the_engine_reserve_anything()
        {
            _owner.Files[2].State = EngineLinkFileState.Renamed;
            _owner.Files[2].ReplacesFileIds = new List<int> { 7 };
            _torrents.Clear();

            Subject.IsReserved(_proxy.Object, _settings, Replacing, Path.Combine(_seasonFolder, "Show - S01E02 - HDTV.mkv"), new List<EpisodeFile> { _oldFile }).Should().BeFalse();
        }

        [Test]
        public void should_clear_rechecks_a_replaced_video_handed_over()
        {
            _owner.Files[0].DetachedFrom = new List<string> { "EARLIER" };

            Detach(Path.Combine(_seasonFolder, "Show - S01E01 - WEBDL.mkv"));

            _owner.Files[0].DetachedFrom.Should().BeNull();
        }

        [Test]
        public void should_reserve_a_final_path_another_download_renamed_to()
        {
            _owner.Files[2].State = EngineLinkFileState.Renamed;

            Subject.IsReserved(_proxy.Object, _settings, Replacing, Path.Combine(_seasonFolder, "Show - S01E02 - HDTV.mkv"), new List<EpisodeFile> { _oldFile }).Should().BeTrue();
            Subject.IsReserved(_proxy.Object, _settings, Replacing, Path.Combine(_seasonFolder, "Show - S01E01 - WEBDL.mkv"), new List<EpisodeFile> { _oldFile }).Should().BeFalse();
        }

        [Test]
        public void should_detach_and_clean_up_subtitles_that_were_not_published_yet()
        {
            _owner.Files[1].State = EngineLinkFileState.Pending;
            _owner.Files[1].PendingPath = ".Show - S01E01 - HDTV.en.srt.12345678.pending";

            Detach(Path.Combine(_seasonFolder, "Show - S01E01 - WEBDL.mkv"));

            _proxy.Verify(p => p.SetFilePriority(Owner.ToLowerInvariant(), new[] { 0, 1 }, 0, _settings), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(Path.Combine(_seasonFolder, ".Show - S01E01 - HDTV.en.srt.12345678.pending")), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(Path.Combine(_seasonFolder, ".Show - S01E01 - HDTV.en.srt.12345678.pending.!qB")), Times.Once());
            _owner.Files[1].State.Should().Be(EngineLinkFileState.Replaced);
        }

        [Test]
        public void should_move_an_old_file_holding_the_final_path_to_the_recycle_bin()
        {
            Detach(Path.Combine(_seasonFolder, "Show - S01E01 - HDTV.mkv"));

            Mocker.GetMock<IRecycleBinProvider>().Verify(s => s.DeleteFile(Path.Combine(_seasonFolder, "Show - S01E01 - HDTV.mkv"), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_only_record_the_replacement_when_the_old_torrent_is_gone()
        {
            _torrents.Clear();

            Detach(Path.Combine(_seasonFolder, "Show - S01E01 - WEBDL.mkv"));

            _proxy.Verify(p => p.SetFilePriority(It.IsAny<string>(), It.IsAny<IEnumerable<int>>(), It.IsAny<int>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _owner.Files[0].State.Should().Be(EngineLinkFileState.Replaced);
        }

        [Test]
        public void should_leave_files_no_torrent_holds_to_the_import()
        {
            _oldFile.RelativePath = Path.Combine("Season 01", "Imported Elsewhere.mkv");

            Subject.FindHolders(Replacing, _series, new List<EpisodeFile> { _oldFile }).Should().BeEmpty();
            Detach(Path.Combine(_seasonFolder, "Show - S01E01 - WEBDL.mkv"));

            Mocker.GetMock<IEngineLinkService>().Verify(s => s.Save(It.IsAny<string>(), It.IsAny<EngineLink>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(s => s.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_recheck_only_torrents_still_in_the_engine()
        {
            Subject.Recheck(_proxy.Object, _settings, new[] { Owner, Replacing });

            _proxy.Verify(p => p.RecheckTorrent(Owner.ToLowerInvariant(), _settings), Times.Once());
            _proxy.Verify(p => p.RecheckTorrent(Replacing.ToLowerInvariant(), _settings), Times.Never());
        }
    }
}
