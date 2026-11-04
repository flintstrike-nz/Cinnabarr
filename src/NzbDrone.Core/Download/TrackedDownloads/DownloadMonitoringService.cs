using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class DownloadMonitoringService : IExecute<RefreshMonitoredDownloadsCommand>,
                                             IExecute<CheckForFinishedDownloadCommand>,
                                             IHandle<EpisodeGrabbedEvent>,
                                             IHandle<EpisodeImportedEvent>,
                                             IHandle<ManualInteractionRequiredEvent>,
                                             IHandle<DownloadsProcessedEvent>,
                                             IHandle<TrackedDownloadsRemovedEvent>,
                                             IHandle<ProviderUpdatedEvent<IDownloadClient>>,
                                             IHandle<ProviderDeletedEvent<IDownloadClient>>
    {
        private readonly IDownloadClientStatusService _downloadClientStatusService;
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _manageCommandQueue;
        private readonly IConfigService _configService;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly IEngineReconcileService _engineReconcileService;
        private readonly IEngineTorrentPreparationService _engineTorrentPreparationService;
        private readonly IEngineFileImportService _engineFileImportService;
        private readonly Logger _logger;
        private readonly Debouncer _refreshDebounce;

        public DownloadMonitoringService(IDownloadClientStatusService downloadClientStatusService,
                                         IDownloadClientFactory downloadClientFactory,
                                         IEventAggregator eventAggregator,
                                         IManageCommandQueue manageCommandQueue,
                                         IConfigService configService,
                                         IFailedDownloadService failedDownloadService,
                                         ICompletedDownloadService completedDownloadService,
                                         ITrackedDownloadService trackedDownloadService,
                                         IEngineClientRegistry engineClientRegistry,
                                         IEngineReconcileService engineReconcileService,
                                         IEngineTorrentPreparationService engineTorrentPreparationService,
                                         IEngineFileImportService engineFileImportService,
                                         Logger logger)
        {
            _downloadClientStatusService = downloadClientStatusService;
            _downloadClientFactory = downloadClientFactory;
            _eventAggregator = eventAggregator;
            _manageCommandQueue = manageCommandQueue;
            _configService = configService;
            _failedDownloadService = failedDownloadService;
            _completedDownloadService = completedDownloadService;
            _trackedDownloadService = trackedDownloadService;
            _engineClientRegistry = engineClientRegistry;
            _engineReconcileService = engineReconcileService;
            _engineTorrentPreparationService = engineTorrentPreparationService;
            _engineFileImportService = engineFileImportService;
            _logger = logger;

            _refreshDebounce = new Debouncer(QueueRefresh, TimeSpan.FromSeconds(5));
        }

        private void QueueRefresh()
        {
            _manageCommandQueue.Push(new RefreshMonitoredDownloadsCommand(), CommandPriority.High);
        }

        private void Refresh()
        {
            _refreshDebounce.Pause();
            try
            {
                var downloadClients = _downloadClientFactory.DownloadHandlingEnabled();

                var allTrackedDownloads = new List<TrackedDownload>();
                var trackedDownloads = new List<TrackedDownload>();

                foreach (var downloadClient in downloadClients)
                {
                    var clientTrackedDownloads = ProcessClientDownloads(downloadClient);

                    allTrackedDownloads.AddRange(clientTrackedDownloads);
                    trackedDownloads.AddRange(clientTrackedDownloads.Where(DownloadIsTrackable));
                }

                // IsTrackable decides what is still processed (import, failure handling).
                // Visibility is separate: the queue gets every download and decides what to show,
                // so torrents in the built-in engine stay listed after import (roadmap §4.4).
                _trackedDownloadService.UpdateTrackable(trackedDownloads);
                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(allTrackedDownloads));
                _manageCommandQueue.Push(new ProcessMonitoredDownloadsCommand(), CommandPriority.High);
            }
            finally
            {
                _refreshDebounce.Resume();
            }
        }

        private List<TrackedDownload> ProcessClientDownloads(IDownloadClient downloadClient)
        {
            var downloadClientItems = new List<DownloadClientItem>();
            var trackedDownloads = new List<TrackedDownload>();
            var missingFromEngine = new List<TrackedDownload>();

            if (_engineClientRegistry.IsEngineClient(downloadClient.Definition.Id))
            {
                PrepareEngineTorrents(downloadClient);
            }

            try
            {
                downloadClientItems = downloadClient.GetItems().ToList();

                _downloadClientStatusService.RecordSuccess(downloadClient.Definition.Id);

                var downloadIds = downloadClientItems.Select(i => i.DownloadId).ToList();

                // Only reconcile against a torrent list the engine actually returned.
                if (_engineClientRegistry.IsEngineClient(downloadClient.Definition.Id))
                {
                    missingFromEngine = FindMissingFromEngine(downloadClient, downloadIds);
                }

                _trackedDownloadService.StopTrackingMissing(downloadClient.Definition.Id,
                    downloadIds.Concat(missingFromEngine.Select(t => t.DownloadItem.DownloadId)).ToList());
            }
            catch (Exception ex)
            {
                // TODO: Stop tracking items for the offline client

                _downloadClientStatusService.RecordFailure(downloadClient.Definition.Id);
                _logger.Warn(ex, "Unable to retrieve queue and history items from " + downloadClient.Definition.Name);
            }

            foreach (var downloadItem in downloadClientItems)
            {
                var item = ProcessClientItem(downloadClient, downloadItem);
                trackedDownloads.AddIfNotNull(item);
            }

            trackedDownloads.AddRange(missingFromEngine);

            // After tracking, so each torrent's tracked download (and its ignored or failed state) is known. Like the
            // folder import, it only runs with completed download handling on.
            if (_configService.EnableCompletedDownloadHandling && _engineClientRegistry.IsEngineClient(downloadClient.Definition.Id))
            {
                ImportEngineFiles(downloadClient);
            }

            return trackedDownloads;
        }

        private void PrepareEngineTorrents(IDownloadClient downloadClient)
        {
            try
            {
                _engineTorrentPreparationService.Prepare(downloadClient);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to prepare new torrents in the engine");
            }
        }

        private void ImportEngineFiles(IDownloadClient downloadClient)
        {
            try
            {
                _engineFileImportService.Process(downloadClient);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to import completed files from the engine");
            }
        }

        private List<TrackedDownload> FindMissingFromEngine(IDownloadClient downloadClient, List<string> downloadIds)
        {
            try
            {
                return _engineReconcileService.FindMissing((DownloadClientDefinition)downloadClient.Definition, downloadIds);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to check for grabs missing from the engine");
                return new List<TrackedDownload>();
            }
        }

        private TrackedDownload ProcessClientItem(IDownloadClient downloadClient, DownloadClientItem downloadItem)
        {
            TrackedDownload trackedDownload = null;

            try
            {
                trackedDownload =
                    _trackedDownloadService.TrackDownload((DownloadClientDefinition)downloadClient.Definition,
                        downloadItem);

                if (trackedDownload is { State: TrackedDownloadState.Downloading or TrackedDownloadState.ImportBlocked })
                {
                    _failedDownloadService.Check(trackedDownload);
                    _completedDownloadService.Check(trackedDownload);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't process tracked download {0}", downloadItem.Title);
            }

            return trackedDownload;
        }

        private bool DownloadIsTrackable(TrackedDownload trackedDownload)
        {
            // Listed for visibility only; there is nothing in the engine to import or fail.
            if (trackedDownload.IsMissingFromEngine)
            {
                return false;
            }

            // If the download has already been imported, failed or the user ignored it don't track it
            if (trackedDownload.State == TrackedDownloadState.Imported ||
                trackedDownload.State == TrackedDownloadState.Failed ||
                trackedDownload.State == TrackedDownloadState.Ignored)
            {
                return false;
            }

            // If CDH is disabled and the download status is complete don't track it
            if (!_configService.EnableCompletedDownloadHandling && trackedDownload.DownloadItem.Status == DownloadItemStatus.Completed)
            {
                return false;
            }

            return true;
        }

        public void Execute(RefreshMonitoredDownloadsCommand message)
        {
            Refresh();
        }

        public void Execute(CheckForFinishedDownloadCommand message)
        {
            _logger.Warn("A third party app used the deprecated CheckForFinishedDownload command, it should be updated RefreshMonitoredDownloads instead");
            Refresh();
        }

        public void Handle(EpisodeGrabbedEvent message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(ManualInteractionRequiredEvent message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(EpisodeImportedEvent message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(ProviderUpdatedEvent<IDownloadClient> message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(ProviderDeletedEvent<IDownloadClient> message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(DownloadsProcessedEvent message)
        {
            PublishRefreshed();
        }

        public void Handle(TrackedDownloadsRemovedEvent message)
        {
            PublishRefreshed();
        }

        private void PublishRefreshed()
        {
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads();

            // Downloads that are no longer eligible for processing are marked as such, but
            // still published so the queue can keep showing built-in engine torrents.
            foreach (var trackedDownload in trackedDownloads.Where(t => t.IsTrackable && !DownloadIsTrackable(t)))
            {
                trackedDownload.IsTrackable = false;
            }

            _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(trackedDownloads));
        }
    }
}
