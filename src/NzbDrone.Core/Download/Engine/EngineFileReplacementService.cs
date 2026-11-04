using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineFileReplacementService
    {
        // Gets existing episode files ready to be replaced by a newer file (roadmap §4.8 "Name already taken", §4.10
        // "Removing one file from a torrent that's still seeding"): each one an engine torrent still holds is detached
        // from it, and a file at the new file's final path is moved to the recycle bin so the new file can take its
        // name. Returns the info-hashes of the torrents the files were detached from.
        List<string> Detach(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, Series series, List<EpisodeFile> existingFiles, string finalPath);

        // Rechecks the torrents files were detached from, once the old files are gone, so they stop offering them.
        void Recheck(IQBittorrentProxy proxy, QBittorrentSettings settings, IEnumerable<string> hashes);
    }

    public class EngineFileReplacementService : IEngineFileReplacementService
    {
        private readonly IEngineLinkService _engineLinkService;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public EngineFileReplacementService(IEngineLinkService engineLinkService, IRecycleBinProvider recycleBinProvider, IDiskProvider diskProvider, Logger logger)
        {
            _engineLinkService = engineLinkService;
            _recycleBinProvider = recycleBinProvider;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public List<string> Detach(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, Series series, List<EpisodeFile> existingFiles, string finalPath)
        {
            var detachedFrom = new List<string>();
            var links = _engineLinkService.GetAll().Where(l => l.Key != replacingDownloadId && l.Value.Files != null).ToList();
            var inEngine = proxy.GetTorrents(settings).Select(t => t.Hash.ToUpperInvariant()).ToHashSet();
            var rootFolder = _diskProvider.GetParentFolder(series.Path);

            foreach (var existingFile in existingFiles)
            {
                var path = Path.Combine(series.Path, existingFile.RelativePath);

                foreach (var (downloadId, link) in links)
                {
                    var video = link.Files.FirstOrDefault(f => !f.IsSubtitle && f.State == EngineLinkFileState.Imported && Path.Combine(link.SavePath, f.FinalPath).PathEquals(path));

                    if (video == null)
                    {
                        continue;
                    }

                    // The old video goes with its subtitles: they carry its name, and would collide with the new
                    // file's subtitles.
                    var detached = link.Files.Where(f => f == video || (f.IsSubtitle && f.VideoIndex == video.Index && f.State == EngineLinkFileState.Imported)).ToList();

                    if (inEngine.Contains(downloadId))
                    {
                        proxy.SetFilePriority(downloadId.ToLowerInvariant(), detached.Select(f => f.Index), 0, settings);
                        detachedFrom.Add(downloadId);
                    }

                    foreach (var subtitle in detached.Where(f => f.IsSubtitle))
                    {
                        Recycle(Path.Combine(link.SavePath, subtitle.FinalPath), rootFolder);
                    }

                    detached.ForEach(f => f.State = EngineLinkFileState.Replaced);
                    _engineLinkService.Save(downloadId, link);

                    _logger.Info("'{0}' is being replaced; detached it from its torrent", path);
                }

                // The new file takes this file's name: move the old one out of the way. The import records the
                // replacement and removes the old file's record.
                if (path.PathEquals(finalPath))
                {
                    Recycle(path, rootFolder);
                }
            }

            return detachedFrom;
        }

        public void Recheck(IQBittorrentProxy proxy, QBittorrentSettings settings, IEnumerable<string> hashes)
        {
            var inEngine = proxy.GetTorrents(settings).Select(t => t.Hash.ToUpperInvariant()).ToHashSet();

            foreach (var hash in hashes.Where(inEngine.Contains))
            {
                proxy.RecheckTorrent(hash.ToLowerInvariant(), settings);
            }
        }

        private void Recycle(string path, string rootFolder)
        {
            if (!_diskProvider.FileExists(path))
            {
                return;
            }

            _logger.Info("Moving replaced file '{0}' to the recycle bin", path);
            _recycleBinProvider.DeleteFile(path, rootFolder.GetRelativePath(_diskProvider.GetParentFolder(path)));
        }
    }
}
