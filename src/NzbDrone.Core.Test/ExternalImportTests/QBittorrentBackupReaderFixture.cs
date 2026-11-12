using System;
using System.IO;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ExternalImport;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ExternalImportTests
{
    [TestFixture]
    public class QBittorrentBackupReaderFixture : CoreTest<QBittorrentBackupReader>
    {
        private string _folder;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "cinnabarr-btbackup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_folder, "BT_backup"));
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        private static byte[] Bencode(string text) => Encoding.UTF8.GetBytes(text);

        private void GivenTorrent(string hash, string resume)
        {
            File.WriteAllBytes(Path.Combine(_folder, "BT_backup", hash + ".fastresume"), Bencode(resume));
            File.WriteAllBytes(Path.Combine(_folder, "BT_backup", hash + ".torrent"), Bencode("d4:infod4:name10:Some Moviee6:lengthi1ee"));
        }

        [Test]
        public void should_read_the_save_path_name_and_state_of_each_torrent()
        {
            GivenTorrent("aaaa", "d6:pausedi0e12:auto_managedi1e12:qBt-savePath12:/downloads/a8:qBt-name9:Named Onee");
            GivenTorrent("bbbb", "d6:pausedi1e12:auto_managedi0e9:save_path12:/downloads/be");

            var torrents = Subject.Read(_folder);

            torrents.Should().HaveCount(2);

            torrents[0].Hash.Should().Be("AAAA");
            torrents[0].SavePath.Should().Be("/downloads/a");
            torrents[0].Name.Should().Be("Named One");
            torrents[0].Stopped.Should().BeFalse();

            torrents[1].Hash.Should().Be("BBBB");
            torrents[1].SavePath.Should().Be("/downloads/b");
            torrents[1].Name.Should().Be("Some Movie");
            torrents[1].Stopped.Should().BeTrue();
        }

        [Test]
        public void should_find_the_backup_folder_from_the_profile_folder_or_the_folder_itself()
        {
            GivenTorrent("cccc", "d12:qBt-savePath2:/ne");

            Subject.Read(_folder).Should().ContainSingle();
            Subject.Read(Path.Combine(_folder, "BT_backup")).Should().ContainSingle();
        }

        [Test]
        public void should_skip_a_torrent_without_its_torrent_file_or_save_path_and_say_so_when_there_is_no_backup()
        {
            File.WriteAllBytes(Path.Combine(_folder, "BT_backup", "dddd.fastresume"), Bencode("d12:qBt-savePath2:/ne"));
            GivenTorrent("eeee", "d6:pausedi0ee");

            Assert.Throws<FileNotFoundException>(() => Subject.Read(Path.Combine(_folder, "nothing-here")));

            Subject.Read(_folder).Should().BeEmpty();
        }
    }
}
