using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.RadarrMetadata;
using NzbDrone.Core.MetadataSource.RadarrMetadata.Resource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class RadarrMetadataSearchFixture : CoreTest<RadarrMetadataProxy>
    {
        private const string SearchJson = @"[
            { ""tmdbId"": 603, ""imdbId"": ""tt0133093"", ""title"": ""The Matrix"", ""originalTitle"": ""The Matrix"", ""year"": 1999, ""images"": [], ""genres"": [] },
            { ""tmdbId"": 604, ""imdbId"": ""tt0234215"", ""title"": ""The Matrix Reloaded"", ""originalTitle"": ""The Matrix Reloaded"", ""year"": 2003, ""images"": [], ""genres"": [] }
        ]";

        private HttpRequest _searchRequest;

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.MovieMetadataUrl).Returns("https://metadata.example/v1/{route}");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.CertificationCountry).Returns(TMDbCountryCode.US);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<List<MovieResource>>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest r) =>
                {
                    _searchRequest = r;

                    return new HttpResponse<List<MovieResource>>(new HttpResponse(r, new HttpHeader(), SearchJson, System.Net.HttpStatusCode.OK));
                });
        }

        [Test]
        public void should_search_the_service_by_title()
        {
            var results = Subject.SearchForNewMovie("The Matrix");

            _searchRequest.Url.FullUri.Should().StartWith("https://metadata.example/v1/search");
            _searchRequest.Url.Query.Should().Contain("q=the%2Bmatrix");

            results.Select(m => m.TmdbId).Should().Equal(603, 604);
            results.Should().OnlyContain(m => m.Id == 0);
        }

        [Test]
        public void should_send_a_year_in_the_title_as_the_year_filter()
        {
            Subject.SearchForNewMovie("The Matrix (1999)");

            _searchRequest.Url.Query.Should().Contain("q=the%2Bmatrix");
            _searchRequest.Url.Query.Should().Contain("year=1999");
        }

        [Test]
        public void should_return_the_library_movie_when_it_is_already_added()
        {
            var inLibrary = new Movie { Id = 12, MovieMetadata = new MovieMetadata { TmdbId = 603, Title = "The Matrix" } };
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByTmdbId(603)).Returns(inLibrary);

            var results = Subject.SearchForNewMovie("The Matrix");

            results.First().Should().BeSameAs(inLibrary);
            results.First().Id.Should().Be(12);
            results.Last().Id.Should().Be(0);
        }

        [Test]
        public void should_look_up_a_tmdb_id_directly()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<MovieResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest r) =>
                {
                    _searchRequest = r;

                    var json = @"{ ""tmdbId"": 603, ""imdbId"": ""tt0133093"", ""title"": ""The Matrix"", ""originalTitle"": ""The Matrix"", ""year"": 1999, ""images"": [], ""genres"": [], ""alternativeTitles"": [], ""translations"": [], ""certifications"": [], ""recommendations"": [], ""credits"": { ""cast"": [], ""crew"": [] } }";

                    return new HttpResponse<MovieResource>(new HttpResponse(r, new HttpHeader(), json, System.Net.HttpStatusCode.OK));
                });

            var results = Subject.SearchForNewMovie("tmdb:603");

            results.Should().ContainSingle().Which.TmdbId.Should().Be(603);
            _searchRequest.Url.FullUri.Should().Contain("movie/603");
        }

        [Test]
        public void should_return_nothing_for_an_invalid_tmdb_id()
        {
            Subject.SearchForNewMovie("tmdb:abc").Should().BeEmpty();
            Subject.SearchForNewMovie("tmdb:").Should().BeEmpty();
        }

        [Test]
        public void should_return_nothing_for_an_imdb_id_with_spaces()
        {
            Subject.SearchForNewMovie("imdb:tt 1234567").Should().BeEmpty();
        }

        [Test]
        public void should_report_a_service_that_cannot_be_reached()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<List<MovieResource>>(It.IsAny<HttpRequest>()))
                .Throws(new System.Net.WebException("no route"));

            Assert.Throws<RadarrMetadataException>(() => Subject.SearchForNewMovie("The Matrix"));
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }
    }
}
