using System;
using Jellyfin.Plugin.Cinnabarr.Client;
using Xunit;

namespace Jellyfin.Plugin.Cinnabarr.Tests
{
    public class CinnabarrClientTests
    {
        [Theory]
        [InlineData("http://localhost:8989", "request/status?tmdbIds=603", "http://localhost:8989/api/v5/request/status?tmdbIds=603")]
        [InlineData("http://localhost:8989/", "queue", "http://localhost:8989/api/v5/queue")]
        [InlineData("https://nas.lan/cinnabarr", "movie/lookup?term=a%20b", "https://nas.lan/cinnabarr/api/v5/movie/lookup?term=a%20b")]
        public void Builds_urls_under_api_v5(string baseUrl, string path, string expected)
        {
            Assert.Equal(expected, CinnabarrClient.BuildUri(baseUrl, path).AbsoluteUri);
        }

        [Theory]
        [InlineData("")]
        [InlineData("localhost:8989")]
        [InlineData("ftp://localhost")]
        [InlineData("http://localhost:8989/?x=1")]
        public void Refuses_bad_base_urls(string baseUrl)
        {
            Assert.Throws<ArgumentException>(() => CinnabarrClient.BuildUri(baseUrl, "queue"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("/api/v3/queue")]
        [InlineData("../v3/queue")]
        [InlineData("queue/../../v3")]
        [InlineData("queue/%2e%2e/x")]
        [InlineData("http://evil.example/")]
        [InlineData("queue\\x")]
        [InlineData("queue//x")]
        public void Refuses_paths_that_leave_api_v5(string path)
        {
            Assert.Throws<ArgumentException>(() => CinnabarrClient.BuildUri("http://localhost:8989", path));
        }
    }
}
