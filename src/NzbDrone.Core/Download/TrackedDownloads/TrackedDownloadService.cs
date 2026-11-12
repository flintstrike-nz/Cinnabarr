using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download.Aggregation;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public interface ITrackedDownloadService
    {
        TrackedDownload Find(string downloadId);
        void StopTracking(string downloadId);
        void StopTracking(List<string> downloadIds);
        TrackedDownload TrackDownload(DownloadClientDefinition downloadClient, DownloadClientItem downloadItem);
        List<TrackedDownload> GetTrackedDownloads();
        void UpdateTrackable(List<TrackedDownload> trackedDownloads);
        void StopTrackingMissing(int downloadClientId, List<string> downloadIds);

        // Lists a built-in engine grab whose torrent is gone from the engine (roadmap §4.4 rule 3).
        TrackedDownload TrackMissingFromEngine(DownloadClientDefinition downloadClient, DownloadHistory grab);
    }

    public class TrackedDownloadService : ITrackedDownloadService,
                                          IHandle<EpisodeGrabbedEvent>,
                                          IHandle<EpisodeInfoRefreshedEvent>,
                                          IHandle<SeriesAddedEvent>,
                                          IHandle<SeriesEditedEvent>,
                                          IHandle<SeriesBulkEditedEvent>,
                                          IHandle<SeriesDeletedEvent>,
                                          IHandle<MoviesDeletedEvent>
    {
        private readonly IParsingService _parsingService;
        private readonly IMovieParsingService _movieParsingService;
        private readonly IMovieHistoryService _movieHistoryService;
        private readonly IVersionSlotService _versionSlotService;
        private readonly IHistoryService _historyService;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IRemoteEpisodeAggregationService _aggregationService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly IEngineLinkService _engineLinkService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        private readonly ICached<TrackedDownload> _cache;

        public TrackedDownloadService(IParsingService parsingService,
                                      IMovieParsingService movieParsingService,
                                      IMovieHistoryService movieHistoryService,
                                      IVersionSlotService versionSlotService,
                                      IHistoryService historyService,
                                      ISeriesService seriesService,
                                      IEpisodeService episodeService,
                                      IDownloadHistoryService downloadHistoryService,
                                      IRemoteEpisodeAggregationService aggregationService,
                                      ICustomFormatCalculationService formatCalculator,
                                      IEngineClientRegistry engineClientRegistry,
                                      IEngineLinkService engineLinkService,
                                      IEventAggregator eventAggregator,
                                      ICacheManager cacheManager,
                                      Logger logger)
        {
            _parsingService = parsingService;
            _movieParsingService = movieParsingService;
            _movieHistoryService = movieHistoryService;
            _versionSlotService = versionSlotService;
            _historyService = historyService;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _downloadHistoryService = downloadHistoryService;
            _aggregationService = aggregationService;
            _formatCalculator = formatCalculator;
            _engineClientRegistry = engineClientRegistry;
            _engineLinkService = engineLinkService;
            _eventAggregator = eventAggregator;
            _logger = logger;

            _cache = cacheManager.GetCache<TrackedDownload>(GetType());
        }

        public TrackedDownload Find(string downloadId)
        {
            return _cache.Find(downloadId);
        }

        public void StopTracking(string downloadId)
        {
            var trackedDownload = _cache.Find(downloadId);

            _cache.Remove(downloadId);
            _eventAggregator.PublishEvent(new TrackedDownloadsRemovedEvent(new List<TrackedDownload> { trackedDownload }));
        }

        public void StopTracking(List<string> downloadIds)
        {
            var trackedDownloads = new List<TrackedDownload>();

            foreach (var downloadId in downloadIds)
            {
                var trackedDownload = _cache.Find(downloadId);
                _cache.Remove(downloadId);
                trackedDownloads.Add(trackedDownload);
            }

            _eventAggregator.PublishEvent(new TrackedDownloadsRemovedEvent(trackedDownloads));
        }

        public TrackedDownload TrackDownload(DownloadClientDefinition downloadClient, DownloadClientItem downloadItem)
        {
            var existingItem = Find(downloadItem.DownloadId);

            // Unlinked and incompletely linked torrents are re-evaluated on every poll, so a link made by Manual Import is picked up.
            if (existingItem != null && existingItem.State != TrackedDownloadState.Downloading && !existingItem.IsUnlinked && !existingItem.HasIncompleteLink)
            {
                LogItemChange(existingItem, existingItem.DownloadItem, downloadItem);

                existingItem.DownloadItem = downloadItem;
                existingItem.IsTrackable = true;

                return existingItem;
            }

            var trackedDownload = new TrackedDownload
            {
                DownloadClient = downloadClient.Id,
                DownloadItem = downloadItem,
                Protocol = downloadClient.Protocol,
                IsTrackable = true,
                HasNotifiedManualInteractionRequired = existingItem?.HasNotifiedManualInteractionRequired ?? false
            };

            var isEngine = _engineClientRegistry.IsEngineClient(downloadClient.Id);

            try
            {
                var historyItems = FindHistory(downloadItem.DownloadId);

                // Cinnabarr's database may have lost the grab (for example after restoring an older backup):
                // rebuild it from the link sidecar the engine keeps (roadmap §4.4 rule 5).
                // A movie's grab is in its own history, so a torrent that has one is not lost.
                if (isEngine && historyItems.Empty() && FindMovieHistory(downloadItem.DownloadId).Empty() && _engineLinkService.TryRestore(downloadClient, downloadItem.DownloadId))
                {
                    historyItems = FindHistory(downloadItem.DownloadId);
                }

                // The info-hash is the only key for the built-in engine (roadmap §4.4 rule 1): a torrent
                // Cinnabarr has no record of is listed as Unlinked and is never matched to a series by name.
                // A movie's grab is kept in its own history.
                var movieHistory = historyItems.Empty() ? FindMovieHistory(downloadItem.DownloadId) : new List<MovieHistory>();
                var isMovie = movieHistory.Any();

                var isUnlinked = isEngine && historyItems.Empty() && !isMovie;

                var downloadHistory = _downloadHistoryService.GetLatestDownloadHistoryItem(downloadItem.DownloadId);

                if (downloadHistory != null)
                {
                    var state = GetStateFromHistory(downloadHistory.EventType);
                    trackedDownload.State = state;
                }

                // Built-in engine downloads take their series and episodes from the info-hash history below.
                var parsedEpisodeInfo = isEngine || isMovie ? null : Parser.Parser.ParseTitle(trackedDownload.DownloadItem.Title);

                if (parsedEpisodeInfo != null)
                {
                    trackedDownload.RemoteEpisode = downloadHistory is { EventType: DownloadHistoryEventType.DownloadImported }
                        ? _parsingService.Map(parsedEpisodeInfo, _seriesService.GetSeries(downloadHistory.SeriesId))
                        : _parsingService.Map(parsedEpisodeInfo, 0, 0, null);
                }

                if (historyItems.Any())
                {
                    var firstHistoryItem = historyItems.First();
                    var grabbedEvent = historyItems.FirstOrDefault(v => v.EventType == EpisodeHistoryEventType.Grabbed);

                    trackedDownload.Indexer = grabbedEvent?.Data?.GetValueOrDefault("indexer");
                    trackedDownload.Added = grabbedEvent?.Date;

                    if (isEngine)
                    {
                        MapFromLinkHistory(trackedDownload, historyItems);
                    }
                    else if (parsedEpisodeInfo == null ||
                        trackedDownload.RemoteEpisode?.Series == null ||
                        trackedDownload.RemoteEpisode.Episodes.Empty())
                    {
                        // Try parsing the original source title and if that fails, try parsing it as a special
                        // TODO: Pass the TVDB ID and TVRage IDs in as well so we have a better chance for finding the item
                        parsedEpisodeInfo = Parser.Parser.ParseTitle(firstHistoryItem.SourceTitle) ??
                                            _parsingService.ParseSpecialEpisodeTitle(parsedEpisodeInfo, firstHistoryItem.SourceTitle, 0, 0, null);

                        if (parsedEpisodeInfo != null)
                        {
                            trackedDownload.RemoteEpisode = _parsingService.Map(parsedEpisodeInfo,
                                firstHistoryItem.SeriesId,
                                historyItems.Where(v => v.EventType == EpisodeHistoryEventType.Grabbed)
                                    .Select(h => h.EpisodeId).Distinct());
                        }
                    }

                    if (trackedDownload.RemoteEpisode != null)
                    {
                        trackedDownload.RemoteEpisode.Release ??= new ReleaseInfo();
                        trackedDownload.RemoteEpisode.Release.Indexer = trackedDownload.Indexer;
                        trackedDownload.RemoteEpisode.Release.Title = trackedDownload.RemoteEpisode.ParsedEpisodeInfo?.ReleaseTitle;

                        if (Enum.TryParse(grabbedEvent?.Data?.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
                        {
                            trackedDownload.RemoteEpisode.Release.IndexerFlags = flags;
                        }

                        if (downloadHistory != null)
                        {
                            trackedDownload.RemoteEpisode.Release.IndexerId = downloadHistory.IndexerId;
                        }
                    }
                }

                if (isMovie)
                {
                    MapFromMovieHistory(trackedDownload, movieHistory, downloadHistory, downloadItem.TotalSize);
                }

                if (isUnlinked)
                {
                    MarkUnlinked(trackedDownload);
                }

                if (trackedDownload.RemoteEpisode != null)
                {
                    _aggregationService.Augment(trackedDownload.RemoteEpisode);

                    // Calculate custom formats
                    trackedDownload.RemoteEpisode.CustomFormats = _formatCalculator.ParseCustomFormat(trackedDownload.RemoteEpisode, downloadItem.TotalSize);
                }

                // Track it so it can be displayed in the queue even though we can't determine which series it is for
                if (trackedDownload.RemoteEpisode == null && trackedDownload.RemoteMovie == null)
                {
                    _logger.Trace("No Episode found for download '{0}'", trackedDownload.DownloadItem.Title);
                }
            }
            catch (MultipleSeriesFoundException e)
            {
                _logger.Debug(e, "Found multiple series for " + downloadItem.Title);

                trackedDownload.Warn("Unable to import automatically, found multiple series: {0}", string.Join(", ", e.Series));
            }
            catch (Exception e)
            {
                _logger.Debug(e, "Failed to find episode for " + downloadItem.Title);

                trackedDownload.Warn("Unable to parse episodes from title");
            }

            LogItemChange(trackedDownload, existingItem?.DownloadItem, trackedDownload.DownloadItem);

            _cache.Set(trackedDownload.DownloadItem.DownloadId, trackedDownload);
            return trackedDownload;
        }

        public TrackedDownload TrackMissingFromEngine(DownloadClientDefinition downloadClient, DownloadHistory grab)
        {
            var existingItem = Find(grab.DownloadId);

            if (existingItem is { IsMissingFromEngine: true })
            {
                return existingItem;
            }

            var size = grab.Release?.Size ?? 0;

            var downloadItem = new DownloadClientItem
            {
                DownloadId = grab.DownloadId,
                Title = grab.SourceTitle,
                TotalSize = size,
                RemainingSize = size,
                Status = DownloadItemStatus.Warning,
                Message = "Missing from engine",
                CanBeRemoved = false,
                CanMoveFiles = false,
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = downloadClient.Protocol,
                    Type = downloadClient.Implementation,
                    Id = downloadClient.Id,
                    Name = downloadClient.Name
                }
            };

            var trackedDownload = new TrackedDownload
            {
                DownloadClient = downloadClient.Id,
                DownloadItem = downloadItem,
                Protocol = downloadClient.Protocol,
                State = TrackedDownloadState.Downloading,
                IsTrackable = false,
                IsMissingFromEngine = true,
                Indexer = grab.Data?.GetValueOrDefault("Indexer"),
                Added = grab.Date
            };

            try
            {
                var historyItems = FindHistory(grab.DownloadId);

                if (historyItems.Any())
                {
                    MapFromLinkHistory(trackedDownload, historyItems);
                }
                else
                {
                    var movieHistory = FindMovieHistory(grab.DownloadId);

                    if (movieHistory.Any())
                    {
                        MapFromMovieHistory(trackedDownload, movieHistory, grab, size);
                    }
                }

                // Same release details as any other row (indexer, languages, custom formats, ...), so queue
                // filters and scores treat it alike.
                if (trackedDownload.RemoteEpisode != null)
                {
                    var remoteEpisode = trackedDownload.RemoteEpisode;

                    remoteEpisode.Release ??= new ReleaseInfo();
                    remoteEpisode.Release.Indexer = trackedDownload.Indexer;
                    remoteEpisode.Release.IndexerId = grab.IndexerId;
                    remoteEpisode.Release.Title = remoteEpisode.ParsedEpisodeInfo?.ReleaseTitle;

                    if (Enum.TryParse(historyItems.FirstOrDefault(h => h.EventType == EpisodeHistoryEventType.Grabbed)?.Data?.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
                    {
                        remoteEpisode.Release.IndexerFlags = flags;
                    }

                    _aggregationService.Augment(remoteEpisode);
                    remoteEpisode.CustomFormats = _formatCalculator.ParseCustomFormat(remoteEpisode, size);
                }
            }
            catch (Exception e)
            {
                _logger.Debug(e, "Failed to map missing engine download " + grab.SourceTitle);
            }

            trackedDownload.Warn(TrackedDownload.MissingFromEngineMessage);

            _logger.Warn("'{0}' was grabbed but is no longer in the engine", grab.SourceTitle);

            _cache.Set(grab.DownloadId, trackedDownload);
            return trackedDownload;
        }

        public List<TrackedDownload> GetTrackedDownloads()
        {
            return _cache.Values.ToList();
        }

        public void UpdateTrackable(List<TrackedDownload> trackedDownloads)
        {
            var untrackable = GetTrackedDownloads().ExceptBy(t => t.DownloadItem.DownloadId, trackedDownloads, t => t.DownloadItem.DownloadId, StringComparer.CurrentCulture).ToList();

            foreach (var trackedDownload in untrackable)
            {
                trackedDownload.IsTrackable = false;
            }
        }

        public void StopTrackingMissing(int downloadClientId, List<string> downloadIds)
        {
            var present = downloadIds.ToHashSet();
            var missing = GetTrackedDownloads().Where(t => t.DownloadClient == downloadClientId && !present.Contains(t.DownloadItem.DownloadId));

            foreach (var trackedDownload in missing)
            {
                _cache.Remove(trackedDownload.DownloadItem.DownloadId);
            }
        }

        private List<EpisodeHistory> FindHistory(string downloadId)
        {
            return _historyService.FindByDownloadId(downloadId)
                .OrderByDescending(h => h.Date)
                .ToList();
        }

        private List<MovieHistory> FindMovieHistory(string downloadId)
        {
            return _movieHistoryService.FindByDownloadId(downloadId)
                .OrderByDescending(h => h.Date)
                .ToList();
        }

        // Builds the movie a grab was for from its history. The movie can be gone (deleted since), which leaves the
        // download listed without one.
        private void MapFromMovieHistory(TrackedDownload trackedDownload, List<MovieHistory> history, DownloadHistory downloadHistory, long size)
        {
            var grabbed = history.FirstOrDefault(h => h.EventType == MovieHistoryEventType.Grabbed) ?? history.First();

            trackedDownload.Indexer = grabbed.Data?.GetValueOrDefault("indexer");
            trackedDownload.Added = grabbed.Date;

            var parsed = MovieParser.ParseMovieTitle(grabbed.SourceTitle) ?? new ParsedMovieInfo { MovieTitles = new List<string> { grabbed.SourceTitle } };

            if (grabbed.Quality != null)
            {
                parsed.Quality = grabbed.Quality;
            }

            RemoteMovie remoteMovie;

            try
            {
                remoteMovie = _movieParsingService.Map(parsed, grabbed.MovieId);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to find the movie for download '{0}'", grabbed.SourceTitle);
                remoteMovie = null;
            }

            if (remoteMovie?.Movie == null)
            {
                return;
            }

            // A download for one of the movie's extra versions is that version of it.
            try
            {
                remoteMovie.Movie = _versionSlotService.GetView(remoteMovie.Movie, grabbed.VersionSlotId);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to find the version of the movie for download '{0}'", grabbed.SourceTitle);
                return;
            }

            remoteMovie.Release ??= new ReleaseInfo();
            remoteMovie.Release.Indexer = trackedDownload.Indexer;
            remoteMovie.Release.Title = grabbed.SourceTitle;

            if (grabbed.Languages is { Count: > 0 })
            {
                remoteMovie.Languages = grabbed.Languages;
            }

            if (Enum.TryParse(grabbed.Data?.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
            {
                remoteMovie.Release.IndexerFlags = flags;
            }

            if (downloadHistory != null)
            {
                remoteMovie.Release.IndexerId = downloadHistory.IndexerId;
            }

            remoteMovie.CustomFormats = _formatCalculator.ParseCustomFormat(remoteMovie, size);

            trackedDownload.RemoteMovie = remoteMovie;
        }

        private void LogItemChange(TrackedDownload trackedDownload, DownloadClientItem existingItem, DownloadClientItem downloadItem)
        {
            if (existingItem == null ||
                existingItem.Status != downloadItem.Status ||
                existingItem.CanBeRemoved != downloadItem.CanBeRemoved ||
                existingItem.CanMoveFiles != downloadItem.CanMoveFiles)
            {
                _logger.Debug("Tracking '{0}:{1}': ClientState={2}{3} SonarrStage={4} Episode='{5}' OutputPath={6}.",
                    downloadItem.DownloadClientInfo.Name,
                    downloadItem.Title,
                    downloadItem.Status,
                    downloadItem.CanBeRemoved ? "" : downloadItem.CanMoveFiles ? " (busy)" : " (readonly)",
                    trackedDownload.State,
                    trackedDownload.RemoteEpisode?.ParsedEpisodeInfo,
                    downloadItem.OutputPath);
            }
        }

        // For the built-in engine the info-hash history decides the series and episodes (roadmap §4.4
        // rule 1); titles are only parsed for release details.
        private void MapFromLinkHistory(TrackedDownload trackedDownload, List<EpisodeHistory> historyItems)
        {
            var firstHistoryItem = historyItems.First();
            var title = trackedDownload.DownloadItem.Title;

            // An unparseable (for example obfuscated) name still links: the release details then come from
            // the history row instead of the title.
            var parsedEpisodeInfo = Parser.Parser.ParseTitle(title) ??
                                    Parser.Parser.ParseTitle(firstHistoryItem.SourceTitle) ??
                                    _parsingService.ParseSpecialEpisodeTitle(null, firstHistoryItem.SourceTitle, 0, 0, null) ??
                                    new ParsedEpisodeInfo
                                    {
                                        ReleaseTitle = firstHistoryItem.SourceTitle ?? title,
                                        Quality = firstHistoryItem.Quality,
                                        Languages = firstHistoryItem.Languages ?? new()
                                    };

            // Cinnabarr owns intent: the grabbed quality (including one restored from the sidecar) wins over
            // whatever the title parses as.
            var grabbedQuality = historyItems.FirstOrDefault(h => h.EventType == EpisodeHistoryEventType.Grabbed)?.Quality;

            if (grabbedQuality?.Quality != null)
            {
                parsedEpisodeInfo.Quality = grabbedQuality;
            }

            // Grabs record their episodes; a torrent linked by Manual Import only has import history.
            var grabbedEpisodeIds = historyItems.Where(h => h.EventType == EpisodeHistoryEventType.Grabbed).Select(h => h.EpisodeId);
            var episodeIds = (grabbedEpisodeIds.Any() ? grabbedEpisodeIds : historyItems.Select(h => h.EpisodeId).Where(id => id > 0))
                .ToHashSet();

            Series series;

            try
            {
                series = _seriesService.GetSeries(firstHistoryItem.SeriesId);
            }
            catch (ModelNotFoundException)
            {
                series = null;
            }

            // Episodes can disappear in a metadata refresh while their history stays. Never fill the gap by
            // name and never drop it silently: keep what still exists and hold the torrent for Manual Import.
            var episodes = series == null
                ? new List<Episode>()
                : _episodeService.GetEpisodeBySeries(series.Id).Where(e => episodeIds.Contains(e.Id)).ToList();

            trackedDownload.RemoteEpisode = series == null
                ? null
                : new RemoteEpisode { ParsedEpisodeInfo = parsedEpisodeInfo, Series = series, Episodes = episodes };

            // The hold only guards automatic import. Once the torrent is imported (for example by the Manual
            // Import the warning asks for), its grab rows may still name the removed episode: don't keep flagging it.
            if (trackedDownload.State != TrackedDownloadState.Imported &&
                (series == null || episodes.Count != episodeIds.Count))
            {
                trackedDownload.HasIncompleteLink = true;
                trackedDownload.Warn(TrackedDownload.IncompleteLinkMessage);
                BlockPendingImport(trackedDownload);
            }
        }

        // A pending import goes straight to Import, skipping Check: send it back through Check's link guards.
        private static void BlockPendingImport(TrackedDownload trackedDownload)
        {
            if (trackedDownload.State == TrackedDownloadState.ImportPending)
            {
                trackedDownload.State = TrackedDownloadState.ImportBlocked;
            }
        }

        // "Missing from engine" outranks link warnings: the torrent isn't there, so Manual Import can't help.
        private static void KeepMissingFromEngineWarning(TrackedDownload trackedDownload)
        {
            if (trackedDownload.IsMissingFromEngine)
            {
                trackedDownload.Warn(TrackedDownload.MissingFromEngineMessage);
            }
        }

        private static void MarkUnlinked(TrackedDownload trackedDownload)
        {
            trackedDownload.IsUnlinked = true;
            trackedDownload.HasIncompleteLink = false;
            trackedDownload.RemoteEpisode = null;
            trackedDownload.Warn(TrackedDownload.UnlinkedMessage);
            BlockPendingImport(trackedDownload);
        }

        private void UpdateCachedItem(TrackedDownload trackedDownload)
        {
            // A movie download is mapped from its history again (the movie may have been edited or deleted).
            if (trackedDownload.RemoteMovie != null)
            {
                var movieHistory = FindMovieHistory(trackedDownload.DownloadItem.DownloadId);

                trackedDownload.RemoteMovie = null;

                if (movieHistory.Any())
                {
                    MapFromMovieHistory(trackedDownload, movieHistory, _downloadHistoryService.GetLatestGrab(trackedDownload.DownloadItem.DownloadId), trackedDownload.DownloadItem.TotalSize);
                }

                if (trackedDownload.RemoteMovie == null && _engineClientRegistry.IsEngineClient(trackedDownload.DownloadClient))
                {
                    MarkUnlinked(trackedDownload);
                    KeepMissingFromEngineWarning(trackedDownload);
                }

                return;
            }

            if (_engineClientRegistry.IsEngineClient(trackedDownload.DownloadClient))
            {
                if (trackedDownload.IsUnlinked)
                {
                    return;
                }

                var historyItems = FindHistory(trackedDownload.DownloadItem.DownloadId);

                // Its record is gone (for example the series was deleted): show it as Unlinked, which also makes
                // the next poll re-evaluate it and retry the sidecar.
                if (historyItems.Empty())
                {
                    MarkUnlinked(trackedDownload);
                }
                else
                {
                    MapFromLinkHistory(trackedDownload, historyItems);

                    if (trackedDownload.RemoteEpisode != null)
                    {
                        _aggregationService.Augment(trackedDownload.RemoteEpisode);
                    }
                }

                KeepMissingFromEngineWarning(trackedDownload);

                return;
            }

            var parsedEpisodeInfo = Parser.Parser.ParseTitle(trackedDownload.DownloadItem.Title);

            trackedDownload.RemoteEpisode = parsedEpisodeInfo == null ? null : _parsingService.Map(parsedEpisodeInfo, 0, 0, null);

            _aggregationService.Augment(trackedDownload.RemoteEpisode);
        }

        private static TrackedDownloadState GetStateFromHistory(DownloadHistoryEventType eventType)
        {
            switch (eventType)
            {
                case DownloadHistoryEventType.DownloadImported:
                    return TrackedDownloadState.Imported;
                case DownloadHistoryEventType.DownloadFailed:
                    return TrackedDownloadState.Failed;
                case DownloadHistoryEventType.DownloadIgnored:
                    return TrackedDownloadState.Ignored;
                default:
                    return TrackedDownloadState.Downloading;
            }
        }

        public void Handle(EpisodeGrabbedEvent message)
        {
            if (message.DownloadId.IsNullOrWhiteSpace())
            {
                return;
            }

            var trackedDownload = _cache.Find(message.DownloadId);

            if (trackedDownload is { State: TrackedDownloadState.Imported or
                                            TrackedDownloadState.Failed or
                                            TrackedDownloadState.Ignored })
            {
                _cache.Remove(message.DownloadId);
            }
        }

        public void Handle(EpisodeInfoRefreshedEvent message)
        {
            var removedEpisodeIds = message.Removed.Select(e => e.Id).ToHashSet();

            var cachedItems = _cache.Values.Where(t =>
                                        t.RemoteEpisode?.Episodes != null &&
                                        t.RemoteEpisode.Episodes.Any(e => removedEpisodeIds.Contains(e.Id)))
                                    .ToList();

            if (cachedItems.Any())
            {
                cachedItems.ForEach(UpdateCachedItem);

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        public void Handle(SeriesAddedEvent message)
        {
            var cachedItems = _cache.Values
                .Where(t =>
                    t.RemoteEpisode?.Series == null ||
                    message.Series?.TvdbId == t.RemoteEpisode.Series.TvdbId)
                .ToList();

            if (cachedItems.Any())
            {
                cachedItems.ForEach(UpdateCachedItem);

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        public void Handle(SeriesEditedEvent message)
        {
            var cachedItems = _cache.Values
                .Where(t =>
                    t.RemoteEpisode?.Series != null &&
                    (t.RemoteEpisode.Series.Id == message.Series?.Id || t.RemoteEpisode.Series.TvdbId == message.Series?.TvdbId))
                .ToList();

            if (cachedItems.Any())
            {
                cachedItems.ForEach(UpdateCachedItem);

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        public void Handle(SeriesBulkEditedEvent message)
        {
            var seriesIds = message.Series.Select(s => s.Id).ToHashSet();
            var tvdbIds = message.Series.Select(s => s.TvdbId).ToHashSet();

            var cachedItems = _cache.Values
                .Where(t =>
                    t.RemoteEpisode?.Series != null &&
                    (seriesIds.Contains(t.RemoteEpisode.Series.Id) || tvdbIds.Contains(t.RemoteEpisode.Series.TvdbId)))
                .ToList();

            if (cachedItems.Any())
            {
                cachedItems.ForEach(UpdateCachedItem);

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        public void Handle(SeriesDeletedEvent message)
        {
            var seriesIds = message.Series.Select(s => s.Id).ToHashSet();
            var tvdbIds = message.Series.Select(s => s.TvdbId).ToHashSet();

            var cachedItems = _cache.Values
                .Where(t =>
                    t.RemoteEpisode?.Series != null &&
                    (seriesIds.Contains(t.RemoteEpisode.Series.Id) || tvdbIds.Contains(t.RemoteEpisode.Series.TvdbId)))
                .ToList();

            if (cachedItems.Any())
            {
                // The series' history is deleted with it, so a built-in engine torrent loses its link whichever
                // handler runs first.
                foreach (var trackedDownload in cachedItems)
                {
                    if (_engineClientRegistry.IsEngineClient(trackedDownload.DownloadClient))
                    {
                        MarkUnlinked(trackedDownload);
                        KeepMissingFromEngineWarning(trackedDownload);
                    }
                    else
                    {
                        UpdateCachedItem(trackedDownload);
                    }
                }

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        public void Handle(MoviesDeletedEvent message)
        {
            var movieIds = message.Movies.Select(m => m.Id).ToHashSet();

            var cachedItems = _cache.Values
                .Where(t => t.RemoteMovie?.Movie != null && movieIds.Contains(t.RemoteMovie.Movie.Id))
                .ToList();

            if (cachedItems.Any())
            {
                // The movie's history is deleted with it, so a built-in engine torrent loses its link.
                foreach (var trackedDownload in cachedItems)
                {
                    UpdateCachedItem(trackedDownload);
                }

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }
    }
}
