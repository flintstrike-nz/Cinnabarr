using Jellyfin.Plugin.Cinnabarr.Web;
using Xunit;

namespace Jellyfin.Plugin.Cinnabarr.Tests
{
    public class IndexInjectionTests
    {
        [Theory]
        [InlineData("GET", "/web/index.html", true)]
        [InlineData("GET", "/web/", true)]
        [InlineData("GET", "/jellyfin/web/index.html", true)]
        [InlineData("GET", "/WEB/INDEX.HTML", true)]
        [InlineData("HEAD", "/web/index.html", false)]
        [InlineData("POST", "/web/index.html", false)]
        [InlineData("GET", "/web/main.jellyfin.bundle.js", false)]
        [InlineData("GET", "/web/configurationpage", false)]
        [InlineData("GET", "", false)]
        public void Only_the_web_app_page_is_changed(string method, string path, bool expected)
        {
            Assert.Equal(expected, IndexInjectionMiddleware.IsIndexRequest(method, path));
        }

        [Fact]
        public void Adds_relative_tags_before_body_end()
        {
            var html = IndexInjectionMiddleware.Inject("<html><body><div></div></body></html>", "1.2.3.4");

            Assert.Equal(
                "<html><body><div></div><link rel=\"stylesheet\" href=\"../Cinnabarr/Web/client.css?v=1.2.3.4\" data-cinnabarr><script defer src=\"../Cinnabarr/Web/client.js?v=1.2.3.4\" data-cinnabarr></script></body></html>",
                html);
        }

        [Fact]
        public void Adds_nothing_twice_or_without_a_body()
        {
            var once = IndexInjectionMiddleware.Inject("<body></body>", "1");

            Assert.Same(once, IndexInjectionMiddleware.Inject(once, "1"));
            Assert.Equal("<p>no body</p>", IndexInjectionMiddleware.Inject("<p>no body</p>", "1"));
        }
    }
}
