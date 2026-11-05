using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.IndexerEngine.IndexerProxies
{
    public interface IIndexerProxySettings : IProviderConfig
    {
        string Host { get; set; }
    }
}
