using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MovieCutoffServiceFixture : DbTest<MovieCutoffService, Movie>
    {
        private QualityProfile _profile;
        private MovieRepository _movies;

        [SetUp]
        public void SetUp()
        {
            _profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                Cutoff = Quality.Bluray1080p.Id,
                UpgradeAllowed = true,
                Name = "TestProfile"
            };

            Mocker.Resolve<QualityProfileRepository>().Insert(_profile);

            _movies = Mocker.Resolve<MovieRepository>();
            Mocker.SetConstant<IMovieRepository>(_movies);

            Mocker.GetMock<IQualityProfileService>().Setup(s => s.All()).Returns(() => new List<QualityProfile> { _profile });
        }

        private Movie AddMovie(int tmdbId, Quality fileQuality, bool monitored = true)
        {
            var metadata = new MovieMetadata
            {
                TmdbId = tmdbId,
                ImdbId = $"tt{tmdbId:D7}",
                Title = $"Movie {tmdbId}",
                CleanTitle = $"movie{tmdbId}",
                OriginalTitle = $"Movie {tmdbId}",
                OriginalLanguage = Language.English,
                Year = 1999,
                Status = MovieStatusType.Released
            };

            Mocker.Resolve<MovieMetadataRepository>().Insert(metadata);

            var movie = _movies.Insert(new Movie
            {
                MovieMetadataId = metadata.Id,
                QualityProfileId = _profile.Id,
                Path = $"/movies/{tmdbId}",
                Monitored = monitored,
                MinimumAvailability = MovieStatusType.Released,
                Added = DateTime.UtcNow
            });

            if (fileQuality != null)
            {
                var file = Mocker.Resolve<MovieFileRepository>().Insert(new MovieFile
                {
                    MovieId = movie.Id,
                    RelativePath = $"{tmdbId}.mkv",
                    Quality = new QualityModel(fileQuality),
                    Languages = new List<Language> { Language.English },
                    DateAdded = DateTime.UtcNow
                });

                movie.MovieFileId = file.Id;
                _movies.Update(movie);
            }

            return movie;
        }

        private static PagingSpec<Movie> Paging()
        {
            return new PagingSpec<Movie> { Page = 1, PageSize = 100, SortDirection = SortDirection.Ascending, SortKey = "Id" };
        }

        [Test]
        public void should_return_movies_with_a_file_below_the_cutoff()
        {
            var below = AddMovie(1, Quality.DVD);
            AddMovie(2, Quality.Bluray1080p);
            AddMovie(3, Quality.Bluray2160p);
            AddMovie(4, null);

            var result = Subject.MoviesWhereCutoffUnmet(Paging());

            result.Records.Select(m => m.Id).Should().Equal(below.Id);
        }

        [Test]
        public void should_return_nothing_when_the_profile_does_not_allow_upgrades_and_the_file_is_the_best_allowed()
        {
            _profile.UpgradeAllowed = false;
            _profile.Items.ToList().ForEach(i => i.Allowed = false);
            _profile.Items.Single(i => i.Quality?.Id == Quality.Bluray1080p.Id).Allowed = true;

            AddMovie(1, Quality.Bluray1080p);

            Subject.MoviesWhereCutoffUnmet(Paging()).Records.Should().BeEmpty();
        }

        [Test]
        public void should_return_nothing_when_no_profile_has_qualities_below_its_cutoff()
        {
            Mocker.GetMock<IQualityProfileService>().Setup(s => s.All()).Returns(new List<QualityProfile>());

            AddMovie(1, Quality.DVD);

            Subject.MoviesWhereCutoffUnmet(Paging()).Records.Should().BeEmpty();
        }

        [Test]
        public void should_list_movies_without_a_file_as_missing_and_not_as_cutoff_unmet()
        {
            AddMovie(1, null);
            AddMovie(2, Quality.DVD);

            _movies.MoviesWithoutFiles(Paging()).Records.Should().ContainSingle().Which.TmdbId.Should().Be(1);
            Subject.MoviesWhereCutoffUnmet(Paging()).Records.Should().ContainSingle().Which.TmdbId.Should().Be(2);
        }

        [Test]
        public void should_only_return_monitored_movies_when_asked_to()
        {
            AddMovie(1, Quality.DVD, monitored: false);
            var monitored = AddMovie(2, Quality.DVD);

            var spec = Paging();
            spec.FilterExpressions.Add(m => m.Monitored == true);

            Subject.MoviesWhereCutoffUnmet(spec).Records.Select(m => m.Id).Should().Equal(monitored.Id);
        }
    }
}
