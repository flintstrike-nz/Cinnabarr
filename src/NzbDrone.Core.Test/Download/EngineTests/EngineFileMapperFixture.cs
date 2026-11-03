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
            return Subject.Map(_series, _episodes, new QualityModel(Quality.HDTV720p), "Show.S01.720p.HDTV-Grp", files.Select(f => new QBittorrentTorrentFile { Name = f.Name, Size = f.Size }).ToList());
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
            map.Files[0].PendingPath.Should().Be(".Show - S01E01.mkv.pending");
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
            subtitle.PendingPath.Should().Be(".Show - S01E02.en.srt.pending");
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
            map.Files.Select(f => f.PendingPath).Should().Equal("Season 01/.Show - E1.mkv.pending", "Season 02/.Show - E2.mkv.pending");
        }
    }
}
