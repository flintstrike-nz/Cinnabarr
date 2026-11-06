using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public interface IDownloadService
    {
        Task DownloadReport(RemoteMedia remote, int? downloadClientId);
    }

    public class DownloadService : IDownloadService
    {
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IDownloadClientStatusService _downloadClientStatusService;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly IRateLimitService _rateLimitService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ISeedConfigProvider _seedConfigProvider;
        private readonly Logger _logger;

        public DownloadService(IProvideDownloadClient downloadClientProvider,
                               IDownloadClientStatusService downloadClientStatusService,
                               IIndexerFactory indexerFactory,
                               IIndexerStatusService indexerStatusService,
                               IRateLimitService rateLimitService,
                               IEventAggregator eventAggregator,
                               ISeedConfigProvider seedConfigProvider,
                               Logger logger)
        {
            _downloadClientProvider = downloadClientProvider;
            _downloadClientStatusService = downloadClientStatusService;
            _indexerFactory = indexerFactory;
            _indexerStatusService = indexerStatusService;
            _rateLimitService = rateLimitService;
            _eventAggregator = eventAggregator;
            _seedConfigProvider = seedConfigProvider;
            _logger = logger;
        }

        public async Task DownloadReport(RemoteMedia remote, int? downloadClientId)
        {
            var filterBlockedClients = remote.Release.PendingReleaseReason == PendingReleaseReason.DownloadClientUnavailable;

            var tags = remote.Tags;

            if (downloadClientId.HasValue)
            {
                var specificClient = _downloadClientProvider.Get(downloadClientId.Value);
                await DownloadReport(remote, specificClient);

                return;
            }

            var availableClients = _downloadClientProvider.GetDownloadClients(
                remote.Release.DownloadProtocol,
                remote.Release.IndexerId,
                filterBlockedClients,
                tags).ToList();

            if (!availableClients.Any())
            {
                throw new DownloadClientUnavailableException($"No {remote.Release.DownloadProtocol} download client available");
            }

            var triedClients = new HashSet<int>();

            foreach (var downloadClient in availableClients)
            {
                if (triedClients.Contains(downloadClient.Definition.Id))
                {
                    continue;
                }

                try
                {
                    _logger.Debug("Attempting download with client: {0}", downloadClient.Definition.Name);
                    await DownloadReport(remote, downloadClient);

                    _downloadClientProvider.ReportSuccessfulDownloadClient(
                        remote.Release.DownloadProtocol,
                        downloadClient.Definition.Id);

                    return;
                }
                catch (DownloadClientException ex)
                {
                    _logger.Trace(ex, "Unable to add report to download client: {0}", downloadClient.Definition.Name);
                    triedClients.Add(downloadClient.Definition.Id);
                }
                catch (Exception ex)
                {
                    // Rethrow specific exceptions that should not trigger a fallback
                    if (ex is ReleaseDownloadException)
                    {
                        throw;
                    }

                    _logger.Trace(ex, "Unable to add report to download client: {0}", downloadClient.Definition.Name);
                    triedClients.Add(downloadClient.Definition.Id);
                }
            }

            throw new DownloadClientUnavailableException("All '{0}' download clients failed", remote.Release.DownloadProtocol);
        }

        private async Task DownloadReport(RemoteMedia remote, IDownloadClient downloadClient)
        {
            switch (remote)
            {
                case RemoteEpisode remoteEpisode:
                    Ensure.That(remoteEpisode.Series, () => remoteEpisode.Series).IsNotNull();
                    Ensure.That(remoteEpisode.Episodes, () => remoteEpisode.Episodes).HasItems();
                    break;
                case RemoteMovie remoteMovie:
                    Ensure.That(remoteMovie.Movie, () => remoteMovie.Movie).IsNotNull();
                    break;
            }

            var downloadTitle = remote.Release.Title;

            if (downloadClient == null)
            {
                throw new DownloadClientUnavailableException($"{remote.Release.DownloadProtocol} Download client isn't configured yet");
            }

            // Get the seed configuration for this release.
            remote.SeedConfiguration = _seedConfigProvider.GetSeedConfiguration(remote);

            // Limit grabs to 2 per second.
            if (remote.Release.DownloadUrl.IsNotNullOrWhiteSpace() && !remote.Release.DownloadUrl.StartsWith("magnet:"))
            {
                var url = new HttpUri(remote.Release.DownloadUrl);
                await _rateLimitService.WaitAndPulseAsync(url.Host, TimeSpan.FromSeconds(2));
            }

            IIndexer indexer = null;

            if (remote.Release.IndexerId > 0)
            {
                indexer = _indexerFactory.GetInstance(_indexerFactory.Get(remote.Release.IndexerId));
            }

            string downloadClientId;
            try
            {
                downloadClientId = await downloadClient.Download(remote, indexer);
                _downloadClientStatusService.RecordSuccess(downloadClient.Definition.Id);
                _indexerStatusService.RecordSuccess(remote.Release.IndexerId);
            }
            catch (ReleaseUnavailableException)
            {
                _logger.Trace("Release {0} no longer available on indexer.", remote);
                throw;
            }
            catch (ReleaseBlockedException)
            {
                _logger.Trace("Release {0} previously added to blocklist, not sending to download client again.", remote);
                throw;
            }
            catch (DownloadClientRejectedReleaseException)
            {
                _logger.Trace("Release {0} rejected by download client, possible duplicate.", remote);
                throw;
            }
            catch (ReleaseDownloadException ex)
            {
                if (ex.InnerException is TooManyRequestsException http429)
                {
                    _indexerStatusService.RecordFailure(remote.Release.IndexerId, http429.RetryAfter);
                }
                else
                {
                    _indexerStatusService.RecordFailure(remote.Release.IndexerId);
                }

                throw;
            }

            _logger.ProgressInfo("Report sent to {0}. Indexer {1}. {2}", downloadClient.Definition.Name, remote.Release.Indexer, downloadTitle);

            var downloadId = downloadClientId.IsNotNullOrWhiteSpace() ? downloadClientId : null;

            switch (remote)
            {
                case RemoteEpisode remoteEpisode:
                    _eventAggregator.PublishEvent(new EpisodeGrabbedEvent(remoteEpisode)
                    {
                        DownloadClient = downloadClient.Name,
                        DownloadClientId = downloadClient.Definition.Id,
                        DownloadClientName = downloadClient.Definition.Name,
                        DownloadId = downloadId
                    });
                    break;
                case RemoteMovie remoteMovie:
                    _eventAggregator.PublishEvent(new MovieGrabbedEvent(remoteMovie)
                    {
                        DownloadClient = downloadClient.Name,
                        DownloadClientId = downloadClient.Definition.Id,
                        DownloadClientName = downloadClient.Definition.Name,
                        DownloadId = downloadId
                    });
                    break;
            }
        }
    }
}
