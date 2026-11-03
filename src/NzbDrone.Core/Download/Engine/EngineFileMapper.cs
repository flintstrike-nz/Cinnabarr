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
        EngineFileMap Map(Series series, List<Episode> episodes, QualityModel quality, string releaseTitle, List<QBittorrentTorrentFile> files);
    }

    public class EngineFileMapper : IEngineFileMapper
    {
        public const string PendingSuffix = ".pending";

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
            return path != null && path.EndsWith(PendingSuffix, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - PendingSuffix.Length)
                : path;
        }

        public static string GetFileName(string torrentPath)
        {
            return torrentPath.Replace('\\', '/').Split('/').Last();
        }

        public EngineFileMap Map(Series series, List<Episode> episodes, QualityModel quality, string releaseTitle, List<QBittorrentTorrentFile> files)
        {
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

                var episodeFile = new EpisodeFile
                {
                    Quality = quality,
                    ReleaseGroup = fileInfo?.ReleaseGroup ?? releaseInfo?.ReleaseGroup,
                    SceneName = releaseTitle,
                    Languages = releaseInfo?.Languages ?? new(),
                    Size = file.Size
                };

                var finalPath = _buildFileNames.BuildFilePath(fileEpisodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList(),
                                                              series,
                                                              episodeFile,
                                                              Path.GetExtension(GetFileName(file.Name)));

                mappedVideos.Add(new MappedVideo { File = file, Index = index, Episodes = fileEpisodes, FinalPath = finalPath });
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
                map.Files.Add(CreateFile(savePath, video.Index, video.FinalPath, video.Episodes, null));
            }

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

                var finalPath = Path.Combine(Path.GetDirectoryName(video.FinalPath), Path.GetFileNameWithoutExtension(video.FinalPath) + suffix);

                map.Files.Add(CreateFile(savePath, index, finalPath, new List<Episode>(), video.Index));
            }

            return map;
        }

        private static MappedVideo FindVideo(string subtitleName, List<MappedVideo> videos)
        {
            var byName = videos.Where(v => subtitleName.StartsWith(Path.GetFileNameWithoutExtension(GetFileName(v.File.Name)), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => GetFileName(v.File.Name).Length)
                .FirstOrDefault();

            return byName ?? (videos.Count == 1 ? videos[0] : null);
        }

        private static EngineLinkFile CreateFile(string savePath, int index, string finalPath, List<Episode> episodes, int? videoIndex)
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
                PendingPath = folder + "." + name + PendingSuffix,
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
        }
    }
}
