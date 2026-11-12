using System;
using System.Collections.Generic;
using System.Linq;
using MonoTorrent;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.MediaFiles.TorrentInfo;

namespace NzbDrone.Core.Download.Engine
{
    // Thrown when an engine control can't be carried out; the message says why.
    public class EngineControlException : Exception
    {
        public EngineControlException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    public class EngineTransferStatus
    {
        // Bytes per second.
        public long DownloadSpeed { get; set; }
        public long UploadSpeed { get; set; }
        public bool AlternativeSpeedLimits { get; set; }
    }

    // The engine's network, speed, queue and privacy settings (roadmap SET-04). Speeds are in KiB/s, 0 meaning unlimited;
    // queue limits of -1 mean unlimited.
    public class EnginePreferences
    {
        public int ListenPort { get; set; }
        public bool RandomPort { get; set; }
        public bool Upnp { get; set; }
        public string NetworkInterface { get; set; }

        public int DownloadLimit { get; set; }
        public int UploadLimit { get; set; }
        public int AlternativeDownloadLimit { get; set; }
        public int AlternativeUploadLimit { get; set; }

        // Turns the alternative limits on by the clock: from the start time to the end time (it may pass midnight), on
        // the chosen days. Days: 0 every day, 1 weekdays, 2 weekends, 3 Monday through 9 Sunday.
        public bool SchedulerEnabled { get; set; }
        public int ScheduleFromHour { get; set; }
        public int ScheduleFromMinute { get; set; }
        public int ScheduleToHour { get; set; }
        public int ScheduleToMinute { get; set; }
        public int SchedulerDays { get; set; }

        public bool QueueingEnabled { get; set; }
        public int MaxActiveDownloads { get; set; }
        public int MaxActiveUploads { get; set; }
        public int MaxActiveTorrents { get; set; }

        public bool Dht { get; set; }
        public bool Pex { get; set; }
        public bool Lsd { get; set; }
        public bool AnonymousMode { get; set; }

        // 0 prefer encryption, 1 require it, 2 turn it off.
        public int Encryption { get; set; }

        public bool PreallocateDisk { get; set; }
    }

    public interface IEngineControlService
    {
        EnginePreferences GetPreferences();
        void SetPreferences(EnginePreferences preferences);

        // The engine's live transfer speeds and whether its alternative speed limits are on (roadmap §6.2).
        EngineTransferStatus GetStatus();
        void SetAlternativeSpeedLimits(bool enabled);

        void PauseAll();

        // Resumes every torrent except those still waiting to be prepared.
        void ResumeAll();

        void Pause(string downloadId);
        void Resume(string downloadId);
        void SetForceStart(string downloadId, bool enabled);
        void Recheck(string downloadId);

        // Moves a download in the engine's queue: "top", "up", "down" or "bottom".
        void MoveInQueue(string downloadId, string move);

        // A download's own speed limits in KiB/s; 0 is unlimited.
        void SetSpeedLimits(string downloadId, int downloadLimit, int uploadLimit);

        // Adds a torrent by hand (roadmap §6.2) and returns its download id. Like a grab it is held until only its video
        // and subtitle files are chosen; it is listed as Unlinked until Manual Import links it to the library.
        string AddMagnet(string magnetLink);
        string AddTorrentFile(string fileName, byte[] fileContent);

        // Adds a torrent that is already downloaded under savePath (taken over from another qBittorrent), as it is: it isn't
        // prepared, renamed or linked, and shows in Downloads as unlinked. Returns its id.
        string AdoptTorrentFile(string fileName, byte[] fileContent, string savePath, bool stopped);

        // The ids (upper-case info-hashes) of the torrents the engine has.
        HashSet<string> GetTorrentIds();
    }

    // Controls for the built-in engine and its torrents (roadmap §6.2). A torrent still being prepared (§4.8) is never
    // started: it would download files the whitelist hasn't excluded yet; preparation starts it.
    public class EngineControlService : IEngineControlService
    {
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly IEngineAddGate _engineAddGate;
        private readonly ITorrentFileInfoReader _torrentFileInfoReader;
        private readonly Logger _logger;

        public EngineControlService(IProvideDownloadClient downloadClientProvider,
                                    IEngineClientRegistry engineClientRegistry,
                                    IQBittorrentProxySelector proxySelector,
                                    IEngineAddGate engineAddGate,
                                    ITorrentFileInfoReader torrentFileInfoReader,
                                    Logger logger)
        {
            _downloadClientProvider = downloadClientProvider;
            _engineClientRegistry = engineClientRegistry;
            _proxySelector = proxySelector;
            _engineAddGate = engineAddGate;
            _torrentFileInfoReader = torrentFileInfoReader;
            _logger = logger;
        }

        public string AddMagnet(string magnetLink)
        {
            string hash;

            try
            {
                hash = MagnetLink.Parse(magnetLink?.Trim() ?? string.Empty).InfoHashes.V1OrV2.ToHex();
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or UriFormatException)
            {
                throw new EngineControlException("that isn't a valid magnet link", ex);
            }

            return Add(hash, (proxy, settings) => proxy.AddManualTorrentFromUrl(magnetLink.Trim(), settings));
        }

        public string AddTorrentFile(string fileName, byte[] fileContent)
        {
            string hash;

            try
            {
                hash = _torrentFileInfoReader.GetHashFromTorrentFile(fileContent);
            }
            catch (Exception ex)
            {
                throw new EngineControlException("that isn't a valid .torrent file", ex);
            }

            return Add(hash, (proxy, settings) => proxy.AddManualTorrentFromFile(fileName, fileContent, settings));
        }

        public string AdoptTorrentFile(string fileName, byte[] fileContent, string savePath, bool stopped)
        {
            string hash;

            try
            {
                hash = _torrentFileInfoReader.GetHashFromTorrentFile(fileContent);
            }
            catch (Exception ex)
            {
                throw new EngineControlException("that isn't a valid .torrent file", ex);
            }

            return Add(hash, (proxy, settings) => proxy.AdoptTorrentFromFile(fileName, fileContent, savePath, stopped, settings));
        }

        public HashSet<string> GetTorrentIds()
        {
            return Run((proxy, settings) => proxy.GetTorrents(settings).Select(t => t.Hash.ToUpperInvariant()).ToHashSet());
        }

        private string Add(string hash, Action<IQBittorrentProxy, QBittorrentSettings> add)
        {
            return Run((proxy, settings) =>
            {
                // A v2-only torrent's id in the engine is its v2 hash cut to 40 digits.
                hash = hash.Length == 64 ? hash.Substring(0, 40) : hash;

                if (proxy.GetTorrents(settings).Any(t => string.Equals(t.Hash, hash, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new EngineControlException("the engine already has this torrent");
                }

                // Takes turns with orphan cleanup, which mustn't delete partial files this torrent may own.
                using (_engineAddGate.BeginAdd())
                {
                    add(proxy, settings);
                }

                _logger.Info("Added torrent {0} to the engine by hand", hash);

                return hash.ToUpperInvariant();
            });
        }

        public EnginePreferences GetPreferences()
        {
            return Run((proxy, settings) =>
            {
                var json = proxy.GetPreferencesJson(settings);

                return new EnginePreferences
                {
                    ListenPort = json.Value<int?>("listen_port") ?? 0,
                    RandomPort = json.Value<bool?>("random_port") ?? false,
                    Upnp = json.Value<bool?>("upnp") ?? false,
                    NetworkInterface = json.Value<string>("current_network_interface") ?? string.Empty,
                    DownloadLimit = (json.Value<int?>("dl_limit") ?? 0) / 1024,
                    UploadLimit = (json.Value<int?>("up_limit") ?? 0) / 1024,
                    AlternativeDownloadLimit = (json.Value<int?>("alt_dl_limit") ?? 0) / 1024,
                    AlternativeUploadLimit = (json.Value<int?>("alt_up_limit") ?? 0) / 1024,
                    SchedulerEnabled = json.Value<bool?>("scheduler_enabled") ?? false,
                    ScheduleFromHour = json.Value<int?>("schedule_from_hour") ?? 0,
                    ScheduleFromMinute = json.Value<int?>("schedule_from_min") ?? 0,
                    ScheduleToHour = json.Value<int?>("schedule_to_hour") ?? 0,
                    ScheduleToMinute = json.Value<int?>("schedule_to_min") ?? 0,
                    SchedulerDays = json.Value<int?>("scheduler_days") ?? 0,
                    QueueingEnabled = json.Value<bool?>("queueing_enabled") ?? false,
                    MaxActiveDownloads = json.Value<int?>("max_active_downloads") ?? -1,
                    MaxActiveUploads = json.Value<int?>("max_active_uploads") ?? -1,
                    MaxActiveTorrents = json.Value<int?>("max_active_torrents") ?? -1,
                    Dht = json.Value<bool?>("dht") ?? true,
                    Pex = json.Value<bool?>("pex") ?? true,
                    Lsd = json.Value<bool?>("lsd") ?? true,
                    AnonymousMode = json.Value<bool?>("anonymous_mode") ?? false,
                    Encryption = json.Value<int?>("encryption") ?? 0,
                    PreallocateDisk = json.Value<bool?>("preallocate_all") ?? false
                };
            });
        }

        public void SetPreferences(EnginePreferences preferences)
        {
            if (preferences.ListenPort is < 0 or > 65535)
            {
                throw new EngineControlException("the listening port has to be between 0 and 65535");
            }

            if (preferences.ScheduleFromHour is < 0 or > 23 || preferences.ScheduleToHour is < 0 or > 23 ||
                preferences.ScheduleFromMinute is < 0 or > 59 || preferences.ScheduleToMinute is < 0 or > 59)
            {
                throw new EngineControlException("the schedule times have to be real times of day");
            }

            if (preferences.SchedulerDays is < 0 or > 9)
            {
                throw new EngineControlException("the schedule days have to be a value from 0 to 9");
            }

            Run((proxy, settings) =>
            {
                var json = new JObject
                {
                    ["listen_port"] = preferences.ListenPort,
                    ["random_port"] = preferences.RandomPort,
                    ["upnp"] = preferences.Upnp,
                    ["current_network_interface"] = preferences.NetworkInterface ?? string.Empty,
                    ["dl_limit"] = Math.Max(0, preferences.DownloadLimit) * 1024,
                    ["up_limit"] = Math.Max(0, preferences.UploadLimit) * 1024,
                    ["alt_dl_limit"] = Math.Max(0, preferences.AlternativeDownloadLimit) * 1024,
                    ["alt_up_limit"] = Math.Max(0, preferences.AlternativeUploadLimit) * 1024,
                    ["scheduler_enabled"] = preferences.SchedulerEnabled,
                    ["schedule_from_hour"] = preferences.ScheduleFromHour,
                    ["schedule_from_min"] = preferences.ScheduleFromMinute,
                    ["schedule_to_hour"] = preferences.ScheduleToHour,
                    ["schedule_to_min"] = preferences.ScheduleToMinute,
                    ["scheduler_days"] = preferences.SchedulerDays,
                    ["queueing_enabled"] = preferences.QueueingEnabled,
                    ["max_active_downloads"] = preferences.MaxActiveDownloads,
                    ["max_active_uploads"] = preferences.MaxActiveUploads,
                    ["max_active_torrents"] = preferences.MaxActiveTorrents,
                    ["dht"] = preferences.Dht,
                    ["pex"] = preferences.Pex,
                    ["lsd"] = preferences.Lsd,
                    ["anonymous_mode"] = preferences.AnonymousMode,
                    ["encryption"] = preferences.Encryption,
                    ["preallocate_all"] = preferences.PreallocateDisk
                };

                proxy.SetPreferencesJson(json, settings);

                return true;
            });

            _logger.Info("Engine network and speed settings saved");
        }

        public EngineTransferStatus GetStatus()
        {
            return Run((proxy, settings) =>
            {
                var info = proxy.GetTransferInfo(settings);

                return new EngineTransferStatus
                {
                    DownloadSpeed = info.DownloadSpeed,
                    UploadSpeed = info.UploadSpeed,
                    AlternativeSpeedLimits = proxy.GetAlternativeSpeedLimitsEnabled(settings)
                };
            });
        }

        public void SetAlternativeSpeedLimits(bool enabled)
        {
            Run((proxy, settings) =>
            {
                if (proxy.GetAlternativeSpeedLimitsEnabled(settings) != enabled)
                {
                    proxy.ToggleAlternativeSpeedLimits(settings);
                }

                return true;
            });
        }

        public void PauseAll()
        {
            Run((proxy, settings) =>
            {
                proxy.StopTorrents("all", settings);
                _logger.Info("Paused every torrent in the engine");

                return true;
            });
        }

        public void ResumeAll()
        {
            Run((proxy, settings) =>
            {
                var hashes = proxy.GetTorrents(settings)
                    .Where(t => !EngineTorrentTags.IsAwaitingPreparation(t.Tags))
                    .Select(t => t.Hash.ToLowerInvariant())
                    .ToList();

                if (hashes.Any())
                {
                    proxy.StartTorrents(string.Join("|", hashes), settings);
                    _logger.Info("Resumed {0} torrent(s) in the engine", hashes.Count);
                }

                return true;
            });
        }

        public void Pause(string downloadId)
        {
            RunOnTorrent(downloadId, false, (proxy, settings, hash) => proxy.StopTorrents(hash, settings));
        }

        public void Resume(string downloadId)
        {
            RunOnTorrent(downloadId, true, (proxy, settings, hash) => proxy.StartTorrents(hash, settings));
        }

        public void SetForceStart(string downloadId, bool enabled)
        {
            RunOnTorrent(downloadId, enabled, (proxy, settings, hash) => proxy.SetForceStart(hash, enabled, settings));
        }

        public void Recheck(string downloadId)
        {
            RunOnTorrent(downloadId, false, (proxy, settings, hash) => proxy.RecheckTorrent(hash, settings));
        }

        public void MoveInQueue(string downloadId, string move)
        {
            if (move is not ("top" or "up" or "down" or "bottom"))
            {
                throw new EngineControlException("the queue move has to be top, up, down or bottom");
            }

            Run((proxy, settings) =>
            {
                if (!proxy.GetPreferencesJson(settings).Value<bool?>("queueing_enabled").GetValueOrDefault())
                {
                    throw new EngineControlException("limiting how many torrents are active (queueing) is off, so there is no queue to move in; turn it on in the engine network settings");
                }

                return true;
            });

            RunOnTorrent(downloadId, false, (proxy, settings, hash) => proxy.MoveTorrentInQueue(hash, move, settings));
        }

        public void SetSpeedLimits(string downloadId, int downloadLimit, int uploadLimit)
        {
            if (downloadLimit < 0 || uploadLimit < 0)
            {
                throw new EngineControlException("a speed limit can't be negative (0 means no limit)");
            }

            RunOnTorrent(downloadId, false, (proxy, settings, hash) => proxy.SetTorrentSpeedLimits(hash, downloadLimit * 1024L, uploadLimit * 1024L, settings));
        }

        private void RunOnTorrent(string downloadId, bool starts, Action<IQBittorrentProxy, QBittorrentSettings, string> action)
        {
            Run((proxy, settings) =>
            {
                var torrent = proxy.GetTorrents(settings).FirstOrDefault(t => string.Equals(t.Hash, downloadId, StringComparison.OrdinalIgnoreCase)) ??
                              throw new EngineControlException("the engine doesn't have this download");

                if (starts && EngineTorrentTags.IsAwaitingPreparation(torrent.Tags))
                {
                    throw new EngineControlException("this download is still being prepared; it starts by itself once its files are chosen");
                }

                action(proxy, settings, torrent.Hash.ToLowerInvariant());

                return true;
            });
        }

        private T Run<T>(Func<IQBittorrentProxy, QBittorrentSettings, T> action)
        {
            var client = _downloadClientProvider.GetDownloadClients().FirstOrDefault(c => _engineClientRegistry.IsEngineClient(c.Definition.Id));

            if (client?.Definition.Settings is not QBittorrentSettings settings)
            {
                throw new EngineControlException("the built-in engine isn't available");
            }

            try
            {
                return action(_proxySelector.GetProxy(settings), settings);
            }
            catch (EngineControlException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EngineControlException("the built-in engine couldn't be reached", ex);
            }
        }
    }
}
