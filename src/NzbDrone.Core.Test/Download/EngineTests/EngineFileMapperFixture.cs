using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineFileMapperFixture : CoreTest<EngineFileMapper>
    {
        private const string Hash = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";

        private Series _series;
        private List<Episode> _episodes;
        private string _seasonFolder;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 1, Title = "Show", Path = @"C:\TV\Show".AsOsAgnostic() };
            _seasonFolder = Path.Combine(_series.Path, "Season 01");
            _episodes = new List<Episode>
            {
                new Episode { Id = 11, SeasonNumber = 1, EpisodeNumber = 1 },
                new Episode { Id = 12, SeasonNumber = 1, EpisodeNumber = 2 }
            };

            Mocker.GetMock<IBuildFileNames>()
                .Setup(s => s.BuildFilePath(It.IsAny<List<Episode>>(), _series, It.IsAny<EpisodeFile>(), It.IsAny<string>(), null, null))
                .Returns((List<Episode> episodes, Series series, EpisodeFile file, string extension, NamingConfig naming, List<CustomFormat> formats) =>
                    Path.Combine(_seasonFolder, $"Show - S01E{episodes.First().EpisodeNumber:00}{extension}"));

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.GetEpisodes(It.IsAny<ParsedEpisodeInfo>(), _series, true, null))
                .Returns((ParsedEpisodeInfo info, Series series, bool sceneSource, NzbDrone.Core.IndexerSearch.Definitions.SearchCriteriaBase criteria) =>
                    _episodes.Where(e => info.EpisodeNumbers.Contains(e.EpisodeNumber)).ToList());
        }

        private EngineFileMap Map(params (string Name, long Size)[] files)
        {
            return Subject.Map(Hash, _series, _episodes, new QualityModel(Quality.HDTV720p), "Show.S01.720p.HDTV-Grp", files.Select(f => new QBittorrentTorrentFile { Name = f.Name, Size = f.Size }).ToList());
        }

        [Test]
        public void should_map_single_video_to_all_grabbed_episodes_with_a_hidden_pending_name()
        {
            var map = Map(("Show.S01E01E02.720p/Show.S01E01E02.720p.mkv", 1000));

            map.SavePath.Should().Be(_seasonFolder);
            map.Files.Should().ContainSingle();
            map.Files[0].Index.Should().Be(0);
            map.Files[0].Episodes.Should().HaveCount(2);
            map.Files[0].FinalPath.Should().Be("Show - S01E01.mkv");
            map.Files[0].PendingPath.Should().Be(".Show - S01E01.mkv.abcdef01.pending");
            map.Files[0].State.Should().Be(EngineLinkFileState.Pending);
        }

        [Test]
        public void should_map_each_video_of_a_pack_by_its_name()
        {
            var map = Map(("Show.S01/Show.S01E01.mkv", 1000), ("Show.S01/Show.S01E02.mkv", 1000));

            map.Files.Select(f => f.FinalPath).Should().Equal("Show - S01E01.mkv", "Show - S01E02.mkv");
            map.Files.Select(f => f.Episodes.Single().EpisodeNumber).Should().Equal(1, 2);
        }

        [Test]
        public void should_skip_samples_unmatched_videos_and_smaller_duplicates()
        {
            var map = Map(("Show.S01/Show.S01E01.mkv", 1000),
                          ("Show.S01/Sample/Show.S01E01.sample.mkv", 10),
                          ("Show.S01/Show.S01E05.mkv", 1000),
                          ("Show.S01/Show.S01E02.mkv", 500),
                          ("Show.S01/Show.S01E02.REPACK.mkv", 900));

            map.Files.Select(f => f.Index).Should().BeEquivalentTo(new[] { 0, 4 });
        }

        [Test]
        public void should_never_map_two_files_to_the_same_episode()
        {
            var map = Map(("Show.S01/Show.S01E01.mkv", 2000), ("Show.S01/Show.S01E01E02.mkv", 1000));

            map.Files.Should().ContainSingle();
            map.Files[0].Index.Should().Be(1);
            map.Files[0].Episodes.Should().HaveCount(2);
        }

        [Test]
        public void should_return_null_when_no_video_matches_the_grabbed_episodes()
        {
            Map(("Show.S01/Show.S01E07.mkv", 1000), ("Show.S01/Show.S01E08.mkv", 1000)).Should().BeNull();
        }

        [Test]
        public void should_attach_subtitles_to_their_video_with_its_final_name()
        {
            var map = Map(("Show.S01/Show.S01E01.mkv", 1000),
                          ("Show.S01/Show.S01E02.mkv", 1000),
                          ("Show.S01/Show.S01E02.en.srt", 10),
                          ("Show.S01/Subs/other.srt", 10));

            var subtitle = map.Files.Single(f => f.IsSubtitle);

            subtitle.Index.Should().Be(2);
            subtitle.VideoIndex.Should().Be(1);
            subtitle.FinalPath.Should().Be("Show - S01E02.en.srt");
            subtitle.PendingPath.Should().Be(".Show - S01E02.en.srt.abcdef01.pending");
        }

        [Test]
        public void should_save_into_the_lowest_folder_that_holds_every_file()
        {
            Mocker.GetMock<IBuildFileNames>()
                .Setup(s => s.BuildFilePath(It.IsAny<List<Episode>>(), _series, It.IsAny<EpisodeFile>(), It.IsAny<string>(), null, null))
                .Returns((List<Episode> episodes, Series series, EpisodeFile file, string extension, NamingConfig naming, List<CustomFormat> formats) =>
                    Path.Combine(_series.Path, $"Season 0{episodes.First().EpisodeNumber}", $"Show - E{episodes.First().EpisodeNumber}{extension}"));

            var map = Map(("Show/Show.S01E01.mkv", 1000), ("Show/Show.S01E02.mkv", 1000));

            map.SavePath.Should().Be(_series.Path);
            map.Files.Select(f => f.PendingPath).Should().Equal("Season 01/.Show - E1.mkv.abcdef01.pending", "Season 02/.Show - E2.mkv.abcdef01.pending");
        }

        [Test]
        public void should_name_each_pack_file_after_its_own_name()
        {
            Map(("Show.S01/Show.S01E01.mkv", 1000), ("Show.S01/Show.S01E02.mkv", 1000));

            Mocker.GetMock<IBuildFileNames>()
                .Verify(s => s.BuildFilePath(It.IsAny<List<Episode>>(), _series, It.Is<EpisodeFile>(f => f.SceneName == "Show.S01E02" && f.RelativePath == "Show.S01E02.mkv"), ".mkv", null, null), Times.Once());
        }

        [Test]
        public void should_name_a_single_file_after_the_release()
        {
            Map(("Show.S01E01E02.720p/Show.S01E01E02.720p.mkv", 1000));

            Mocker.GetMock<IBuildFileNames>()
                .Verify(s => s.BuildFilePath(It.IsAny<List<Episode>>(), _series, It.Is<EpisodeFile>(f => f.SceneName == "Show.S01.720p.HDTV-Grp"), ".mkv", null, null), Times.Once());
        }

        [Test]
        public void should_keep_pending_names_within_the_file_name_limit()
        {
            Mocker.GetMock<IBuildFileNames>()
                .Setup(s => s.BuildFilePath(It.IsAny<List<Episode>>(), _series, It.IsAny<EpisodeFile>(), It.IsAny<string>(), null, null))
                .Returns((List<Episode> episodes, Series series, EpisodeFile file, string extension, NamingConfig naming, List<CustomFormat> formats) =>
                    Path.Combine(_seasonFolder, new string('a', 250) + extension));

            var file = Map(("Show.S01E01.mkv", 1000)).Files.Single();

            (System.Text.Encoding.UTF8.GetByteCount(file.PendingPath) + 4).Should().BeLessOrEqualTo(255);
            file.FinalPath.Should().EndWith(".mkv");
        }

        [Test]
        public void should_give_each_torrent_its_own_pending_name()
        {
            var other = Subject.Map("1234567890ABCDEF1234567890ABCDEF12345678", _series, _episodes, new QualityModel(Quality.HDTV720p), "Show.S01.720p.HDTV-Grp", new List<QBittorrentTorrentFile> { new QBittorrentTorrentFile { Name = "Show.S01E01.mkv" } });

            other.Files.Single().PendingPath.Should().Be(".Show - S01E01.mkv.12345678.pending");
            Map(("Show.S01E01.mkv", 1000)).Files.Single().PendingPath.Should().Be(".Show - S01E01.mkv.abcdef01.pending");
        }

        [Test]
        public void should_name_subtitles_by_language_and_number_copies_of_the_same_language()
        {
            var map = Map(("Show.S01E01.720p/Show.S01E01.720p.mkv", 1000),
                          ("Show.S01E01.720p/Subs/English/Show.S01E01.720p.srt", 10),
                          ("Show.S01E01.720p/Subs/SDH/Show.S01E01.720p.srt", 10),
                          ("Show.S01E01.720p/A/Subs/Show.S01E01.720p.en.srt", 10),
                          ("Show.S01E01.720p/B/Subs/Show.S01E01.720p.en.srt", 10));

            var subtitles = map.Files.Where(f => f.IsSubtitle).ToList();

            subtitles.Select(f => f.FinalPath).Should().Equal(
                "Show - S01E01.1.en.srt",
                "Show - S01E01.sdh.srt",
                "Show - S01E01.2.en.srt",
                "Show - S01E01.3.en.srt");

            // The number is saved as the title, which library renames keep.
            subtitles.Select(f => f.SubtitleTitle).Should().Equal("1", null, "2", "3");
        }

        [Test]
        public void should_read_languages_and_tags_from_short_names_and_folders()
        {
            var map = Map(("Show.S01E01/Show.S01E01.mkv", 1000),
                          ("Show.S01E01/Subs/eng.forced.srt", 10),
                          ("Show.S01E01/Subs/fr/1.srt", 10),
                          ("Show.S01E01/Subs/English/Show.S01E01.forced.ass", 10));

            map.Files.Where(f => f.IsSubtitle).Select(f => f.FinalPath).Should().Equal(
                "Show - S01E01.en.forced.srt",
                "Show - S01E01.fr.srt",
                "Show - S01E01.en.forced.ass");
        }

        [Test]
        public void should_read_a_language_name_followed_by_tags_and_drop_languages_without_a_code()
        {
            var map = Map(("Show.S01E01/Show.S01E01.mkv", 1000),
                          ("Show.S01E01/Show.S01E01.English.forced.srt", 10),
                          ("Show.S01E01/Subs/Original/Show.S01E01.srt", 10));

            var subtitles = map.Files.Where(f => f.IsSubtitle).ToList();

            subtitles.Select(f => f.FinalPath).Should().Equal("Show - S01E01.en.forced.srt", "Show - S01E01.srt");
            subtitles[1].LanguageId.Should().Be(NzbDrone.Core.Languages.Language.Unknown.Id);
        }

        [Test]
        public void should_read_regional_codes_and_language_names_with_spaces()
        {
            var map = Map(("Show.S01E01/Show.S01E01.mkv", 1000),
                          ("Show.S01E01/Show.S01E01.pt-BR.srt", 10),
                          ("Show.S01E01/Show.S01E01.Spanish (Latino).forced.srt", 10));

            var subtitles = map.Files.Where(f => f.IsSubtitle).ToList();

            subtitles[0].LanguageId.Should().Be(NzbDrone.Core.Languages.Language.PortugueseBrazil.Id);
            subtitles[1].LanguageId.Should().Be(NzbDrone.Core.Languages.Language.SpanishLatino.Id);
            subtitles[1].LanguageTags.Should().Equal("forced");
        }

        [Test]
        public void should_keep_vobsub_tracks_whose_folders_differ_only_by_case_apart()
        {
            var map = Map(("Show.S01E01/Show.S01E01.mkv", 1000),
                          ("Show.S01E01/Subs/English/Show.S01E01.en.idx", 10),
                          ("Show.S01E01/Subs/English/Show.S01E01.en.sub", 10),
                          ("Show.S01E01/Subs/english/Show.S01E01.en.idx", 10),
                          ("Show.S01E01/Subs/english/Show.S01E01.en.sub", 10));

            map.Files.Where(f => f.IsSubtitle).Select(f => f.FinalPath).Should().Equal(
                "Show - S01E01.1.en.idx",
                "Show - S01E01.1.en.sub",
                "Show - S01E01.2.en.idx",
                "Show - S01E01.2.en.sub");
        }

        [Test]
        public void should_keep_a_vobsub_index_and_its_data_on_the_same_name()
        {
            var map = Map(("Show.S01E01/Show.S01E01.mkv", 1000),
                          ("Show.S01E01/Subs/Show.S01E01.en.idx", 10),
                          ("Show.S01E01/Subs/Show.S01E01.en.sub", 10),
                          ("Show.S01E01/Subs/Other/Show.S01E01.en.sub", 10));

            map.Files.Where(f => f.IsSubtitle).Select(f => f.FinalPath).Should().Equal(
                "Show - S01E01.1.en.idx",
                "Show - S01E01.1.en.sub",
                "Show - S01E01.2.en.sub");
        }

        [Test]
        public void should_number_languages_that_share_a_code()
        {
            var map = Map(("Show.S01E01.mkv", 1000), ("Show.S01E01.Dutch.srt", 10), ("Show.S01E01.Flemish.srt", 10));

            var subtitles = map.Files.Where(f => f.IsSubtitle).ToList();

            subtitles.Select(f => f.FinalPath).Should().Equal("Show - S01E01.1.nl.srt", "Show - S01E01.2.nl.srt");
            subtitles.Select(f => f.SubtitleTitle).Should().Equal("1", "2");
        }

        [Test]
        public void should_keep_the_language_and_tags_a_subtitle_name_carries()
        {
            var map = Map(("Show.S01E01.mkv", 1000), ("Show.S01E01.fr.forced.srt", 10), ("Show.S01E01.de.sdh.srt", 10));

            var subtitles = map.Files.Where(f => f.IsSubtitle).ToList();

            subtitles.Select(f => f.FinalPath).Should().Equal("Show - S01E01.fr.forced.srt", "Show - S01E01.de.sdh.srt");
            subtitles[0].LanguageId.Should().Be(NzbDrone.Core.Languages.Language.French.Id);
            subtitles[0].LanguageTags.Should().Equal("forced");
            subtitles[1].LanguageTags.Should().Equal("sdh");
        }

        [Test]
        public void should_shorten_a_long_name_so_every_subtitle_keeps_its_suffix()
        {
            Mocker.GetMock<IBuildFileNames>()
                .Setup(s => s.BuildFilePath(It.IsAny<List<Episode>>(), _series, It.IsAny<EpisodeFile>(), It.IsAny<string>(), null, null))
                .Returns((List<Episode> episodes, Series series, EpisodeFile file, string extension, NamingConfig naming, List<CustomFormat> formats) =>
                    Path.Combine(_seasonFolder, new string('a', 229) + extension));

            var map = Map(("Show.S01E01.mkv", 1000), ("Show.S01E01.en.srt", 10), ("Show.S01E01.fr.srt", 10));

            var video = map.Files.Single(f => !f.IsSubtitle);
            var stem = Path.GetFileNameWithoutExtension(video.FinalPath);

            map.Files.Where(f => f.IsSubtitle).Select(f => f.FinalPath).Should().Equal(stem + ".en.srt", stem + ".fr.srt");
            map.Files.Should().OnlyContain(f => System.Text.Encoding.UTF8.GetByteCount(f.PendingPath) + 4 <= 255);
        }

        [Test]
        public void should_rebuild_final_names_from_what_the_checks_learned()
        {
            var map = Map(("Show.S01E01.mkv", 1000), ("Show.S01E01.en.srt", 10));
            var link = new EngineLink { SavePath = map.SavePath, Files = map.Files };
            var video = link.Files[0];

            Mocker.GetMock<IBuildFileNames>()
                .Setup(s => s.BuildFilePath(It.IsAny<List<Episode>>(), _series, It.Is<EpisodeFile>(f => f.MediaInfo != null && f.SceneName == "Show.S01.720p.HDTV-Grp" && f.RelativePath == "Show.S01E01.mkv"), ".mkv", null, It.IsAny<List<CustomFormat>>()))
                .Returns(Path.Combine(_seasonFolder, "Show - S01E01 [x265].mkv"));

            var localEpisode = new LocalEpisode { Episodes = _episodes, MediaInfo = new NzbDrone.Core.MediaFiles.MediaInfo.MediaInfoModel() };

            Subject.UpdateFinalPaths(link, video, _series, localEpisode).Should().BeTrue();

            video.FinalPath.Should().Be("Show - S01E01 [x265].mkv");
            link.Files[1].FinalPath.Should().Be("Show - S01E01 [x265].en.srt");

            // The pending names stay: the files are already downloading under them.
            video.PendingPath.Should().Be(".Show - S01E01.mkv.abcdef01.pending");
        }

        [Test]
        public void should_keep_the_mapped_name_when_the_rebuilt_one_leaves_the_save_folder()
        {
            var map = Map(("Show.S01E01.mkv", 1000));
            var link = new EngineLink { SavePath = map.SavePath, Files = map.Files };

            Mocker.GetMock<IBuildFileNames>()
                .Setup(s => s.BuildFilePath(It.IsAny<List<Episode>>(), _series, It.IsAny<EpisodeFile>(), ".mkv", null, It.IsAny<List<CustomFormat>>()))
                .Returns(Path.Combine(_series.Path, "Specials", "Show - S01E01.mkv"));

            Subject.UpdateFinalPaths(link, link.Files[0], _series, new LocalEpisode { Episodes = _episodes }).Should().BeFalse();

            link.Files[0].FinalPath.Should().Be("Show - S01E01.mkv");
        }

        private void GivenStaging(string seriesMount, string rootMount)
        {
            var root = Path.GetDirectoryName(_series.Path);
            var seriesMountMock = new Mock<NzbDrone.Common.Disk.IMount>();
            var rootMountMock = new Mock<NzbDrone.Common.Disk.IMount>();
            seriesMountMock.SetupGet(m => m.RootDirectory).Returns(seriesMount);
            rootMountMock.SetupGet(m => m.RootDirectory).Returns(rootMount);

            Mocker.GetMock<NzbDrone.Core.Configuration.IConfigService>().SetupGet(c => c.EngineUseStagingFolder).Returns(true);
            Mocker.GetMock<NzbDrone.Core.RootFolders.IRootFolderService>().Setup(r => r.GetBestRootFolderPath(_series.Path)).Returns(root);
            Mocker.GetMock<NzbDrone.Common.Disk.IDiskProvider>().Setup(d => d.GetMount(root)).Returns(rootMountMock.Object);
            Mocker.GetMock<NzbDrone.Common.Disk.IDiskProvider>().Setup(d => d.GetMount(_seasonFolder)).Returns(seriesMountMock.Object);
        }

        [Test]
        public void should_download_into_the_staging_folder_of_the_root_folder_when_enabled()
        {
            var root = Path.GetDirectoryName(_series.Path);
            GivenStaging(root, root);

            var map = Map(("Show.S01E01.mkv", 1000), ("Show.S01E01.en.srt", 10));

            map.SavePath.Should().Be(root);
            map.Files[0].FinalPath.Should().Be("Show/Season 01/Show - S01E01.mkv");
            map.Files[0].PendingPath.Should().Be(".embarr-staging/abcdef01/.Show - S01E01.mkv.abcdef01.pending");
            map.Files[1].PendingPath.Should().Be(".embarr-staging/abcdef01/.Show - S01E01.en.srt.abcdef01.pending");
        }

        [Test]
        public void should_not_stage_files_whose_final_folder_is_on_another_drive()
        {
            GivenStaging("/mnt/other".AsOsAgnostic(), Path.GetDirectoryName(_series.Path));

            var map = Map(("Show.S01E01.mkv", 1000));

            map.SavePath.Should().Be(_seasonFolder);
            map.Files[0].PendingPath.Should().Be(".Show - S01E01.mkv.abcdef01.pending");
        }

        [TestCase(".Show - S01E01.mkv.abcdef01.pending", ".Show - S01E01.mkv")]
        [TestCase("Show - S01E01.mkv", "Show - S01E01.mkv")]
        [TestCase("Show.pending.mkv", "Show.pending.mkv")]
        public void should_strip_only_the_pending_suffix(string name, string expected)
        {
            EngineFileMapper.GetNameWithoutPendingSuffix(name).Should().Be(expected);
        }
    }
}
