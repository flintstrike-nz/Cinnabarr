using NzbDrone.Common.Http;

namespace NzbDrone.Core.IndexerEngine.IndexerProxies
{
    public interface IProxyTestRequestBuilder
    {
        IHttpRequestBuilderFactory Services { get; }
    }

    // Embarr doesn't call Prowlarr's cloud services. Testing a proxy only needs a reachable public host; the test only
    // looks for a 400 response from the proxy itself (roadmap §4.3).
    public class ProxyTestRequestBuilder : IProxyTestRequestBuilder
    {
        public ProxyTestRequestBuilder()
        {
            Services = new HttpRequestBuilder("https://example.com/").CreateFactory();
        }

        public IHttpRequestBuilderFactory Services { get; private set; }
    }
}
