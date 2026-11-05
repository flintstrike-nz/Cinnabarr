using NzbDrone.Common.Http;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.IndexerEngine.IndexerProxies
{
    public interface IIndexerProxy : IProvider
    {
        HttpRequest PreRequest(HttpRequest request);
        HttpResponse PostResponse(HttpResponse response);
    }
}
