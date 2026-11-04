using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Download.Clients.QBittorrent;

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

    public interface IEngineControlService
    {
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
    }

    // Controls for the built-in engine and its torrents (roadmap §6.2). A torrent still being prepared (§4.8) is never
    // started: it would download files the whitelist hasn't excluded yet; preparation starts it.
    public class EngineControlService : IEngineControlService
    {
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly Logger _logger;

        public EngineControlService(IProvideDownloadClient downloadClientProvider,
                                    IEngineClientRegistry engineClientRegistry,
                                    IQBittorrentProxySelector proxySelector,
                                    Logger logger)
        {
            _downloadClientProvider = downloadClientProvider;
            _engineClientRegistry = engineClientRegistry;
            _proxySelector = proxySelector;
            _logger = logger;
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
