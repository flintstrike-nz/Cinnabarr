using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Cinnabarr.Api;
using Jellyfin.Plugin.Cinnabarr.Requests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Cinnabarr.Tests
{
    public class ProxyHelperTests
    {
        [Fact]
        public void Query_escapes_and_skips_empty_values()
        {
            Assert.Equal("?term=a%20%26%20b&x=1", CinnabarrProxyController.Query(("term", "a & b"), ("empty", null), ("blank", string.Empty), ("x", "1")));
            Assert.Equal(string.Empty, CinnabarrProxyController.Query(("a", null)));
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("1", true)]
        [InlineData("1,22,333", true)]
        [InlineData("1,", false)]
        [InlineData("1;2", false)]
        [InlineData("-1", false)]
        [InlineData("1&x=2", false)]
        public void Id_lists_are_numbers_and_commas(string? ids, bool valid)
        {
            Assert.Equal(valid, CinnabarrProxyController.IsIdList(ids));
        }

        [Fact]
        public void Id_lists_are_capped()
        {
            Assert.True(CinnabarrProxyController.IsIdList(string.Join(",", Enumerable.Range(1, 200))));
            Assert.False(CinnabarrProxyController.IsIdList(string.Join(",", Enumerable.Range(1, 201))));
        }

        [Theory]
        [InlineData(null, "removeFromClient")]
        [InlineData("IGNORE", "ignore")]
        [InlineData("removefromclient", "removeFromClient")]
        [InlineData("changeCategory", null)]
        public void OneOf_allows_only_listed_values(string? input, string? expected)
        {
            Assert.Equal(expected, CinnabarrProxyController.OneOf(input, "removeFromClient", "removeFromClient", "ignore"));
        }
    }

    public class RequestLogTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "cinnabarr-log-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [Fact]
        public void Newest_first_and_filtered_by_user()
        {
            var log = new RequestLog(() => _folder, NullLogger<RequestLog>.Instance);
            var alice = Guid.NewGuid();
            var bob = Guid.NewGuid();

            log.Add(new RequestLogEntry { UserId = alice, MediaType = "movie", ExternalId = 1 });
            log.Add(new RequestLogEntry { UserId = bob, MediaType = "movie", ExternalId = 2 });
            log.Add(new RequestLogEntry { UserId = alice, MediaType = "series", ExternalId = 3 });

            Assert.Equal(new[] { 3, 2, 1 }, log.Recent(null, 10).Select(e => e.ExternalId));
            Assert.Equal(new[] { 3, 1 }, log.Recent(alice, 10).Select(e => e.ExternalId));
            Assert.Single(log.Recent(null, 1));
        }

        [Fact]
        public void Empty_when_nothing_logged()
        {
            Assert.Empty(new RequestLog(() => _folder, NullLogger<RequestLog>.Instance).Recent(null, 10));
            Assert.Empty(new RequestLog(() => null, NullLogger<RequestLog>.Instance).Recent(null, 10));
        }

        [Fact]
        public void Trims_when_it_grows_past_twice_the_limit()
        {
            var log = new RequestLog(() => _folder, NullLogger<RequestLog>.Instance);

            for (var i = 0; i <= RequestLog.MaxEntries * 2; i++)
            {
                log.Add(new RequestLogEntry { MediaType = "movie", ExternalId = i + 1 });
            }

            Assert.Equal(RequestLog.MaxEntries, File.ReadAllLines(Path.Combine(_folder, "requests.jsonl")).Length);
            Assert.Equal(RequestLog.MaxEntries * 2 + 1, log.Recent(null, 1)[0].ExternalId);
        }
    }
}
