using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Download.Engine
{
    public class EngineOrphanFile
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public DateTime LastWrite { get; set; }
    }

    // Thrown when the files the engine owns can't be known, so nothing may be called an orphan.
    public class EngineOrphanException : Exception
    {
        public EngineOrphanException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    public class EngineOrphanScanCompletedEvent : IEvent
    {
    }

    public interface IEngineOrphanService
    {
        // Orphaned partial download files left awaiting approval by the last scheduled run (0 once approved).
        int AwaitingReview { get; }

        // Finds the partial download files no engine torrent owns (roadmap §4.13). Deletes nothing.
        List<EngineOrphanFile> Scan();

        // Approves a list: deletes those of the given files that are still orphans on a fresh scan, and sets whether
        // the scheduled cleanup deletes orphans itself from now on. Returns the files deleted.
        List<EngineOrphanFile> Approve(IEnumerable<string> paths, bool deleteAutomatically);
    }

    // Only Embarr's own partial files are ever considered: the hidden pending files of a mapped torrent (with or
    // without the engine's ".!qB" suffix, §4.8) and libtorrent's ".parts" files. Each carries its torrent's id in its
    // name, so its owner is known. A file counts as an orphan only when no torrent in the engine has that id, it
    // hasn't been written to for the grace period, and it is inside a root folder (staging folders are inside one).
    public class EngineOrphanService : IEngineOrphanService, IExecute<EngineOrphanCleanupCommand>
    {
        private static readonly Regex PendingRegex = new(@"^\..+\.(?<id>[0-9a-f]{8})" + Regex.Escape(EngineFileMapper.PendingSuffix) + "(?:" + Regex.Escape(EngineLinkService.IncompleteSuffix) + ")?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PartsRegex = new(@"^\.(?<id>[0-9a-f]{40}|[0-9a-f]{64})\.parts$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly IEngineSupervisor _engineSupervisor;
        private readonly IQBittorrentProxySelector _proxySelector;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        private volatile int _awaitingReview;

        public EngineOrphanService(IProvideDownloadClient downloadClientProvider,
                                   IEngineClientRegistry engineClientRegistry,
                                   IEngineSupervisor engineSupervisor,
                                   IQBittorrentProxySelector proxySelector,
                                   IRootFolderService rootFolderService,
                                   IDiskProvider diskProvider,
                                   IConfigService configService,
                                   IEventAggregator eventAggregator,
                                   Logger logger)
        {
            _downloadClientProvider = downloadClientProvider;
            _engineClientRegistry = engineClientRegistry;
            _engineSupervisor = engineSupervisor;
            _proxySelector = proxySelector;
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public int AwaitingReview => _awaitingReview;

        // The torrent id in an Embarr partial file's name: the first 8 hex digits of the info-hash for a pending
        // file, the whole info-hash for a part file. Null for any other file.
        public static string GetTorrentId(string fileName)
        {
            var match = PendingRegex.Match(fileName);

            if (!match.Success)
            {
                match = PartsRegex.Match(fileName);
            }

            return match.Success ? match.Groups["id"].Value.ToLowerInvariant() : null;
        }

        public List<EngineOrphanFile> Scan()
        {
            var owners = GetOwners();
            var graceDays = Math.Max(1, _configService.EngineOrphanGraceDays);
            var cutoff = DateTime.UtcNow.AddDays(-graceDays);
            var orphans = new List<EngineOrphanFile>();

            foreach (var rootFolder in _rootFolderService.All().Select(r => r.Path).Distinct())
            {
                if (!_diskProvider.FolderExists(rootFolder))
                {
                    continue;
                }

                try
                {
                    foreach (var path in _diskProvider.GetFiles(rootFolder, true))
                    {
                        var id = GetTorrentId(Path.GetFileName(path));

                        // A pending file's id is a prefix of its torrent's hash; a part file's name has the hash libtorrent
                        // prefers, which for a v2-only torrent is longer than the engine's (truncated) id.
                        if (id == null || owners.Any(h => h.StartsWith(id, StringComparison.Ordinal) || id.StartsWith(h, StringComparison.Ordinal)))
                        {
                            continue;
                        }

                        var lastWrite = _diskProvider.FileGetLastWrite(path);

                        if (lastWrite > cutoff || orphans.Any(o => o.Path.PathEquals(path)))
                        {
                            continue;
                        }

                        orphans.Add(new EngineOrphanFile { Path = path, Size = _diskProvider.GetFileSize(path), LastWrite = lastWrite });
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.Warn(ex, "Unable to look for orphaned partial files in '{0}'", rootFolder);
                }
            }

            return orphans;
        }

        public List<EngineOrphanFile> Approve(IEnumerable<string> paths, bool deleteAutomatically)
        {
            var approved = paths.ToList();

            // Ownership is checked again just before deleting: a torrent may have been added since the list was made.
            var orphans = Scan();
            var deleted = Delete(orphans.Where(o => approved.Any(p => p.PathEquals(o.Path))));

            _configService.EngineOrphanCleanupApproved = deleteAutomatically;
            _awaitingReview = deleteAutomatically ? 0 : orphans.Count - deleted.Count;
            _eventAggregator.PublishEvent(new EngineOrphanScanCompletedEvent());

            return deleted;
        }

        private List<EngineOrphanFile> Delete(IEnumerable<EngineOrphanFile> orphans)
        {
            var deleted = new List<EngineOrphanFile>();

            foreach (var orphan in orphans)
            {
                try
                {
                    _logger.Info("Deleting orphaned partial file '{0}'", orphan.Path);
                    _diskProvider.DeleteFile(orphan.Path);
                    deleted.Add(orphan);

                    RemoveEmptyStagingFolders(orphan.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.Warn(ex, "Unable to delete orphaned partial file '{0}'", orphan.Path);
                }
            }

            return deleted;
        }

        public void Execute(EngineOrphanCleanupCommand message)
        {
            try
            {
                var orphans = Scan();

                if (orphans.Any() && _configService.EngineOrphanCleanupApproved)
                {
                    var deleted = Delete(orphans);

                    _awaitingReview = 0;
                    _logger.Info("Deleted {0} orphaned partial file(s)", deleted.Count);
                }
                else
                {
                    _awaitingReview = orphans.Count;

                    if (orphans.Any())
                    {
                        _logger.Info("Found {0} orphaned partial file(s); nothing is deleted until the list is approved", orphans.Count);
                    }
                }
            }
            catch (EngineOrphanException ex)
            {
                _logger.Debug("Skipped looking for orphaned partial files: {0}", ex.Message);
            }

            _eventAggregator.PublishEvent(new EngineOrphanScanCompletedEvent());
        }

        // Every id the engine's torrents are known by (lower-case). Throws when the engine can't be asked: without
        // the full list, any file could belong to a torrent that wasn't seen.
        private List<string> GetOwners()
        {
            if (_engineSupervisor.Status != EngineStatus.Running)
            {
                throw new EngineOrphanException("the built-in engine isn't running");
            }

            var client = _downloadClientProvider.GetDownloadClients().FirstOrDefault(c => _engineClientRegistry.IsEngineClient(c.Definition.Id));

            if (client?.Definition.Settings is not QBittorrentSettings settings)
            {
                throw new EngineOrphanException("the built-in engine's download client isn't available");
            }

            List<QBittorrentTorrent> torrents;

            try
            {
                torrents = _proxySelector.GetProxy(settings).GetTorrents(settings);
            }
            catch (Exception ex)
            {
                throw new EngineOrphanException("the built-in engine's torrents couldn't be listed", ex);
            }

            return torrents
                .SelectMany(t => new[] { t.Hash, t.InfoHashV1, t.InfoHashV2 })
                .Where(h => h.IsNotNullOrWhiteSpace())
                .Select(h => h.ToLowerInvariant())
                .Distinct()
                .ToList();
        }

        // A staged file sits in .embarr-staging/{torrent}/{file}/: those folders go once they are empty, never the
        // staging folder itself.
        private void RemoveEmptyStagingFolders(string path)
        {
            var folder = Path.GetDirectoryName(path);

            while (folder != null && Path.GetFileName(folder) != EngineFileMapper.StagingFolderName && IsInsideStaging(folder) && _diskProvider.TryDeleteEmptyFolder(folder))
            {
                folder = Path.GetDirectoryName(folder);
            }
        }

        private static bool IsInsideStaging(string folder)
        {
            return folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(EngineFileMapper.StagingFolderName);
        }
    }
}
