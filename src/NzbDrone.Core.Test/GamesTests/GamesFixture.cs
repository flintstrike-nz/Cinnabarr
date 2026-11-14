using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
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
}
