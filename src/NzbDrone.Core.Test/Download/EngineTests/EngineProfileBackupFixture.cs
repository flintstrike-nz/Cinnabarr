using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineProfileBackupFixture : CoreTest<EngineProfileBackup>
    {
        private string _profile;
        private string _zip;

        [SetUp]
        public void Setup()
        {
            _profile = Path.Combine(TempFolder, "engine");
            _zip = Path.Combine(TempFolder, "engine_profile.zip");

            Mocker.GetMock<IEngineClientRegistry>().SetupGet(r => r.EngineFolder).Returns(_profile);

            Write("cinnabarr/0123.json", "{\"link\":true}");
            Write("qBittorrent/config/qBittorrent.conf", "[BitTorrent]");
            Write("qBittorrent/data/BT_backup/0123.fastresume", "resume");
            Write("qBittorrent/data/logs/qbittorrent.log", "log");
            Write("qBittorrent/cache/x", "cache");
            Write("api.key", "secret");
            Write("engine.pid", "1");
        }

        private void Write(string relative, string content)
        {
            var path = Path.Combine(_profile, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private string[] Entries()
        {
            using var archive = ZipFile.OpenRead(_zip);

            return archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray();
        }

        [Test]
        public void should_back_up_the_settings_resume_data_and_link_records_but_not_the_key_or_logs()
        {
            Subject.CreateArchive(_zip).Should().BeTrue();

            Entries().Should().Equal(
                "cinnabarr/0123.json",
                "qBittorrent/config/qBittorrent.conf",
                "qBittorrent/data/BT_backup/0123.fastresume");
        }

        [Test]
        public void should_report_when_there_is_no_profile()
        {
            Directory.Delete(_profile, true);

            Subject.CreateArchive(_zip).Should().BeFalse();
            File.Exists(_zip).Should().BeFalse();
        }

        [Test]
        public void should_put_a_staged_profile_in_place_and_keep_the_old_one_and_its_key()
        {
            Subject.CreateArchive(_zip);
            Write("qBittorrent/config/qBittorrent.conf", "[Changed]");

            Subject.StageRestore(_zip);
            Subject.ApplyStagedRestore();

            File.ReadAllText(Path.Combine(_profile, "qBittorrent/config/qBittorrent.conf")).Should().Be("[BitTorrent]");
            File.ReadAllText(Path.Combine(_profile, "api.key")).Should().Be("secret");
            File.ReadAllText(Path.Combine(_profile + ".pre-restore", "qBittorrent/config/qBittorrent.conf")).Should().Be("[Changed]");
            Directory.Exists(_profile + ".restore").Should().BeFalse();
        }

        [Test]
        public void should_do_nothing_when_nothing_is_staged()
        {
            Subject.ApplyStagedRestore();

            File.ReadAllText(Path.Combine(_profile, "api.key")).Should().Be("secret");
            Directory.Exists(_profile + ".pre-restore").Should().BeFalse();
        }
    }
}
