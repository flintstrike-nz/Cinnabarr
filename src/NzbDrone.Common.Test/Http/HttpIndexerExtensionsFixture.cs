using System.Net;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.Http
{
    // The HTTP pieces the indexer engine ported from Prowlarr relies on (roadmap Phase 3).
    [TestFixture]
    public class HttpIndexerExtensionsFixture : TestBase
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding("iso-8859-1");

        [Test]
        public void should_use_the_requests_encoding_for_content()
        {
            var request = new HttpRequest("http://host/") { Encoding = Latin1 };

            request.SetContent("café");

            request.ContentData.Should().Equal(Latin1.GetBytes("café"));
            request.GetContent().Should().Be("café");
        }

        [Test]
        public void should_keep_using_the_content_type_encoding_without_a_request_encoding()
        {
            var request = new HttpRequest("http://host/");

            request.SetContent("café");

            request.ContentData.Should().Equal(Encoding.UTF8.GetBytes("café"));
            request.GetContent().Should().Be("café");
        }

        [Test]
        public void should_pass_the_builders_encoding_to_the_request()
        {
            var builder = new HttpRequestBuilder("http://host/").SetEncoding(Latin1);

            builder.Build().Encoding.Should().Be(Latin1);
            new HttpRequestBuilder("http://host/").Build().Encoding.Should().BeNull();
        }

        [Test]
        public void should_set_several_headers_at_once()
        {
            var request = new HttpRequestBuilder("http://host/")
                .SetHeaders(new System.Collections.Generic.Dictionary<string, string> { { "X-One", "1" }, { "User-Agent", "Test" } })
                .Build();

            request.Headers["X-One"].Should().Be("1");
            request.Headers.UserAgent.Should().Be("Test");
        }

        [Test]
        public void should_expose_common_response_headers()
        {
            var headers = new HttpHeader { Vary = "Accept-Encoding", ContentEncoding = "gzip" };

            headers["Vary"].Should().Be("Accept-Encoding");
            headers.ContentEncoding.Should().Be("gzip");
        }

        [Test]
        public void should_resolve_redirect_urls_against_the_request()
        {
            var request = new HttpRequest("http://host/login/page");

            Response(request, "Location", "/home").RedirectUrl.Should().Be("http://host/home");
            Response(request, "Location", "https://other/x").RedirectUrl.Should().Be("https://other/x");
            Response(request, "Refresh", "0; url=/next").RedirectUrl.Should().Be("http://host/next");
            Response(request, "X-Other", "1").RedirectUrl.Should().BeEmpty();
        }

        [Test]
        public void should_url_encode_and_decode_with_an_encoding()
        {
            "café au lait".UrlEncode(Latin1).Should().Be("caf%E9+au+lait");
            "caf%E9+au+lait".UrlDecode(Latin1).Should().Be("café au lait");
            "".UrlEncode(Latin1).Should().BeEmpty();
        }

        [TestCase("12345", true)]
        [TestCase("", true)]
        [TestCase("12a45", false)]
        public void should_detect_all_digits(string input, bool expected)
        {
            input.IsAllDigits().Should().Be(expected);
        }

        [TestCase("2020-05-01", true)]
        [TestCase("not a date", false)]
        public void should_detect_valid_dates(string input, bool expected)
        {
            input.IsValidDate().Should().Be(expected);
        }

        private static HttpResponse Response(HttpRequest request, string header, string value)
        {
            var headers = new HttpHeader();
            headers.Add(header, value);

            return new HttpResponse(request, headers, string.Empty, HttpStatusCode.Found);
        }
    }
}
