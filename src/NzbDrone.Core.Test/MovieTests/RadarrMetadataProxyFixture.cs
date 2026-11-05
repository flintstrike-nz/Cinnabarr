using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.RadarrMetadata;
using NzbDrone.Core.MetadataSource.RadarrMetadata.Resource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class RadarrMetadataProxyFixture : CoreTest<RadarrMetadataProxy>
    {
        private const string MatrixJson = @"{
            ""tmdbId"": 603, ""imdbId"": ""tt0133093"", ""title"": ""The Matrix"", ""originalTitle"": ""The Matrix"",
            ""overview"": ""A hacker learns the truth."", ""runtime"": 136, ""year"": 1999, ""originalLanguage"": ""en"",
            ""inCinema"": ""1999-03-31T00:00:00Z"", ""physicalRelease"": ""1999-09-21T00:00:00Z"",
            ""genres"": [""Action"", ""Science Fiction""], ""keywords"": [""simulation""],
            ""images"": [{ ""coverType"": ""Poster"", ""url"": ""https://image.example/poster.jpg"" }, { ""coverType"": ""Fanart"", ""url"": ""https://image.example/fanart.jpg"" }],
            ""alternativeTitles"": [{ ""title"": ""Matrix"", ""sourceType"": ""tmdb"" }],
            ""translations"": [{ ""title"": ""Matrix"", ""overview"": ""Ein Hacker."", ""language"": ""de"" }],
            ""certifications"": [{ ""country"": ""US"", ""certification"": ""R"" }, { ""country"": ""GB"", ""certification"": ""15"" }],
            ""movieRatings"": { ""tmdb"": { ""count"": 25000, ""value"": 8.2, ""type"": ""user"" } },
            ""ratings"": [{ ""count"": 25000, ""value"": 8.2 }],
            ""recommendations"": [{ ""tmdbId"": 604 }],
            ""collection"": { ""tmdbId"": 2344, ""name"": ""The Matrix Collection"" },
            ""credits"": {
                ""cast"": [{ ""name"": ""Keanu Reeves"", ""order"": 0, ""character"": ""Neo"", ""tmdbId"": 6384, ""creditId"": ""c1"", ""images"": [] }],
                ""crew"": [{ ""name"": ""Lana Wachowski"", ""order"": 0, ""job"": ""Director"", ""department"": ""Directing"", ""tmdbId"": 9339, ""creditId"": ""c2"", ""images"": [] }]
            },
            ""studio"": ""Warner Bros."", ""youtubeTrailerId"": ""vKQi3bBA1y8"", ""popularity"": 55.5
        }";

        private HttpRequest _request;

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.MovieMetadataUrl)
                .Returns("https://metadata.example/v1/{route}");

            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.CertificationCountry)
                .Returns(TMDbCountryCode.US);
        }

        private void GivenMovieResponse(string json, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<MovieResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest r) =>
                {
                    _request = r;

                    var response = new HttpResponse(r, new HttpHeader(), json, status);

                    return new HttpResponse<MovieResource>(response);
                });
        }

        [Test]
        public void should_ask_the_configured_service_for_the_movie()
        {
            GivenMovieResponse(MatrixJson);

            Subject.GetMovieInfo(603);

            _request.Url.FullUri.Should().Be("https://metadata.example/v1/movie/603");
        }

        [Test]
        public void should_map_the_movie()
        {
            GivenMovieResponse(MatrixJson);

            var (movie, credits) = Subject.GetMovieInfo(603);

            movie.TmdbId.Should().Be(603);
            movie.ImdbId.Should().Be("tt0133093");
            movie.Title.Should().Be("The Matrix");
            movie.CleanTitle.Should().Be("thematrix");
            movie.OriginalLanguage.Should().Be(Language.English);
            movie.Runtime.Should().Be(136);
            movie.Year.Should().Be(1999);
            movie.Certification.Should().Be("R");
            movie.Genres.Should().Equal("Action", "Science Fiction");
            movie.Images.Should().HaveCount(2);
            movie.Images[0].CoverType.Should().Be(MediaCoverTypes.Poster);
            movie.Ratings.Tmdb.Votes.Should().Be(25000);
            movie.CollectionTmdbId.Should().Be(2344);
            movie.Recommendations.Should().Equal(604);
            movie.Status.Should().Be(MovieStatusType.Released);
            movie.AlternativeTitles.Should().ContainSingle().Which.CleanTitle.Should().Be("matrix");
            movie.Translations.Should().ContainSingle().Which.Language.Should().Be(Language.German);
            movie.InCinemas.Should().Be(new DateTime(1999, 3, 31, 0, 0, 0, DateTimeKind.Utc));

            credits.Should().HaveCount(2);
            credits.Should().Contain(c => c.Name == "Keanu Reeves" && c.Type == NzbDrone.Core.Movies.Credits.CreditType.Cast && c.Character == "Neo");
            credits.Should().Contain(c => c.Name == "Lana Wachowski" && c.Type == NzbDrone.Core.Movies.Credits.CreditType.Crew && c.Job == "Director");
        }

        [Test]
        public void should_use_the_certification_for_the_configured_country()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.CertificationCountry).Returns(TMDbCountryCode.GB);
            GivenMovieResponse(MatrixJson);

            Subject.GetMovieInfo(603).Item1.Certification.Should().Be("15");
        }

        [Test]
        public void should_say_the_movie_was_not_found_on_a_404()
        {
            GivenMovieResponse("{}", System.Net.HttpStatusCode.NotFound);

            Subject.Invoking(s => s.GetMovieInfo(1)).Should().Throw<MovieNotFoundException>();
        }
    }
}
