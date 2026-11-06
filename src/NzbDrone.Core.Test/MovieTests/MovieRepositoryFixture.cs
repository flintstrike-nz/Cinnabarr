using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MovieRepositoryFixture : DbTest<MovieRepository, Movie>
    {
        private QualityProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(Quality.Bluray1080p, Quality.DVD, Quality.HDTV720p),
                Cutoff = Quality.Bluray1080p.Id,
                Name = "TestProfile"
            };

            Mocker.Resolve<QualityProfileRepository>().Insert(_profile);
        }

        private Movie AddMovie(int tmdbId, string title, int year = 1999, string path = null)
        {
            var metadata = new MovieMetadata
            {
                TmdbId = tmdbId,
                ImdbId = $"tt{tmdbId:D7}",
                Title = title,
                CleanTitle = title.ToLowerInvariant().Replace(" ", ""),
                OriginalTitle = title,
                OriginalLanguage = Language.English,
                Year = year,
                Status = MovieStatusType.Released
            };

            Mocker.Resolve<MovieMetadataRepository>().Insert(metadata);

            var movie = new Movie
            {
                MovieMetadataId = metadata.Id,
                QualityProfileId = _profile.Id,
                Path = path ?? $"/movies/{title} ({year})",
                Monitored = true,
                MinimumAvailability = MovieStatusType.Released,
                Added = DateTime.UtcNow,
                Tags = new HashSet<int> { 1, 2 }
            };

            return Subject.Insert(movie);
        }

        [Test]
        public void should_load_the_metadata_and_profile_with_the_movie()
        {
            AddMovie(603, "The Matrix");

            var movie = Subject.FindByTmdbId(603);

            movie.Title.Should().Be("The Matrix");
            movie.Year.Should().Be(1999);
            movie.TmdbId.Should().Be(603);
            movie.QualityProfile.Should().NotBeNull();
            movie.QualityProfile.Name.Should().Be("TestProfile");
            movie.Tags.Should().BeEquivalentTo(new[] { 1, 2 });
            movie.HasFile.Should().BeFalse();
        }

        [Test]
        public void should_find_by_imdb_id_and_path()
        {
            AddMovie(603, "The Matrix", path: "/movies/Matrix");

            Subject.FindByImdbId("tt0000603").TmdbId.Should().Be(603);
            Subject.FindByPath("/movies/Matrix").TmdbId.Should().Be(603);
            Subject.MoviePathExists("/movies/Matrix").Should().BeTrue();
            Subject.MoviePathExists("/movies/Other").Should().BeFalse();
        }

        [Test]
        public void should_list_ids_and_paths()
        {
            AddMovie(603, "The Matrix");
            AddMovie(604, "The Matrix Reloaded", 2003);

            Subject.AllMovieTmdbIds().Should().BeEquivalentTo(new[] { 603, 604 });
            Subject.AllMoviePaths().Should().HaveCount(2);
        }

        [Test]
        public void should_report_whether_metadata_is_in_use()
        {
            var movie = AddMovie(603, "The Matrix");

            Subject.ExistsByMetadataId(movie.MovieMetadataId).Should().BeTrue();
            Subject.ExistsByMetadataId(9999).Should().BeFalse();
        }

        [Test]
        public void should_list_movies_without_a_file_only()
        {
            var withFile = AddMovie(603, "The Matrix");
            AddMovie(604, "The Matrix Reloaded", 2003);

            var file = new MovieFile
            {
                MovieId = withFile.Id,
                RelativePath = "The Matrix (1999).mkv",
                Size = 1234,
                DateAdded = DateTime.UtcNow,
                Quality = new QualityModel(Quality.Bluray1080p),
                Languages = new List<Language> { Language.English }
            };

            var storedFile = Mocker.Resolve<MovieFileRepository>().Insert(file);
            withFile.MovieFileId = storedFile.Id;
            Subject.Update(withFile);

            Subject.GetMoviesByFileId(storedFile.Id).Should().ContainSingle().Which.TmdbId.Should().Be(603);
            Subject.Find(withFile.Id).HasFile.Should().BeTrue();
        }
    }
}
