using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class AddMovieServiceFixture : CoreTest<AddMovieService>
    {
        private Movie _newMovie;

        [SetUp]
        public void SetUp()
        {
            _newMovie = new Movie
            {
                RootFolderPath = "/movies",
                Monitored = true,
                QualityProfileId = 1,
                MovieMetadata = new MovieMetadata { TmdbId = 603 }
            };

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(p => p.GetMovieInfo(603))
                .Returns(Tuple.Create(new MovieMetadata { TmdbId = 603, Title = "The Matrix", Year = 1999 }, new List<Credit>()));

            Mocker.GetMock<IAddMovieValidator>()
                .Setup(v => v.Validate(It.IsAny<Movie>()))
                .Returns(new ValidationResult());

            Mocker.GetMock<IBuildMovieFolderName>()
                .Setup(b => b.GetMovieFolder(It.IsAny<Movie>()))
                .Returns("The Matrix (1999)");

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.AddMovie(It.IsAny<Movie>()))
                .Returns((Movie m) => m);
        }

        [Test]
        public void should_fill_in_the_movie_from_the_metadata_service_and_set_the_folder()
        {
            var added = Subject.AddMovie(_newMovie);

            added.Title.Should().Be("The Matrix");
            added.Year.Should().Be(1999);
            added.Path.Should().Be(System.IO.Path.Combine("/movies", "The Matrix (1999)"));
            added.QualityProfileId.Should().Be(1);
            added.Monitored.Should().BeTrue();
            added.MovieMetadata.Value.CleanTitle.Should().Be("thematrix");

            Mocker.GetMock<IMovieMetadataService>().Verify(s => s.Upsert(It.Is<MovieMetadata>(m => m.TmdbId == 603)), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(s => s.AddMovie(It.IsAny<Movie>()), Times.Once());
        }

        [Test]
        public void should_keep_the_path_that_was_given()
        {
            _newMovie.Path = "/elsewhere/Matrix";

            Subject.AddMovie(_newMovie).Path.Should().Be("/elsewhere/Matrix");
        }

        [Test]
        public void should_refuse_a_movie_the_metadata_service_does_not_know()
        {
            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(p => p.GetMovieInfo(603))
                .Throws(new MovieNotFoundException(603));

            Subject.Invoking(s => s.AddMovie(_newMovie)).Should().Throw<ValidationException>();
            Mocker.GetMock<IMovieService>().Verify(s => s.AddMovie(It.IsAny<Movie>()), Times.Never());

            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_not_add_a_movie_that_fails_validation()
        {
            Mocker.GetMock<IAddMovieValidator>()
                .Setup(v => v.Validate(It.IsAny<Movie>()))
                .Returns(new ValidationResult(new[] { new ValidationFailure("Path", "Path is already used") }));

            Subject.Invoking(s => s.AddMovie(_newMovie)).Should().Throw<ValidationException>();
            Mocker.GetMock<IMovieService>().Verify(s => s.AddMovie(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void should_skip_invalid_movies_when_adding_many_and_errors_are_ignored()
        {
            Mocker.GetMock<IAddMovieValidator>()
                .Setup(v => v.Validate(It.IsAny<Movie>()))
                .Returns(new ValidationResult(new[] { new ValidationFailure("Path", "bad") }));

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.AddMovies(It.IsAny<List<Movie>>()))
                .Returns((List<Movie> m) => m);

            Subject.AddMovies(new List<Movie> { _newMovie }, ignoreErrors: true).Should().BeEmpty();
        }
    }
}
