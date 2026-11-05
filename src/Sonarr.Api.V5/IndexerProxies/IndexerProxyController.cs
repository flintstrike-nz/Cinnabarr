using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.IndexerEngine.IndexerProxies;
using NzbDrone.SignalR;
using Sonarr.Api.V5.Provider;
using Sonarr.Http;

namespace Sonarr.Api.V5.IndexerProxies;

// Proxies (FlareSolverr, HTTP, SOCKS) the indexer engine sends a site's requests through. A proxy applies to the
// indexers that share one of its tags.
[V5ApiController]
public class IndexerProxyController : ProviderControllerBase<IndexerProxyResource, IndexerProxyBulkResource, IIndexerProxy, IndexerProxyDefinition>
{
    public static readonly IndexerProxyResourceMapper ResourceMapper = new();
    public static readonly IndexerProxyBulkResourceMapper BulkResourceMapper = new();

    public IndexerProxyController(IBroadcastSignalRMessage signalRBroadcaster, IndexerProxyFactory indexerProxyFactory)
        : base(signalRBroadcaster, indexerProxyFactory, "indexerproxy", ResourceMapper, BulkResourceMapper)
    {
    }

    [NonAction]
    public override Results<Ok<IEnumerable<IndexerProxyResource>>, BadRequest> UpdateProvider([FromBody] IndexerProxyBulkResource providerResource)
    {
        throw new NotImplementedException();
    }

    [NonAction]
    public override NoContent DeleteProviders([FromBody] IndexerProxyBulkResource resource)
    {
        throw new NotImplementedException();
    }
}
