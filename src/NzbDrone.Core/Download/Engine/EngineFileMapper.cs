using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Download.Engine
{
    public class EngineFileMap
    {
        // The folder the engine saves into: the lowest folder that holds every final path.
        public string SavePath { get; set; }
        public List<EngineLinkFile> Files { get; set; } = new();
    }

    public interface IEngineFileMapper
    {
        // Maps each wanted file of a torrent to its final library path (roadmap §4.8 steps 4-6). Returns null when no
        // video file matches the grabbed episodes.
        EngineFileMap Map(string downloadId, Series series, List<Episode> episodes, QualityModel quality, string releaseTitle, List<QBittorrentTorrentFile> files);

        // Rebuilds a video's final name (and its subtitles') from what the import checks learned about the file,
        // such as its media info. Returns true if any final name changed.
        bool UpdateFinalPaths(EngineLink link, EngineLinkFile video, Series series, LocalEpisode localEpisode);
    }

    public class EngineFileMapper : IEngineFileMapper
    {
        public const string PendingSuffix = ".pending";

        // Leading dot, ".{8 hex}" torrent id, ".pending", and the engine's ".!qB" while incomplete.
        public const int PendingOverheadBytes = 1 + 9 + 8 + 4;
        public const int MaxFileNameBytes = 255;

        private static readonly Regex PendingRegex = new(@"\.[0-9a-f]{8}\.pending$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SampleRegex = new(@"(?:^|[\W_])sample(?:[\W_]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly IParsingService _parsingService;
        private readonly IBuildFileNames _buildFileNames;

        public EngineFileMapper(IParsingService parsingService, IBuildFileNames buildFileNames)
        {
            _parsingService = parsingService;
            _buildFileNames = buildFileNames;
        }

        public static string GetNameWithoutPendingSuffix(string path)
        {
            return path == null ? null : PendingRegex.Replace(path, string.Empty);
        }

        public static string GetFileName(string torrentPath)
        {
            return torrentPath.Replace('\\', '/').Split('/').Last();
        }

        public EngineFileMap Map(string downloadId, Series series, List<Episode> episodes, QualityModel quality, string releaseTitle, List<QBittorrentTorrentFile> files)
        {
            // Pending names carry part of the info-hash, so two torrents for the same target never share one.
            var pendingSuffix = "." + downloadId.Substring(0, 8).ToLowerInvariant() + PendingSuffix;

            var indexed = files.Select((file, index) => (File: file, Index: index)).ToList();
            var videos = indexed.Where(f => EngineFileWhitelist.IsVideo(f.File.Name)).ToList();

            // A sample is never wanted when there is a real video next to it (a name and size check; the runtime check
            // happens on import).
            var candidates = videos.Count > 1
                ? videos.Where(v => !SampleRegex.IsMatch(Path.GetFileNameWithoutExtension(GetFileName(v.File.Name)))).ToList()
                : videos;

            var grabbedIds = episodes.Select(e => e.Id).ToHashSet();
            var releaseInfo = Parser.Parser.ParseTitle(releaseTitle);

            var mappedVideos = new List<MappedVideo>();

            foreach (var (file, index) in candidates)
            {
                var fileInfo = Parser.Parser.ParseTitle(Path.GetFileNameWithoutExtension(GetFileName(file.Name)));

                var fileEpisodes = candidates.Count == 1
                    ? episodes
                    : fileInfo == null
                        ? new List<Episode>()
                        : _parsingService.GetEpisodes(fileInfo, series, true).Where(e => grabbedIds.Contains(e.Id)).ToList();

                if (fileEpisodes.Count == 0)
                {
                    continue;
                }

                // As for any import: a single file takes the release title as its original title; each file of a
                // pack keeps its own name, so files don't collide when episode renaming is off.
                var episodeFile = new EpisodeFile
                {
                    Quality = quality,
                    ReleaseGroup = fileInfo?.ReleaseGroup ?? releaseInfo?.ReleaseGroup,
                    SceneName = candidates.Count == 1 ? releaseTitle : Path.GetFileNameWithoutExtension(GetFileName(file.Name)),
                    RelativePath = GetFileName(file.Name),
                    Languages = releaseInfo?.Languages ?? new(),
                    Size = file.Size
                };

                var finalPath = BuildFinalPath(fileEpisodes, series, episodeFile, Path.GetExtension(GetFileName(file.Name)), null);

                mappedVideos.Add(new MappedVideo
                {
                    File = file,
                    Index = index,
                    Episodes = fileEpisodes,
                    FinalPath = finalPath,
                    SceneName = episodeFile.SceneName,
                    OriginalName = episodeFile.RelativePath
                });
            }

            // Two files for the same episodes (for example a release and a re-encode): keep the larger one.
            mappedVideos = mappedVideos
                .GroupBy(v => string.Join(",", v.Episodes.Select(e => e.Id).OrderBy(id => id)))
                .Select(g => g.OrderByDescending(v => v.File.Size).First())
                .ToList();

            if (mappedVideos.Count == 0)
            {
                return null;
            }

            var savePath = GetCommonFolder(mappedVideos.Select(v => Path.GetDirectoryName(v.FinalPath)).ToList());
            var map = new EngineFileMap { SavePath = savePath };

            foreach (var video in mappedVideos)
            {
                var videoFile = CreateFile(savePath, pendingSuffix, video.Index, video.FinalPath, video.Episodes, null);

                videoFile.SceneName = video.SceneName;
                videoFile.OriginalName = video.OriginalName;
                map.Files.Add(videoFile);
            }

            var subtitles = new List<MappedSubtitle>();

            foreach (var (file, index) in indexed.Where(f => EngineFileWhitelist.IsSubtitle(f.File.Name)))
            {
                var subtitleName = GetFileName(file.Name);
                var video = FindVideo(subtitleName, mappedVideos);

                if (video == null)
                {
                    continue;
                }

                var videoStem = Path.GetFileNameWithoutExtension(GetFileName(video.File.Name));
                var suffix = subtitleName.StartsWith(videoStem, StringComparison.OrdinalIgnoreCase)
                    ? subtitleName.Substring(videoStem.Length)
                    : "." + subtitleName;

                subtitles.Add(new MappedSubtitle { File = file, Index = index, Video = video, Suffix = suffix });
            }

            // Subtitles from different folders (for example English/ and SDH/) can end up with the same name: keep
            // them apart by their folder name, then by their position if that isn't enough.
            foreach (var group in subtitles.GroupBy(s => GetSubtitlePath(s.Video.FinalPath, s.Suffix), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                foreach (var subtitle in group)
                {
                    var folder = GetFolderName(subtitle.File.Name);

                    if (folder.IsNotNullOrWhiteSpace())
                    {
                        subtitle.Suffix = "." + folder + subtitle.Suffix;
                    }
                }
            }

            foreach (var group in subtitles.GroupBy(s => GetSubtitlePath(s.Video.FinalPath, s.Suffix), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                foreach (var (subtitle, position) in group.Skip(1).Select((s, i) => (s, i + 2)))
                {
                    subtitle.Suffix = "." + position + subtitle.Suffix;
                }
            }

            foreach (var subtitle in subtitles)
            {
                map.Files.Add(CreateFile(savePath, pendingSuffix, subtitle.Index, FitFileName(GetSubtitlePath(subtitle.Video.FinalPath, subtitle.Suffix)), new List<Episode>(), subtitle.Video.Index));
            }

            return map;
        }

        public bool UpdateFinalPaths(EngineLink link, EngineLinkFile video, Series series, LocalEpisode localEpisode)
        {
            var episodeFile = new EpisodeFile
            {
                Quality = localEpisode.Quality,
                ReleaseGroup = localEpisode.ReleaseGroup,
                SceneName = video.SceneName,
                RelativePath = video.OriginalName,
                Languages = localEpisode.Languages,
                MediaInfo = localEpisode.MediaInfo,
                IndexerFlags = localEpisode.IndexerFlags,
                ReleaseType = localEpisode.ReleaseType,
                Size = localEpisode.Size
            };

            var finalPath = BuildFinalPath(localEpisode.Episodes, series, episodeFile, Path.GetExtension(video.FinalPath), localEpisode.CustomFormats);
            var finalRelative = Path.GetRelativePath(link.SavePath, finalPath).Replace('\\', '/');

            // The torrent saves into one folder; a name that would move out of it keeps the name it was mapped to.
            if (finalRelative == video.FinalPath || finalRelative.StartsWith("../") || Path.IsPathRooted(finalRelative))
            {
                return false;
            }

            var oldStem = Path.GetFileNameWithoutExtension(video.FinalPath);
            var newFolder = Path.GetDirectoryName(finalPath);
            var newStem = Path.GetFileNameWithoutExtension(finalPath);

            foreach (var subtitle in link.Files.Where(f => f.IsSubtitle && f.VideoIndex == video.Index && f.State == EngineLinkFileState.Pending))
            {
                var name = subtitle.FinalPath.Replace('\\', '/').Split('/').Last();

                if (name.StartsWith(oldStem, StringComparison.Ordinal))
                {
                    var subtitlePath = FitFileName(Path.Combine(newFolder, string.Concat(newStem, name.AsSpan(oldStem.Length))));

                    subtitle.FinalPath = Path.GetRelativePath(link.SavePath, subtitlePath).Replace('\\', '/');
                }
            }

            video.FinalPath = finalRelative;

            return true;
        }

        private string BuildFinalPath(List<Episode> episodes, Series series, EpisodeFile episodeFile, string extension, List<CustomFormats.CustomFormat> customFormats)
        {
            var path = _buildFileNames.BuildFilePath(episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList(),
                                                     series,
                                                     episodeFile,
                                                     extension,
                                                     null,
                                                     customFormats);

            return FitFileName(path);
        }

        private static string GetSubtitlePath(string videoFinalPath, string suffix)
        {
            return Path.Combine(Path.GetDirectoryName(videoFinalPath), Path.GetFileNameWithoutExtension(videoFinalPath) + suffix);
        }

        private static string GetFolderName(string torrentPath)
        {
            var parts = torrentPath.Replace('\\', '/').Split('/');

            return parts.Length > 1 ? parts[^2].Trim().Trim('.') : null;
        }

        private static MappedVideo FindVideo(string subtitleName, List<MappedVideo> videos)
        {
            var byName = videos.Where(v => subtitleName.StartsWith(Path.GetFileNameWithoutExtension(GetFileName(v.File.Name)), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => GetFileName(v.File.Name).Length)
                .FirstOrDefault();

            return byName ?? (videos.Count == 1 ? videos[0] : null);
        }

        // Keeps the pending name (final name plus the pending overhead) within the filesystem's 255-byte limit.
        private static string FitFileName(string path)
        {
            var name = Path.GetFileName(path);
            var extension = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var limit = MaxFileNameBytes - PendingOverheadBytes;

            if (System.Text.Encoding.UTF8.GetByteCount(name) <= limit)
            {
                return path;
            }

            while (stem.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(stem + extension) > limit)
            {
                stem = stem.Substring(0, stem.Length - 1);
            }

            return Path.Combine(Path.GetDirectoryName(path), stem.TrimEnd(' ', '.') + extension);
        }

        private static EngineLinkFile CreateFile(string savePath, string pendingSuffix, int index, string finalPath, List<Episode> episodes, int? videoIndex)
        {
            var finalRelative = Path.GetRelativePath(savePath, finalPath).Replace('\\', '/');
            var folder = finalRelative.Contains('/') ? finalRelative.Substring(0, finalRelative.LastIndexOf('/') + 1) : string.Empty;
            var name = finalRelative.Substring(folder.Length);

            return new EngineLinkFile
            {
                Index = index,
                IsSubtitle = videoIndex.HasValue,
                VideoIndex = videoIndex,
                Episodes = episodes.Select(e => new EngineLinkEpisode { SeasonNumber = e.SeasonNumber, EpisodeNumber = e.EpisodeNumber }).ToList(),
                FinalPath = finalRelative,

                // Hidden while it downloads and until it passes its checks; the engine adds .!qB while incomplete.
                PendingPath = folder + "." + name + pendingSuffix,
                State = EngineLinkFileState.Pending
            };
        }

        private static string GetCommonFolder(List<string> folders)
        {
            var split = folders.Select(f => f.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar)).ToList();
            var common = split[0].ToList();

            foreach (var parts in split.Skip(1))
            {
                var length = 0;

                while (length < common.Count && length < parts.Length && string.Equals(common[length], parts[length], StringComparison.Ordinal))
                {
                    length++;
                }

                common = common.Take(length).ToList();
            }

            var path = string.Join(Path.DirectorySeparatorChar, common);

            return path.IsNullOrWhiteSpace() ? Path.DirectorySeparatorChar.ToString() : path;
        }

        private class MappedVideo
        {
            public QBittorrentTorrentFile File { get; set; }
            public int Index { get; set; }
            public List<Episode> Episodes { get; set; }
            public string FinalPath { get; set; }
            public string SceneName { get; set; }
            public string OriginalName { get; set; }
        }

        private class MappedSubtitle
        {
            public QBittorrentTorrentFile File { get; set; }
            public int Index { get; set; }
            public MappedVideo Video { get; set; }
            public string Suffix { get; set; }
        }
    }
}
