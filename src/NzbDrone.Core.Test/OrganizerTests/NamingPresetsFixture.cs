using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.OrganizerTests
{
    [TestFixture]
    public class NamingPresetsFixture : CoreTest<FileNameBuilder>
    {
        private Series _series;
        private Episode _episode;
        private EpisodeFile _episodeFile;
        private Movie _movie;
        private MovieFile _movieFile;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Title = "30 Rock", Year = 2006, TvdbId = 79488 };
            _episode = new Episode { SeasonNumber = 1, EpisodeNumber = 5, Title = "Episode Title" };
            _episodeFile = new EpisodeFile { Quality = new QualityModel(Quality.HDTV720p), ReleaseGroup = "Grp", RelativePath = "x.mkv" };

            _movie = new Movie
            {
                MovieMetadata = new MovieMetadata { Title = "Alien", Year = 1979, TmdbId = 348, ImdbId = "tt0078748" }
            };
            _movieFile = new MovieFile { Quality = new QualityModel(Quality.Bluray1080p), ReleaseGroup = "Grp", RelativePath = "x.mkv" };

            Mocker.GetMock<IQualityDefinitionService>()
                  .Setup(s => s.Get(It.IsAny<Quality>()))
                  .Returns<Quality>(q => new QualityDefinition(q) { Title = q.Name });
        }

        private void GivenPreset(NamingPreset preset)
        {
            Mocker.GetMock<INamingConfigService>().Setup(c => c.GetConfig()).Returns(NamingPresets.Get(preset));
        }

        [Test]
        public void the_default_is_the_jellyfin_preset_with_renaming_on()
        {
            NamingConfig.Default.Should().BeEquivalentTo(NamingPresets.Get(NamingPreset.Jellyfin));
            NamingConfig.Default.RenameEpisodes.Should().BeTrue();
            NamingConfig.Default.RenameMovies.Should().BeTrue();
        }

        [TestCase(NamingPreset.Jellyfin, "30 Rock (2006) [tvdbid-79488]")]
        [TestCase(NamingPreset.Emby, "30 Rock (2006) [tvdbid=79488]")]
        [TestCase(NamingPreset.Plex, "30 Rock (2006) {tvdb-79488}")]
        public void should_name_the_series_folder_with_the_servers_provider_id(NamingPreset preset, string expected)
        {
            GivenPreset(preset);

            Subject.GetSeriesFolder(_series).Should().Be(expected);
        }

        [TestCase(NamingPreset.Jellyfin, "Alien (1979) [tmdbid-348]")]
        [TestCase(NamingPreset.Emby, "Alien (1979) [tmdbid=348]")]
        [TestCase(NamingPreset.Plex, "Alien (1979) {tmdb-348}")]
        public void should_name_the_movie_folder_with_the_servers_provider_id(NamingPreset preset, string expected)
        {
            GivenPreset(preset);

            Subject.GetMovieFolder(_movie).Should().Be(expected);
        }

        [TestCase(NamingPreset.Jellyfin)]
        [TestCase(NamingPreset.Emby)]
        [TestCase(NamingPreset.Plex)]
        public void should_name_files_the_same_way_for_every_preset(NamingPreset preset)
        {
            GivenPreset(preset);

            Subject.BuildFileName(new List<Episode> { _episode }, _series, _episodeFile).Should().Be("30 Rock (2006) - S01E05 - Episode Title");
            Subject.BuildFileName(_movie, _movieFile).Should().Be("Alien (1979)");
            Subject.GetSeasonFolder(_series, 1).Should().Be("Season 01");
        }

        [Test]
        public void should_join_a_multi_episode_file_with_a_range()
        {
            GivenPreset(NamingPreset.Jellyfin);

            var episodes = new List<Episode> { _episode, new Episode { SeasonNumber = 1, EpisodeNumber = 6, Title = "Episode Title" } };

            Subject.BuildFileName(episodes, _series, _episodeFile).Should().Be("30 Rock (2006) - S01E05-E06 - Episode Title");
        }
    }
}
