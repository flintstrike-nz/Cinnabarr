using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.IndexerEngine.Indexers
{
    public interface IIndexerSettings : IProviderConfig
    {
        string BaseUrl { get; set; }
        IndexerBaseSettings BaseSettings { get; set; }
    }
}
