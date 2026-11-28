using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Games;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.GamesTests
{
    [TestFixture]
    public class GameTitleParserFixture
    {
        [TestCase("Super Mario World (USA)", "Super Mario World", "USA")]
        [TestCase("Legend of Zelda, The - A Link to the Past (Europe) (Rev 1) [!]", "The Legend of Zelda - A Link to the Past", "Europe")]
        [TestCase("Sonic_The_Hedgehog (Japan, USA)", "Sonic The Hedgehog", "Japan")]
        [TestCase("Final Fantasy VII (Disc 1)", "Final Fantasy VII", null)]
        public void should_parse_title_and_region(string name, string title, string region)
        {
            var parsed = GameTitleParser.Parse(name);

            parsed.Title.Should().Be(title);
            parsed.Region.Should().Be(region);
        }

        [Test]
        public void should_sort_without_the_article()
        {
            GameTitleParser.SortTitle("The Legend of Zelda").Should().Be("legend of zelda");
        }
    }

    [TestFixture]
    public class GamePlatformsFixture
    {
        private const long Mb = 1024 * 1024;

        [TestCase(".sms", "sms")]
        [TestCase(".sfc", "snes")]
        [TestCase(".smc", "snes")]
        [TestCase(".cue", "psx")]
        [TestCase(".pbp", "psx")]
        [TestCase(".cso", "ps2")]
        public void should_detect_by_a_unique_extension(string extension, string platform)
        {
            GamePlatforms.Detect(new[] { "Games" }, extension, 10 * Mb).Key.Should().Be(platform);
        }

        [TestCase("SNES", ".zip", "snes")]
        [TestCase("Sega Master System", ".zip", "sms")]
        [TestCase("PS2", ".iso", "ps2")]
        [TestCase("PlayStation", ".chd", "psx")]
        public void should_prefer_the_folder_name(string folder, string extension, string platform)
        {
            GamePlatforms.Detect(new[] { "Games", folder }, extension, 10 * Mb).Key.Should().Be(platform);
        }

        [Test]
        public void should_not_guess_an_archive_without_a_folder()
        {
            GamePlatforms.Detect(new[] { "Games" }, ".zip", 10 * Mb).Should().BeNull();
        }

        [TestCase(650L, "psx")]
        [TestCase(4300L, "ps2")]
        public void should_tell_a_disc_image_by_size_when_nothing_else_says(long megabytes, string platform)
        {
            GamePlatforms.Detect(new[] { "Games" }, ".iso", megabytes * Mb).Key.Should().Be(platform);
        }

        [TestCase(".nes", "nes")]
        [TestCase(".fds", "nes")]
        [TestCase(".gen", "md")]
        [TestCase(".smd", "md")]
        [TestCase(".z64", "n64")]
        [TestCase(".v64", "n64")]
        [TestCase(".gcm", "gc")]
        [TestCase(".wbfs", "wii")]
        [TestCase(".wua", "wiiu")]
        [TestCase(".wux", "wiiu")]
        public void should_detect_new_platforms_by_a_unique_extension(string extension, string platform)
        {
            GamePlatforms.Detect(new[] { "Games" }, extension, 10 * Mb).Key.Should().Be(platform);
        }

        [TestCase(".md")]
        [TestCase(".bin")]
        [TestCase(".rvz")]
        [TestCase(".gcz")]
        public void should_not_guess_shared_or_markdown_extensions_without_a_folder(string extension)
        {
            GamePlatforms.Detect(new[] { "Games" }, extension, 10 * Mb).Should().BeNull();
        }

        [TestCase("Mega Drive", ".md", "md")]
        [TestCase("Genesis", ".bin", "md")]
        [TestCase("Nintendo 64", ".zip", "n64")]
        [TestCase("GameCube", ".iso", "gc")]
        [TestCase("Wii", ".rvz", "wii")]
        [TestCase("NES", ".zip", "nes")]
        [TestCase("Wii U", ".wud", "wiiu")]
        public void should_detect_new_platforms_by_folder(string folder, string extension, string platform)
        {
            GamePlatforms.Detect(new[] { "Games", folder }, extension, 10 * Mb).Key.Should().Be(platform);
        }

        [Test]
        public void should_not_take_a_cd_track_in_a_playstation_folder_for_a_mega_drive_game()
        {
            GamePlatforms.Detect(new[] { "Games", "PlayStation" }, ".bin", 500 * Mb).Should().BeNull();
        }

        private static byte[] Header(int length = DiscSignature.HeaderLength)
        {
            return new byte[length];
        }

        private static void PutBigEndian(byte[] b, int offset, uint value)
        {
            b[offset] = (byte)(value >> 24);
            b[offset + 1] = (byte)(value >> 16);
            b[offset + 2] = (byte)(value >> 8);
            b[offset + 3] = (byte)value;
        }

        [Test]
        public void should_tell_a_wii_iso_by_its_header()
        {
            var header = Header();
            PutBigEndian(header, 0x18, 0x5D1C9EA3);

            GamePlatforms.Detect(new[] { "Games" }, ".iso", 4300 * Mb, header).Key.Should().Be("wii");
        }

        [Test]
        public void should_tell_a_gamecube_iso_by_its_header()
        {
            var header = Header();
            PutBigEndian(header, 0x1C, 0xC2339F3D);

            GamePlatforms.Detect(new[] { "Games" }, ".iso", 1400 * Mb, header).Key.Should().Be("gc");
        }

        [TestCase("RVZ\u0001", 1u, "gc")]
        [TestCase("RVZ\u0001", 2u, "wii")]
        [TestCase("WIA\u0001", 2u, "wii")]
        public void should_tell_rvz_and_wia_by_disc_type(string magic, uint discType, string platform)
        {
            var header = Header();
            var text = System.Text.RegularExpressions.Regex.Unescape(magic);

            for (var i = 0; i < 4; i++)
            {
                header[i] = (byte)text[i];
            }

            PutBigEndian(header, 0x48, discType);

            GamePlatforms.Detect(new[] { "Games" }, ".rvz", 900 * Mb, header).Key.Should().Be(platform);
        }

        [TestCase(0u, "gc")]
        [TestCase(1u, "wii")]
        public void should_tell_gcz_by_sub_type(uint subType, string platform)
        {
            var header = Header();
            System.BitConverter.GetBytes(0xB10BC001u).CopyTo(header, 0);
            System.BitConverter.GetBytes(subType).CopyTo(header, 4);

            GamePlatforms.Detect(new[] { "Games" }, ".gcz", 900 * Mb, header).Key.Should().Be(platform);
        }

        [Test]
        public void should_fall_back_to_size_for_an_iso_that_is_not_a_nintendo_disc()
        {
            GamePlatforms.Detect(new[] { "Games" }, ".iso", 650 * Mb, Header()).Key.Should().Be("psx");
        }

        [Test]
        public void should_ignore_a_short_header()
        {
            DiscSignature.Identify(new byte[4]).Should().BeNull();
            DiscSignature.Identify(null).Should().BeNull();
        }

        [Test]
        public void every_platform_has_an_igdb_id()
        {
            foreach (var platform in GamePlatforms.All)
            {
                IgdbClient.PlatformIds.Should().ContainKey(platform.Key);
            }
        }

        [Test]
        public void every_platform_has_a_way_to_play_or_a_streaming_plan()
        {
            foreach (var platform in GamePlatforms.All)
            {
                (platform.Core != null || platform.Play == GamePlayMode.Stream).Should().BeTrue(platform.Key);
                (platform.Play != GamePlayMode.Stream || platform.Core == null).Should().BeTrue(platform.Key);
            }
        }
    }

    [TestFixture]
    public class GameScanFixture : CoreTest<GameService>
    {
        [Test]
        public void should_find_games_and_skip_discs_covered_by_a_playlist_or_ignored_files()
        {
            var root = "/games";
            var files = new[]
            {
                "/games/SNES/Super Mario World (USA).sfc",
                "/games/SNES/readme.txt",
                "/games/PS1/Final Fantasy VII (USA)/Final Fantasy VII (USA).m3u",
                "/games/PS1/Final Fantasy VII (USA)/Final Fantasy VII (USA) (Disc 1).cue",
                "/games/PS1/Final Fantasy VII (USA)/Final Fantasy VII (USA) (Disc 1).bin",
                "/games/PS1/Crash Bandicoot (USA).cue",
                "/games/PS1/Crash Bandicoot (USA) (Track 1).bin",
                "/games/Master System/Alex Kidd in Miracle World (USA, Europe).sms"
            };

            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFiles(root, true)).Returns(files);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFiles("/games/PS1", false)).Returns(files.Where(f => f.StartsWith("/games/PS1/Crash")));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(It.IsAny<string>())).Returns(1000);

            var games = Subject.FindGames(root).ToList();

            games.Select(g => g.Title).Should().BeEquivalentTo("Super Mario World", "Final Fantasy VII", "Crash Bandicoot", "Alex Kidd in Miracle World");
            games.Single(g => g.Title == "Crash Bandicoot").Size.Should().Be(2000);
            games.Single(g => g.Title == "Super Mario World").PlatformKey.Should().Be("snes");
            games.Single(g => g.Title == "Final Fantasy VII").PlatformKey.Should().Be("psx");
            games.Single(g => g.Title == "Alex Kidd in Miracle World").PlatformKey.Should().Be("sms");
        }
    }

    [TestFixture]
    public class IgdbMatcherFixture
    {
        [Test]
        public void should_prefer_the_result_with_the_same_name()
        {
            var results = new[]
            {
                new IgdbGame { Id = 1, Name = "Super Mario World 2: Yoshi's Island" },
                new IgdbGame { Id = 2, Name = "Super Mario World" }
            };

            IgdbMatcher.PickBest("Super Mario World", results).Id.Should().Be(2);
        }

        [Test]
        public void should_take_the_first_result_without_an_exact_name()
        {
            var results = new[] { new IgdbGame { Id = 5, Name = "Alex Kidd in Miracle World" } };

            IgdbMatcher.PickBest("Alex Kidd", results).Id.Should().Be(5);
        }

        [Test]
        public void should_find_nothing_in_no_results()
        {
            IgdbMatcher.PickBest("Anything", new IgdbGame[0]).Should().BeNull();
        }

        [Test]
        public void should_build_a_cover_url_from_the_image_id()
        {
            IgdbMatcher.CoverUrl(new IgdbGame { Cover = new IgdbCover { ImageId = "co1abc" } }).Should().Be("https://images.igdb.com/igdb/image/upload/t_cover_big/co1abc.jpg");
            IgdbMatcher.CoverUrl(new IgdbGame()).Should().BeNull();
        }

        [Test]
        public void should_copy_the_details_onto_the_game()
        {
            var game = new Game { Title = "Super Mario World" };

            GameMetadataService.Apply(game, new IgdbGame
            {
                Id = 7,
                Summary = "Jump.",
                FirstReleaseDate = 658195200, // 1990-11-12
                Rating = 91.6,
                Genres = new System.Collections.Generic.List<IgdbName> { new IgdbName { Name = "Platform" }, new IgdbName { Name = "Adventure" } },
                InvolvedCompanies = new System.Collections.Generic.List<IgdbInvolvedCompany>
                {
                    new IgdbInvolvedCompany { Developer = false, Company = new IgdbName { Name = "Publisher Co" } },
                    new IgdbInvolvedCompany { Developer = true, Company = new IgdbName { Name = "Nintendo EAD" } }
                }
            });

            game.IgdbId.Should().Be(7);
            game.Year.Should().Be(1990);
            game.Genres.Should().Be("Platform, Adventure");
            game.Developer.Should().Be("Nintendo EAD");
            game.Rating.Should().Be(92);
        }
    }

    [TestFixture]
    public class GamePlayFixture : CoreTest<GamePlayService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.AppDataFolder).Returns("/config");
        }

        [Test]
        public void should_list_the_tracks_of_a_cue_sheet()
        {
            var cue = "FILE \"Crash (Track 1).bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\nFILE \"Crash (Track 2).bin\" BINARY\n  TRACK 02 AUDIO\n";

            GamePlayService.CueTracks(cue).Should().Equal("Crash (Track 1).bin", "Crash (Track 2).bin");
        }

        [TestCase("sms")]
        [TestCase("snes")]
        [TestCase("psx")]
        [TestCase("nes")]
        [TestCase("md")]
        [TestCase("n64")]
        public void should_play_what_the_browser_emulator_has_a_core_for(string platform)
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(true);

            var info = Subject.GetInfo(new Game { Id = 1, PlatformKey = platform, Path = "/games/x.rom" });

            info.Playable.Should().BeTrue();
            info.NeedsBios.Should().Be(platform == "psx");
        }

        [TestCase("ps2", "PlayStation 2")]
        [TestCase("gc", "GameCube")]
        [TestCase("wii", "Wii")]
        [TestCase("wiiu", "Wii U")]
        public void should_not_play_streamed_platforms_in_the_browser(string platform, string name)
        {
            var info = Subject.GetInfo(new Game { Id = 1, PlatformKey = platform, Path = "/games/x.iso" });

            info.Playable.Should().BeFalse();
            info.Reason.Should().Contain(name).And.Contain("streamed");
        }

        [Test]
        public void should_not_play_a_game_whose_file_is_missing()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(false);

            Subject.GetInfo(new Game { Id = 1, PlatformKey = "snes", Path = "/games/gone.sfc" }).Playable.Should().BeFalse();
        }
    }
}
