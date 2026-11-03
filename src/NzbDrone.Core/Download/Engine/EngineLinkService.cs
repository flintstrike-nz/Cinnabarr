using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineLinkService
    {
        // Rebuilds the grab record for a built-in engine torrent from its link sidecar.
        // Returns true if grab history was written.
        bool TryRestore(DownloadClientDefinition downloadClient, string downloadId);
    }

    // Writes the link-intent sidecar for every grab sent to the built-in engine, and uses it
    // to restore links that Embarr's database has lost (roadmap §4.4 rule 5).
    public class EngineLinkService : IEngineLinkService, IHandle<EpisodeGrabbedEvent>
    {
        public const string SeriesMediaType = "series";
        public const string RestoredFromKey = "LinkRestoredFrom";

        private static readonly Regex InfoHashRegex = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly IDiskProvider _diskProvider;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IHistoryRepository _historyRepository;
        private readonly Logger _logger;

        public EngineLinkService(IEngineClientRegistry engineClientRegistry,
                                 IDiskProvider diskProvider,
                                 ISeriesService seriesService,
                                 IEpisodeService episodeService,
                                 IHistoryRepository historyRepository,
                                 Logger logger)
        {
            _engineClientRegistry = engineClientRegistry;
            _diskProvider = diskProvider;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _historyRepository = historyRepository;
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
            var quality = QualityParser.ParseQuality(sourceTitle);
            var languages = LanguageParser.ParseLanguages(sourceTitle);

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

                _historyRepository.Insert(history);
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
