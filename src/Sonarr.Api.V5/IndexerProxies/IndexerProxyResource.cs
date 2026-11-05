using NzbDrone.Core.IndexerEngine.IndexerProxies;
using Sonarr.Api.V5.Provider;

namespace Sonarr.Api.V5.IndexerProxies;

public class IndexerProxyResource : ProviderResource<IndexerProxyResource>
{
}

public class IndexerProxyResourceMapper : ProviderResourceMapper<IndexerProxyResource, IndexerProxyDefinition>
{
}

public class IndexerProxyBulkResource : ProviderBulkResource<IndexerProxyBulkResource>
{
}

public class IndexerProxyBulkResourceMapper : ProviderBulkResourceMapper<IndexerProxyBulkResource, IndexerProxyDefinition>
{
}
