using FluentValidation;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Engine;
using NzbDrone.Core.Validation;
using NzbDrone.SignalR;
using Sonarr.Api.V5.Provider;
using Sonarr.Http;

namespace Sonarr.Api.V5.Indexers;

[V5ApiController]
public class IndexerController : ProviderControllerBase<IndexerResource, IndexerBulkResource, IIndexer, IndexerDefinition>
{
    public static readonly IndexerBulkResourceMapper BulkResourceMapper = new();

    public IndexerController(IBroadcastSignalRMessage signalRBroadcaster,
        IndexerFactory indexerFactory,
        IEngineIndexerProvider engineIndexerProvider,
        DownloadClientExistsValidator downloadClientExistsValidator)
        : base(signalRBroadcaster, indexerFactory, "indexer", new IndexerResourceMapper(engineIndexerProvider), BulkResourceMapper)
    {
        SharedValidator.RuleFor(c => c.Priority).InclusiveBetween(1, 50);
        SharedValidator.RuleFor(c => c.SeasonSearchMaximumSingleEpisodeAge).GreaterThanOrEqualTo(0);
        SharedValidator.RuleFor(c => c.DownloadClientId).SetValidator(downloadClientExistsValidator);
    }
}
