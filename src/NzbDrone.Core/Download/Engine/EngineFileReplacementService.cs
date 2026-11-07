using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineFileReplacementService
    {
        // The engine torrents (info-hashes) that still hold any of the existing files. Called before Detach, so the
        // caller can save them first: a detachment cut short can then still be rechecked.
        List<string> FindHolders(string replacingDownloadId, Series series, List<EpisodeFile> existingFiles);

        // True while another download still in the engine has a replacement of any of the same files, or of the same
        // final path, under way (started but not yet recorded). A download removed from the engine reserves nothing.
        bool IsReserved(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, string finalPath, List<EpisodeFile> existingFiles);

        // Gets existing episode files ready to be replaced by a newer file (roadmap §4.8 "Name already taken", §4.10
        // "Removing one file from a torrent that's still seeding"): each one an engine torrent still holds is detached
        // from it, with its subtitles, and a file at the new file's final path is moved to the recycle bin so the new
        // file can take its name.
        void Detach(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, Series series, List<EpisodeFile> existingFiles, string finalPath);

        // Rechecks the torrents files were detached from, once the old files are gone, so they stop offering them.
        void Recheck(IQBittorrentProxy proxy, QBittorrentSettings settings, IEnumerable<string> hashes);

        // The same for a movie's file (its torrents are the movie links, so a movie file id never meets an episode file id).
        List<string> FindHolders(string replacingDownloadId, Movie movie, MovieFile existingFile);
        bool IsReserved(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, string finalPath, MovieFile existingFile);
        void Detach(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, Movie movie, MovieFile existingFile, string finalPath);
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

        public List<string> FindHolders(string replacingDownloadId, Series series, List<EpisodeFile> existingFiles)
        {
            return FindHolders(replacingDownloadId, series.Path, existingFiles.Select(f => (f.Id, f.RelativePath)).ToList(), false);
        }

        public List<string> FindHolders(string replacingDownloadId, Movie movie, MovieFile existingFile)
        {
            return FindHolders(replacingDownloadId, movie.Path, new List<(int, string)> { (existingFile.Id, existingFile.RelativePath) }, true);
        }

        private List<string> FindHolders(string replacingDownloadId, string mediaPath, List<(int Id, string RelativePath)> existingFiles, bool movies)
        {
            var links = GetLinks(replacingDownloadId, movies);
            var holders = new List<string>();

            foreach (var existingFile in existingFiles)
            {
                foreach (var (downloadId, link) in links)
                {
                    var video = FindVideo(link, Path.Combine(mediaPath, existingFile.RelativePath), existingFile.Id);

                    if (video == null)
                    {
                        continue;
                    }

                    holders.Add(downloadId);

                    // Rechecks the holder still owed for its own replacements carry over: once its video is replaced,
                    // nothing else retries them.
                    holders.AddRange(video.DetachedFrom ?? new List<string>());
                }
            }

            return holders.Distinct().ToList();
        }

        public bool IsReserved(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, string finalPath, List<EpisodeFile> existingFiles)
        {
            return IsReserved(proxy, settings, replacingDownloadId, finalPath, existingFiles.Select(f => f.Id).ToHashSet(), false);
        }

        public bool IsReserved(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, string finalPath, MovieFile existingFile)
        {
            return IsReserved(proxy, settings, replacingDownloadId, finalPath, new HashSet<int> { existingFile.Id }, true);
        }

        private bool IsReserved(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, string finalPath, HashSet<int> fileIds, bool movies)
        {
            var inEngine = proxy.GetTorrents(settings).Select(t => t.Hash.ToUpperInvariant()).ToHashSet();

            return GetLinks(replacingDownloadId, movies)
                .Where(l => inEngine.Contains(l.Key))
                .SelectMany(l => l.Value.Files.Where(f => !f.IsSubtitle).Select(f => (Link: l.Value, File: f)))
                .Any(x => (x.File.State == EngineLinkFileState.Renamed || (x.File.State == EngineLinkFileState.Pending && x.File.ReplacesFileIds != null)) &&
                          ((x.File.ReplacesFileIds?.Any(fileIds.Contains) ?? false) || Path.Combine(x.Link.SavePath, x.File.FinalPath).PathEquals(finalPath)));
        }

        public void Detach(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, Series series, List<EpisodeFile> existingFiles, string finalPath)
        {
            Detach(proxy, settings, replacingDownloadId, series.Path, existingFiles.Select(f => (f.Id, f.RelativePath)).ToList(), finalPath, false);
        }

        public void Detach(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, Movie movie, MovieFile existingFile, string finalPath)
        {
            Detach(proxy, settings, replacingDownloadId, movie.Path, new List<(int, string)> { (existingFile.Id, existingFile.RelativePath) }, finalPath, true);
        }

        private void Detach(IQBittorrentProxy proxy, QBittorrentSettings settings, string replacingDownloadId, string mediaPath, List<(int Id, string RelativePath)> existingFiles, string finalPath, bool movies)
        {
            var links = GetLinks(replacingDownloadId, movies);
            var inEngine = proxy.GetTorrents(settings).Select(t => t.Hash.ToUpperInvariant()).ToHashSet();
            var rootFolder = _diskProvider.GetParentFolder(mediaPath);

            foreach (var existingFile in existingFiles)
            {
                var path = Path.Combine(mediaPath, existingFile.RelativePath);

                foreach (var (downloadId, link) in links)
                {
                    var video = FindVideo(link, path, existingFile.Id);

                    if (video == null)
                    {
                        continue;
                    }

                    // The old video goes with all its subtitles, published or not: they carry its name, and would
                    // collide with the new file's subtitles.
                    var subtitles = link.Files.Where(f => f.IsSubtitle && f.VideoIndex == video.Index && f.State is EngineLinkFileState.Pending or EngineLinkFileState.Renamed or EngineLinkFileState.Imported).ToList();

                    if (inEngine.Contains(downloadId))
                    {
                        proxy.SetFilePriority(downloadId.ToLowerInvariant(), subtitles.Select(f => f.Index).Prepend(video.Index), 0, settings);
                    }

                    foreach (var subtitle in subtitles)
                    {
                        // Unpublished copies are hidden download files; a published one is a library file.
                        var pendingPath = Path.Combine(link.SavePath, subtitle.PendingPath);

                        Delete(pendingPath);
                        Delete(pendingPath + EngineLinkService.IncompleteSuffix);
                        Recycle(Path.Combine(link.SavePath, subtitle.FinalPath), rootFolder);

                        subtitle.State = EngineLinkFileState.Replaced;
                    }

                    // Rechecks it still owed were handed to the new file (FindHolders), which saved them first.
                    video.State = EngineLinkFileState.Replaced;
                    video.DetachedFrom = null;
                    _engineLinkService.Save(downloadId, link);

                    _logger.Info("'{0}' is being replaced; detached it from its torrent", path);
                }

                // The new file takes this file's name: move the old one out of the way. The import then replaces the
                // old file's record.
                if (path.PathEquals(finalPath))
                {
                    Recycle(path, rootFolder);
                }
            }
        }

        public void Recheck(IQBittorrentProxy proxy, QBittorrentSettings settings, IEnumerable<string> hashes)
        {
            var inEngine = proxy.GetTorrents(settings).Select(t => t.Hash.ToUpperInvariant()).ToHashSet();

            foreach (var hash in hashes.Where(inEngine.Contains))
            {
                proxy.RecheckTorrent(hash.ToLowerInvariant(), settings);
            }
        }

        // Movie files and episode files share no id space, so each kind only meets the links of its own kind.
        private List<KeyValuePair<string, EngineLink>> GetLinks(string replacingDownloadId, bool movies)
        {
            return _engineLinkService.GetAll()
                .Where(l => l.Key != replacingDownloadId && l.Value.Files != null && (l.Value.MediaType == EngineLinkService.MovieMediaType) == movies)
                .ToList();
        }

        // The video a link holds at the path of an existing file. A Renamed video counts when the record is its own
        // (its import finished but the state wasn't saved), not when it is the file it replaces.
        private static EngineLinkFile FindVideo(EngineLink link, string path, int fileId)
        {
            return link.Files.FirstOrDefault(f => !f.IsSubtitle &&
                                                  (f.State == EngineLinkFileState.Imported || (f.State == EngineLinkFileState.Renamed && f.ReplacesFileIds?.Contains(fileId) != true)) &&
                                                  Path.Combine(link.SavePath, f.FinalPath).PathEquals(path));
        }

        private void Delete(string path)
        {
            if (_diskProvider.FileExists(path))
            {
                _diskProvider.DeleteFile(path);
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
