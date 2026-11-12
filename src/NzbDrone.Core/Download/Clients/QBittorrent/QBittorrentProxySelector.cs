using System;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;

namespace NzbDrone.Core.Download.Clients.QBittorrent
{
    public interface IQBittorrentProxy
    {
        bool IsApiSupported(QBittorrentSettings settings);
        Version GetApiVersion(QBittorrentSettings settings);
        string GetVersion(QBittorrentSettings settings);
        QBittorrentPreferences GetConfig(QBittorrentSettings settings);
        List<QBittorrentTorrent> GetTorrents(QBittorrentSettings settings);
        bool IsTorrentLoaded(string hash, QBittorrentSettings settings);
        QBittorrentTorrentProperties GetTorrentProperties(string hash, QBittorrentSettings settings);
        List<QBittorrentTorrentFile> GetTorrentFiles(string hash, QBittorrentSettings settings);

        // hold: the built-in engine's add, where nothing downloads until Cinnabarr has prepared the torrent (roadmap §4.8)
        void AddTorrentFromUrl(string torrentUrl, TorrentSeedConfiguration seedConfiguration, QBittorrentSettings settings, bool hold = false);
        void AddTorrentFromFile(string fileName, byte[] fileContent, TorrentSeedConfiguration seedConfiguration, QBittorrentSettings settings, bool hold = false);

        void RemoveTorrent(string hash, bool removeData, QBittorrentSettings settings);
        void SetTorrentLabel(string hash, string label, QBittorrentSettings settings);
        void AddLabel(string label, QBittorrentSettings settings);
        Dictionary<string, QBittorrentLabel> GetLabels(QBittorrentSettings settings);
        void SetTorrentSeedingConfiguration(string hash, TorrentSeedConfiguration seedConfiguration, QBittorrentSettings settings);
        void MoveTorrentToTopInQueue(string hash, QBittorrentSettings settings);
        void SetForceStart(string hash, bool enabled, QBittorrentSettings settings);
        void AddTags(string hash, IEnumerable<string> tags, QBittorrentSettings settings);
        void SetFilePriority(string hash, IEnumerable<int> fileIndexes, int priority, QBittorrentSettings settings);
        void StartTorrent(string hash, QBittorrentSettings settings);
        void RenameFile(string hash, string oldPath, string newPath, QBittorrentSettings settings);
        void SetLocation(string hash, string location, QBittorrentSettings settings);
        void RecheckTorrent(string hash, QBittorrentSettings settings);

        // Moves a torrent in the engine's queue ("top", "up", "down" or "bottom"). Throws when queueing is off.
        void MoveTorrentInQueue(string hash, string move, QBittorrentSettings settings);

        // A torrent's own speed limits in bytes per second; 0 is unlimited.
        void SetTorrentSpeedLimits(string hash, long downloadLimit, long uploadLimit, QBittorrentSettings settings);

        // Sets a torrent's ratio limit and clears both its seeding time limits (total and inactive).
        void SetShareLimitsWithoutTimeLimits(string hash, double ratioLimit, QBittorrentSettings settings);

        // Stops (pauses) torrents; hashes are joined with '|', or "all".
        void StopTorrents(string hashes, QBittorrentSettings settings);

        // Starts (resumes) torrents; hashes are joined with '|', or "all".
        void StartTorrents(string hashes, QBittorrentSettings settings);

        // A torrent added by hand to the built-in engine: held for preparation like a grab, and tagged as manual.
        void AddManualTorrentFromUrl(string torrentUrl, QBittorrentSettings settings);
        void AddManualTorrentFromFile(string fileName, byte[] fileContent, QBittorrentSettings settings);

        // Adds a torrent that is already downloaded where savePath says, with no Cinnabarr tags (so it isn't prepared or
        // changed): the engine checks the files and carries on seeding.
        void AdoptTorrentFromFile(string fileName, byte[] fileContent, string savePath, bool stopped, QBittorrentSettings settings);

        QBittorrentTransferInfo GetTransferInfo(QBittorrentSettings settings);
        bool GetAlternativeSpeedLimitsEnabled(QBittorrentSettings settings);
        void ToggleAlternativeSpeedLimits(QBittorrentSettings settings);

        // The engine's preferences as the JSON the engine sends (only the built-in engine's settings screen uses them).
        JObject GetPreferencesJson(QBittorrentSettings settings);
        void SetPreferencesJson(JObject preferences, QBittorrentSettings settings);
    }

    public interface IQBittorrentProxySelector
    {
        IQBittorrentProxy GetProxy(QBittorrentSettings settings, bool force = false);
        Version GetApiVersion(QBittorrentSettings settings, bool force = false);
    }

    public class QBittorrentProxySelector : IQBittorrentProxySelector
    {
        private readonly ICached<Tuple<IQBittorrentProxy, Version>> _proxyCache;
        private readonly Logger _logger;

        private readonly IQBittorrentProxy _proxyV1;
        private readonly IQBittorrentProxy _proxyV2;

        public  QBittorrentProxySelector(QBittorrentProxyV1 proxyV1,
                                         QBittorrentProxyV2 proxyV2,
                                         ICacheManager cacheManager,
                                         Logger logger)
        {
            _proxyCache = cacheManager.GetCache<Tuple<IQBittorrentProxy, Version>>(GetType());
            _logger = logger;

            _proxyV1 = proxyV1;
            _proxyV2 = proxyV2;
        }

        public IQBittorrentProxy GetProxy(QBittorrentSettings settings, bool force)
        {
            return GetProxyCache(settings, force).Item1;
        }

        public Version GetApiVersion(QBittorrentSettings settings, bool force)
        {
            return GetProxyCache(settings, force).Item2;
        }

        private Tuple<IQBittorrentProxy, Version> GetProxyCache(QBittorrentSettings settings, bool force)
        {
            var proxyKey = $"{settings.Host}_{settings.Port}";

            if (force)
            {
                _proxyCache.Remove(proxyKey);
            }

            return _proxyCache.Get(proxyKey, () => FetchProxy(settings), TimeSpan.FromMinutes(10.0));
        }

        private Tuple<IQBittorrentProxy, Version> FetchProxy(QBittorrentSettings settings)
        {
            if (_proxyV2.IsApiSupported(settings))
            {
                _logger.Trace("Using qbitTorrent API v2");
                return Tuple.Create(_proxyV2, _proxyV2.GetApiVersion(settings));
            }

            if (_proxyV1.IsApiSupported(settings))
            {
                _logger.Trace("Using qbitTorrent API v1");
                return Tuple.Create(_proxyV1, _proxyV1.GetApiVersion(settings));
            }

            throw new DownloadClientException("Unable to determine qBittorrent API version");
        }
    }
}
