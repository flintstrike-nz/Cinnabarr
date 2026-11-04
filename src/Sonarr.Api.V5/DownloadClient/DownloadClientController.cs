using FluentValidation;
using NzbDrone.Core.Download;
using NzbDrone.SignalR;
using Sonarr.Api.V5.Provider;
using Sonarr.Http;

namespace Sonarr.Api.V5.DownloadClient;

[V5ApiController]
public class DownloadClientController : ProviderControllerBase<DownloadClientResource, DownloadClientBulkResource, IDownloadClient, DownloadClientDefinition>
{
    public static readonly DownloadClientResourceMapper ResourceMapper = new();
    public static readonly DownloadClientBulkResourceMapper BulkResourceMapper = new();

    public DownloadClientController(IBroadcastSignalRMessage signalRBroadcaster, IDownloadClientFactory downloadClientFactory)
        : base(signalRBroadcaster, downloadClientFactory, "downloadclient", ResourceMapper, BulkResourceMapper)
    {
        SharedValidator.RuleFor(c => c.Priority).InclusiveBetween(1, 50);

        // Embarr downloads only with its built-in engine (roadmap §4.4), which it sets up itself.
        PostValidator.RuleFor(c => c.Implementation).Must(_ => false).WithMessage("Embarr downloads only with its built-in engine; other download clients can't be added");
    }
}
