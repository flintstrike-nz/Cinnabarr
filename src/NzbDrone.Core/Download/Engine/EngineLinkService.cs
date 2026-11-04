using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineLinkService
    {
        // Rebuilds the grab record for a built-in engine torrent from its link sidecar.
        // Returns true if grab history was written.
        bool TryRestore(DownloadClientDefinition downloadClient, string downloadId);

        EngineLink Get(string downloadId);
        void Save(string downloadId, EngineLink link);

        // True when a sidecar file exists for the download, whether or not it can be read.
        bool Exists(string downloadId);

        // Every readable link, by download id (upper-case info-hash).
        Dictionary<string, EngineLink> GetAll();

        // Deletes the hidden pending files of a mapped torrent (complete or still incomplete); never a final file.
        void DeletePendingFiles(EngineLink link);
    }

    // Writes the link-intent sidecar for every grab sent to the built-in engine, and uses it
    // to restore links that Embarr's database has lost (roadmap §4.4 rule 5).
    public class EngineLinkService : IEngineLinkService, IHandle<EpisodeGrabbedEvent>
    {
        public const string SeriesMediaType = "series";
        public const string RestoredFromKey = "LinkRestoredFrom";
        public const string IncompleteSuffix = ".!qB";

        private static readonly Regex InfoHashRegex = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly IDiskProvider _diskProvider;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IHistoryRepository _historyRepository;
        private readonly IDownloadHistoryRepository _downloadHistoryRepository;
        private readonly Logger _logger;

        public EngineLinkService(IEngineClientRegistry engineClientRegistry,
                                 IDiskProvider diskProvider,
                                 ISeriesService seriesService,
                                 IEpisodeService episodeService,
                                 IHistoryRepository historyRepository,
                                 IDownloadHistoryRepository downloadHistoryRepository,
                                 Logger logger)
        {
            _engineClientRegistry = engineClientRegistry;
            _diskProvider = diskProvider;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _historyRepository = historyRepository;
            _downloadHistoryRepository = downloadHistoryRepository;
            _logger = logger;
        }

        private string LinkFolder => Path.Combine(_engineClientRegistry.EngineFolder, "embarr");

        public static bool IsInfoHash(string downloadId)
        {
            return downloadId != null && InfoHashRegex.IsMatch(downloadId);
        }

        public bool TryRestore(DownloadClientDefinition downloadClient, string downloadId)
        {
            var link = Read(downloadId);

            if (link == null)
            {
                return false;
            }

            if (link.MediaType != SeriesMediaType)
            {
                _logger.Debug("Link for {0} is for media type '{1}', which can't be restored yet", downloadId, link.MediaType);
                return false;
            }

            var series = _seriesService.FindByTvdbId(link.TvdbId);

            if (series == null)
            {
                _logger.Debug("Link for {0} is for TVDB id {1}, which isn't in the library", downloadId, link.TvdbId);
                return false;
            }

            var linkedEpisodes = (link.Episodes ?? new())
                .DistinctBy(e => (e.SeasonNumber, e.EpisodeNumber))
                .ToList();

            var episodes = linkedEpisodes
                .Select(e => _episodeService.FindEpisode(series.Id, e.SeasonNumber, e.EpisodeNumber))
                .Where(e => e != null)
                .ToList();

            // Restore all or nothing: a partial link would never be completed, because restoring only
            // happens while the torrent has no history at all. Leaving it Unlinked lets a later poll retry
            // once the missing episodes exist.
            if (linkedEpisodes.Count == 0 || episodes.Count != linkedEpisodes.Count)
            {
                _logger.Debug("Link for {0} names {1} episode(s), {2} of them exist in '{3}'", downloadId, linkedEpisodes.Count, episodes.Count, series.Title);
                return false;
            }

            var sourceTitle = link.SourceTitle ?? string.Empty;
            var quality = ToQualityModel(link.Quality) ?? QualityParser.ParseQuality(sourceTitle);
            var languages = LanguageParser.ParseLanguages(sourceTitle);

            var historyItems = new List<EpisodeHistory>();

            foreach (var episode in episodes)
            {
                var history = new EpisodeHistory
                {
                    EventType = EpisodeHistoryEventType.Grabbed,
                    Date = link.Grabbed == default ? DateTime.UtcNow : link.Grabbed,
                    Quality = quality,
                    SourceTitle = sourceTitle,
                    SeriesId = series.Id,
                    EpisodeId = episode.Id,
                    DownloadId = downloadId,
                    Languages = languages
                };

                history.Data.Add("Indexer", link.Indexer);
                history.Data.Add("DownloadClient", downloadClient.Implementation);
                history.Data.Add("DownloadClientName", downloadClient.Name);
                history.Data.Add("Protocol", ((int)DownloadProtocol.Torrent).ToString());
                history.Data.Add("TorrentInfoHash", downloadId);
                history.Data.Add(RestoredFromKey, "EngineSidecar");

                historyItems.Add(history);
            }

            // One transaction, so a failure can't leave part of the torrent linked.
            _historyRepository.InsertMany(historyItems);

            // The download-level grab makes the restored torrent count as unfinished, so reconcile can report
            // it if it later disappears from the engine.
            try
            {
                var grab = new DownloadHistory
                {
                    EventType = DownloadHistoryEventType.DownloadGrabbed,
                    SeriesId = series.Id,
                    DownloadId = downloadId,
                    SourceTitle = sourceTitle,
                    Date = historyItems[0].Date,
                    Protocol = DownloadProtocol.Torrent,
                    DownloadClientId = downloadClient.Id,
                    Release = new ReleaseInfo { Title = sourceTitle, Indexer = link.Indexer, DownloadProtocol = DownloadProtocol.Torrent }
                };

                grab.Data.Add("Indexer", link.Indexer);
                grab.Data.Add("DownloadClient", downloadClient.Implementation);
                grab.Data.Add("DownloadClientName", downloadClient.Name);
                grab.Data.Add(RestoredFromKey, "EngineSidecar");

                _downloadHistoryRepository.Insert(grab);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to record the restored grab for '{0}'", sourceTitle);
            }

            _logger.Info("Restored link for '{0}' to {1} ({2} episode(s)) from the engine sidecar", sourceTitle, series.Title, episodes.Count);

            return true;
        }

        public void Handle(EpisodeGrabbedEvent message)
        {
            if (!_engineClientRegistry.IsEngineClient(message.DownloadClientId) || !IsInfoHash(message.DownloadId))
            {
                return;
            }

            var remoteEpisode = message.Episode;

            var link = new EngineLink
            {
                MediaType = SeriesMediaType,
                TvdbId = remoteEpisode.Series.TvdbId,
                Episodes = remoteEpisode.Episodes
                    .Select(e => new EngineLinkEpisode { SeasonNumber = e.SeasonNumber, EpisodeNumber = e.EpisodeNumber })
                    .ToList(),
                SourceTitle = remoteEpisode.Release?.Title,
                Indexer = remoteEpisode.Release?.Indexer,
                Quality = FromQualityModel(remoteEpisode.ParsedEpisodeInfo?.Quality),
                Grabbed = DateTime.UtcNow
            };

            try
            {
                Write(message.DownloadId, link);
            }
            catch (Exception ex)
            {
                // The grab itself has succeeded; without a sidecar the torrent is still linked
                // through Embarr's database, it just can't be recovered from the engine alone.
                _logger.Warn(ex, "Unable to write the engine link for '{0}'", link.SourceTitle);
            }
        }

        private static EngineLinkQuality FromQualityModel(QualityModel quality)
        {
            if (quality?.Quality == null)
            {
                return null;
            }

            return new EngineLinkQuality
            {
                Id = quality.Quality.Id,
                Version = quality.Revision?.Version ?? 1,
                Real = quality.Revision?.Real ?? 0,
                IsRepack = quality.Revision?.IsRepack ?? false
            };
        }

        private static QualityModel ToQualityModel(EngineLinkQuality quality)
        {
            if (quality == null)
            {
                return null;
            }

            try
            {
                return new QualityModel(Quality.FindById(quality.Id), new Revision(quality.Version, quality.Real, quality.IsRepack));
            }
            catch (ArgumentException)
            {
                // An id this build doesn't know: fall back to parsing the title.
                return null;
            }
        }

        public EngineLink Get(string downloadId)
        {
            return Read(downloadId);
        }

        public Dictionary<string, EngineLink> GetAll()
        {
            var links = new Dictionary<string, EngineLink>();

            if (!_diskProvider.FolderExists(LinkFolder))
            {
                return links;
            }

            foreach (var path in _diskProvider.GetFiles(LinkFolder, false).Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var downloadId = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
                var link = Read(downloadId);

                if (link != null)
                {
                    links[downloadId] = link;
                }
            }

            return links;
        }

        public bool Exists(string downloadId)
        {
            return IsInfoHash(downloadId) && _diskProvider.FileExists(GetLinkPath(downloadId));
        }

        public void Save(string downloadId, EngineLink link)
        {
            if (!IsInfoHash(downloadId))
            {
                throw new ArgumentException("Not an info-hash", nameof(downloadId));
            }

            Write(downloadId, link);
        }

        public void DeletePendingFiles(EngineLink link)
        {
            if (link?.Files == null || link.SavePath == null)
            {
                return;
            }

            foreach (var file in link.Files)
            {
                var pendingPath = Path.Combine(link.SavePath, file.PendingPath);

                foreach (var path in new[] { pendingPath, pendingPath + IncompleteSuffix })
                {
                    try
                    {
                        if (_diskProvider.FileExists(path))
                        {
                            _diskProvider.DeleteFile(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Unable to delete '{0}'", path);
                    }
                }
            }
        }

        private string GetLinkPath(string downloadId)
        {
            return Path.Combine(LinkFolder, downloadId.ToLowerInvariant() + ".json");
        }

        private void Write(string downloadId, EngineLink link)
        {
            var path = GetLinkPath(downloadId);
            var temp = path + ".tmp";

            _diskProvider.EnsureFolder(LinkFolder);
            _diskProvider.WriteAllText(temp, link.ToJson());
            _diskProvider.ReplaceFile(temp, path);
        }

        private EngineLink Read(string downloadId)
        {
            if (!IsInfoHash(downloadId))
            {
                return null;
            }

            var path = GetLinkPath(downloadId);

            try
            {
                if (!_diskProvider.FileExists(path))
                {
                    return null;
                }

                if (Json.TryDeserialize<EngineLink>(_diskProvider.ReadAllText(path), out var link))
                {
                    return link;
                }

                _logger.Warn("Engine link file '{0}' couldn't be read", path);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Engine link file '{0}' couldn't be read", path);
            }

            return null;
        }
    }
}
