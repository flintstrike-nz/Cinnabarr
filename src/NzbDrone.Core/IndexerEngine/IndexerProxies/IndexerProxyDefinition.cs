using System.Linq;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.IndexerEngine.IndexerProxies
{
    public class IndexerProxyDefinition : ProviderDefinition
    {
        public override bool Enable => Tags.Any();
    }
}
