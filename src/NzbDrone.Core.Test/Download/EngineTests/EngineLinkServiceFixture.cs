using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineLinkServiceFixture : CoreTest<EngineLinkService>
    {
        private const string Hash = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
        private const int EngineClientId = 3;

        private Dictionary<string, string> _files;
        private string _engineFolder;
        private DownloadClientDefinition _client;

        [SetUp]
        public void Setup()
        {
            _files = new Dictionary<string, string>();
            _engineFolder = Path.Combine("data".AsOsAgnostic(), "engine");
            _client = new DownloadClientDefinition { Id = EngineClientId, Name = "Embarr Engine", Implementation = "QBittorrent" };

            Mocker.GetMock<IEngineClientRegistry>().SetupGet(s => s.EngineFolder).Returns(_engineFolder);
            Mocker.GetMock<IEngineClientRegistry>().Setup(s => s.IsEngineClient(EngineClientId)).Returns(true);

            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, contents) => _files[path] = contents);
            disk.Setup(d => d.ReplaceFile(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((source, destination) =>
                {
                    _files[destination] = _files[source];
                    _files.Remove(source);
                });
            disk.Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(path => _files.ContainsKey(path));
            disk.Setup(d => d.ReadAllText(It.IsAny<string>())).Returns<string>(path => _files[path]);
        }

        private string LinkPath => Path.Combine(_engineFolder, "embarr", Hash.ToLowerInvariant() + ".json");

        private EpisodeGrabbedEvent GivenGrab(int downloadClientId = EngineClientId, string downloadId = Hash)
        {
            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series { Id = 99, TvdbId = 1234 },
                Episodes = new List<Episode>
                {
                    new Episode { Id = 1, SeasonNumber = 1, EpisodeNumber = 2 },
                    new Episode { Id = 2, SeasonNumber = 1, EpisodeNumber = 3 }
                },
                Release = new ReleaseInfo { Title = "Series.S01E02E03.1080p.WEB-DL-Group", Indexer = "Indexer" },
                ParsedEpisodeInfo = new ParsedEpisodeInfo { Quality = new QualityModel(Quality.Bluray1080p, new Revision(2)) }
            };

            return new EpisodeGrabbedEvent(remoteEpisode) { DownloadClientId = downloadClientId, DownloadId = downloadId };
        }

        private void GivenSeriesInLibrary()
        {
            Mocker.GetMock<ISeriesService>().Setup(s => s.FindByTvdbId(1234)).Returns(new Series { Id = 7, Title = "Series" });
            Mocker.GetMock<IEpisodeService>().Setup(s => s.FindEpisode(7, 1, 2)).Returns(new Episode { Id = 21 });
            Mocker.GetMock<IEpisodeService>().Setup(s => s.FindEpisode(7, 1, 3)).Returns(new Episode { Id = 22 });
        }

        [Test]
        public void should_write_link_with_external_ids_for_engine_grab()
        {
            Subject.Handle(GivenGrab());

            _files.Should().ContainKey(LinkPath);
            _files.Should().HaveCount(1);

            var link = Json.Deserialize<EngineLink>(_files[LinkPath]);

            link.Version.Should().Be(EngineLink.CurrentVersion);
            link.MediaType.Should().Be(EngineLinkService.SeriesMediaType);
            link.TvdbId.Should().Be(1234);
            link.Episodes.Should().HaveCount(2);
            link.Episodes[1].SeasonNumber.Should().Be(1);
            link.Episodes[1].EpisodeNumber.Should().Be(3);
            link.SourceTitle.Should().Be("Series.S01E02E03.1080p.WEB-DL-Group");
            link.Indexer.Should().Be("Indexer");
        }

        [Test]
        public void should_not_write_link_for_other_clients()
        {
            Subject.Handle(GivenGrab(downloadClientId: 4));

            _files.Should().BeEmpty();
        }

        [TestCase("../../escape")]
        [TestCase("SABnzbd_nzo_12345")]
        [TestCase("")]
        [TestCase(null)]
        public void should_not_write_link_unless_download_id_is_an_info_hash(string downloadId)
        {
            Subject.Handle(GivenGrab(downloadId: downloadId));

            _files.Should().BeEmpty();
        }

        [Test]
        public void should_not_fail_grab_if_link_cannot_be_written()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new IOException("disk full"));

            Subject.Handle(GivenGrab());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_restore_grab_history_from_link()
        {
            Subject.Handle(GivenGrab());
            GivenSeriesInLibrary();

            var inserted = new List<EpisodeHistory>();
            Mocker.GetMock<IHistoryRepository>()
                .Setup(r => r.InsertMany(It.IsAny<IList<EpisodeHistory>>()))
                .Callback<IList<EpisodeHistory>>(inserted.AddRange);

            Subject.TryRestore(_client, Hash).Should().BeTrue();

            Mocker.GetMock<IHistoryRepository>().Verify(r => r.InsertMany(It.IsAny<IList<EpisodeHistory>>()), Times.Once());
            Mocker.GetMock<IHistoryRepository>().Verify(r => r.Insert(It.IsAny<EpisodeHistory>()), Times.Never());
            inserted.Should().HaveCount(2);
            inserted.Should().OnlyContain(h => h.EventType == EpisodeHistoryEventType.Grabbed &&
                                               h.DownloadId == Hash &&
                                               h.SeriesId == 7 &&
                                               h.SourceTitle == "Series.S01E02E03.1080p.WEB-DL-Group" &&
                                               h.Data[EngineLinkService.RestoredFromKey] == "EngineSidecar" &&
                                               h.Data["DownloadClientName"] == "Embarr Engine");
            inserted.Should().Contain(h => h.EpisodeId == 21).And.Contain(h => h.EpisodeId == 22);
            inserted[0].Quality.Quality.Should().Be(Quality.Bluray1080p);

            Mocker.GetMock<IDownloadHistoryRepository>()
                .Verify(r => r.Insert(It.Is<DownloadHistory>(h => h.EventType == DownloadHistoryEventType.DownloadGrabbed && h.DownloadId == Hash && h.SeriesId == 7 && h.DownloadClientId == EngineClientId)), Times.Once());
        }

        [Test]
        public void should_restore_grabbed_quality_rather_than_parse_it()
        {
            Subject.Handle(GivenGrab());
            GivenSeriesInLibrary();

            var inserted = new List<EpisodeHistory>();
            Mocker.GetMock<IHistoryRepository>()
                .Setup(r => r.InsertMany(It.IsAny<IList<EpisodeHistory>>()))
                .Callback<IList<EpisodeHistory>>(inserted.AddRange);

            Subject.TryRestore(_client, Hash).Should().BeTrue();

            inserted.Should().OnlyContain(h => h.Quality.Quality == Quality.Bluray1080p && h.Quality.Revision.Version == 2);
        }

        [Test]
        public void should_parse_quality_from_title_for_links_without_quality()
        {
            var link = new EngineLink { MediaType = EngineLinkService.SeriesMediaType, TvdbId = 1234, SourceTitle = "Series.S01E02.1080p.WEB-DL-Group" };
            link.Episodes.Add(new EngineLinkEpisode { SeasonNumber = 1, EpisodeNumber = 2 });
            _files[LinkPath] = link.ToJson();
            GivenSeriesInLibrary();

            var inserted = new List<EpisodeHistory>();
            Mocker.GetMock<IHistoryRepository>()
                .Setup(r => r.InsertMany(It.IsAny<IList<EpisodeHistory>>()))
                .Callback<IList<EpisodeHistory>>(inserted.AddRange);

            Subject.TryRestore(_client, Hash).Should().BeTrue();

            inserted.Should().ContainSingle().Which.Quality.Quality.Should().Be(Quality.WEBDL1080p);
        }

        [Test]
        public void should_not_restore_if_series_is_not_in_library()
        {
            Subject.Handle(GivenGrab());

            Subject.TryRestore(_client, Hash).Should().BeFalse();

            Mocker.GetMock<IHistoryRepository>().Verify(r => r.InsertMany(It.IsAny<IList<EpisodeHistory>>()), Times.Never());
        }

        [Test]
        public void should_not_restore_if_no_episodes_exist()
        {
            Subject.Handle(GivenGrab());
            Mocker.GetMock<ISeriesService>().Setup(s => s.FindByTvdbId(1234)).Returns(new Series { Id = 7, Title = "Series" });

            Subject.TryRestore(_client, Hash).Should().BeFalse();

            Mocker.GetMock<IHistoryRepository>().Verify(r => r.InsertMany(It.IsAny<IList<EpisodeHistory>>()), Times.Never());
        }

        [Test]
        public void should_not_restore_part_of_a_link_if_some_episodes_are_missing()
        {
            Subject.Handle(GivenGrab());
            Mocker.GetMock<ISeriesService>().Setup(s => s.FindByTvdbId(1234)).Returns(new Series { Id = 7, Title = "Series" });
            Mocker.GetMock<IEpisodeService>().Setup(s => s.FindEpisode(7, 1, 2)).Returns(new Episode { Id = 21 });

            Subject.TryRestore(_client, Hash).Should().BeFalse();

            Mocker.GetMock<IHistoryRepository>().Verify(r => r.InsertMany(It.IsAny<IList<EpisodeHistory>>()), Times.Never());
        }

        [Test]
        public void should_replace_existing_link_in_one_step()
        {
            Subject.Handle(GivenGrab());
            Subject.Handle(GivenGrab());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.ReplaceFile(LinkPath + ".tmp", LinkPath), Times.Exactly(2));
            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_restore_without_link()
        {
            GivenSeriesInLibrary();

            Subject.TryRestore(_client, Hash).Should().BeFalse();

            Mocker.GetMock<IHistoryRepository>().Verify(r => r.InsertMany(It.IsAny<IList<EpisodeHistory>>()), Times.Never());
        }

        [Test]
        public void should_not_restore_from_unreadable_link()
        {
            _files[LinkPath] = "{ not json";
            GivenSeriesInLibrary();

            Subject.TryRestore(_client, Hash).Should().BeFalse();

            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IHistoryRepository>().Verify(r => r.InsertMany(It.IsAny<IList<EpisodeHistory>>()), Times.Never());
        }

        [Test]
        public void should_remove_only_empty_staging_folders_of_the_torrent()
        {
            var root = @"C:\TV".AsOsAgnostic();
            var torrentFolder = Path.Combine(root, ".embarr-staging", "abcdef01");
            var link = new EngineLink
            {
                SavePath = root,
                Files = new List<EngineLinkFile>
                {
                    new EngineLinkFile { PendingPath = ".embarr-staging/abcdef01/0/.Show - S01E01.mkv.abcdef01.pending" },
                    new EngineLinkFile { PendingPath = ".embarr-staging/abcdef01/1/.Show - S01E01.en.srt.abcdef01.pending" }
                }
            };

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns(true);

            Subject.RemoveEmptyStagingFolder(link);

            // Each file's folder, then the torrent's: never recursively, so a file appearing meanwhile is kept.
            Mocker.GetMock<IDiskProvider>().Verify(d => d.TryDeleteEmptyFolder(Path.Combine(torrentFolder, "0")), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.TryDeleteEmptyFolder(Path.Combine(torrentFolder, "1")), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.TryDeleteEmptyFolder(torrentFolder), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
