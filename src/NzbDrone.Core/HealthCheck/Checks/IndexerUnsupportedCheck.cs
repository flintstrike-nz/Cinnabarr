using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(ProviderAddedEvent<IIndexer>))]
    [CheckOn(typeof(ProviderUpdatedEvent<IIndexer>))]
    [CheckOn(typeof(ProviderDeletedEvent<IIndexer>))]
    public class IndexerUnsupportedCheck : HealthCheckBase
    {
        private readonly IIndexerFactory _indexerFactory;

        public IndexerUnsupportedCheck(IIndexerFactory indexerFactory, ILocalizationService localizationService)
            : base(localizationService)
        {
            _indexerFactory = indexerFactory;
        }

        public override HealthCheck Check()
        {
            var unsupported = _indexerFactory.Unsupported();

            if (unsupported.Empty())
            {
                return new HealthCheck(GetType());
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Warning,
                HealthCheckReason.IndexerUnsupported,
                _localizationService.GetLocalizedString("IndexerUnsupportedHealthCheckMessage", new Dictionary<string, object>
                {
                    { "indexerNames", string.Join(", ", unsupported.Select(i => i.Name)) }
                }));
        }
    }
}
