using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Languages;
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
        public const int MaxSuffixBytes = 64;

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

            // Files whose episodes overlap (a release and a re-encode, or E01 next to E01-E02): keep one per episode,
            // preferring the file that covers more episodes, then the larger one. A torrent never maps two files to the
            // same episode, so one of its files never has to replace another.
            var kept = new List<MappedVideo>();

            foreach (var video in mappedVideos.OrderByDescending(v => v.Episodes.Count).ThenByDescending(v => v.File.Size))
            {
                if (!kept.Any(k => k.Episodes.Any(e => video.Episodes.Any(v => v.Id == e.Id))))
                {
                    kept.Add(video);
                }
            }

            mappedVideos = kept.OrderBy(v => v.Index).ToList();

            if (mappedVideos.Count == 0)
            {
                return null;
            }

            var savePath = GetCommonFolder(mappedVideos.Select(v => Path.GetDirectoryName(v.FinalPath)).ToList());
            var map = new EngineFileMap { SavePath = savePath };

            var subtitles = new List<MappedSubtitle>();

            foreach (var (file, index) in indexed.Where(f => EngineFileWhitelist.IsSubtitle(f.File.Name)))
            {
                var subtitleName = GetFileName(file.Name);
                var video = FindVideo(subtitleName, mappedVideos);

                if (video == null)
                {
                    continue;
                }

                // Language and tags come from the file name, or from its folder (for example Subs/English/2_Eng.srt).
                var language = LanguageParser.ParseSubtitleLanguage(subtitleName);
                var folder = GetFolderName(file.Name);

                if (language == Language.Unknown && folder.IsNotNullOrWhiteSpace())
                {
                    language = LanguageParser.ParseSubtitleLanguage(folder + Path.GetExtension(subtitleName));
                }

                subtitles.Add(new MappedSubtitle
                {
                    File = file,
                    Index = index,
                    Video = video,
                    Language = language,
                    LanguageTags = LanguageParser.ParseLanguageTags(subtitleName),
                    Extension = Path.GetExtension(subtitleName).ToLowerInvariant()
                });
            }

            // Named like any imported subtitle (roadmap §4.11): the video's name, then a copy number when several share
            // a language, the language code, the tags and the extension, e.g. "….en.forced.srt".
            foreach (var group in subtitles.GroupBy(s => (s.Video.Index, s.Language.Id, string.Join(".", s.LanguageTags), s.Extension)))
            {
                var copy = 1;

                foreach (var subtitle in group)
                {
                    subtitle.Copy = group.Count() > 1 ? copy++ : 0;
                    subtitle.Suffix = BuildSuffix(subtitle.Language, subtitle.Copy, subtitle.LanguageTags, subtitle.Extension);
                }
            }

            // A language without a two-letter code is named like an unknown one: keep any such names apart.
            foreach (var group in subtitles.GroupBy(s => (s.Video.Index, s.Suffix.ToLowerInvariant())).Where(g => g.Count() > 1))
            {
                foreach (var (subtitle, position) in group.Skip(1).Select((s, i) => (s, i + 2)))
                {
                    subtitle.Suffix = "." + position + subtitle.Suffix;
                }
            }

            foreach (var video in mappedVideos)
            {
                // The video's name is shortened (if needed) so that its own and every subtitle's pending name fit,
                // and every subtitle keeps the video's exact name in front of its suffix.
                var videoSubtitles = subtitles.Where(s => s.Video == video).ToList();
                var folder = Path.GetDirectoryName(video.FinalPath);
                var extension = Path.GetExtension(video.FinalPath);
                var stem = FitStem(Path.GetFileNameWithoutExtension(video.FinalPath), videoSubtitles.Select(s => s.Suffix).Append(extension));

                var videoFile = CreateFile(savePath, pendingSuffix, video.Index, Path.Combine(folder, stem + extension), video.Episodes, null);

                videoFile.SceneName = video.SceneName;
                videoFile.OriginalName = video.OriginalName;
                map.Files.Add(videoFile);

                foreach (var subtitle in videoSubtitles)
                {
                    var subtitleFile = CreateFile(savePath, pendingSuffix, subtitle.Index, Path.Combine(folder, stem + subtitle.Suffix), new List<Episode>(), video.Index);

                    subtitleFile.LanguageId = subtitle.Language.Id;
                    subtitleFile.LanguageTags = subtitle.LanguageTags;
                    subtitleFile.Copy = subtitle.Copy;
                    map.Files.Add(subtitleFile);
                }
            }

            map.Files = map.Files.OrderBy(f => f.IsSubtitle).ThenBy(f => f.Index).ToList();

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

            var oldStem = Path.GetFileNameWithoutExtension(video.FinalPath);
            var extension = Path.GetExtension(video.FinalPath);
            var subtitles = link.Files
                .Where(f => f.IsSubtitle && f.VideoIndex == video.Index && f.State == EngineLinkFileState.Pending)
                .Select(f => (File: f, Name: GetFileName(f.FinalPath)))
                .Where(f => f.Name.StartsWith(oldStem, StringComparison.Ordinal))
                .Select(f => (f.File, Suffix: f.Name.Substring(oldStem.Length)))
                .ToList();

            var rawPath = BuildFinalPath(localEpisode.Episodes, series, episodeFile, extension, localEpisode.CustomFormats);
            var newFolder = Path.GetDirectoryName(rawPath);
            var newStem = FitStem(Path.GetFileNameWithoutExtension(rawPath), subtitles.Select(s => s.Suffix).Append(extension));
            var finalPath = Path.Combine(newFolder, newStem + extension);
            var finalRelative = Path.GetRelativePath(link.SavePath, finalPath).Replace('\\', '/');

            // The torrent saves into one folder; a name that would move out of it keeps the name it was mapped to.
            if (finalRelative == video.FinalPath || finalRelative.StartsWith("../") || Path.IsPathRooted(finalRelative))
            {
                return false;
            }

            foreach (var (subtitle, suffix) in subtitles)
            {
                subtitle.FinalPath = Path.GetRelativePath(link.SavePath, Path.Combine(newFolder, newStem + suffix)).Replace('\\', '/');
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

            return path;
        }

        private static string BuildSuffix(Language language, int copy, List<string> languageTags, string extension)
        {
            var suffix = new StringBuilder();

            if (copy > 0)
            {
                suffix.Append('.').Append(copy);
            }

            var isoLanguage = language == Language.Unknown ? null : IsoLanguages.Get(language);

            if (isoLanguage != null)
            {
                suffix.Append('.').Append(isoLanguage.TwoLetterCode);
            }

            foreach (var tag in languageTags)
            {
                suffix.Append('.').Append(tag);
            }

            return CapSuffix(suffix.Append(extension).ToString());
        }

        // A subtitle's own part of its name (for example ".en.srt") is kept short, so it never squeezes out the
        // video's name; the end, which holds the language and extension, is what is kept.
        private static string CapSuffix(string suffix)
        {
            if (Encoding.UTF8.GetByteCount(suffix) <= MaxSuffixBytes)
            {
                return suffix;
            }

            while (Encoding.UTF8.GetByteCount(suffix) > MaxSuffixBytes - 1)
            {
                suffix = suffix.Substring(char.IsHighSurrogate(suffix[0]) && suffix.Length > 1 ? 2 : 1);
            }

            return "." + suffix.TrimStart('.');
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

        // Shortens a name so that, with the longest of the given endings (extension or subtitle suffix), its pending
        // name (final name plus the pending overhead) stays within the filesystem's 255-byte limit.
        private static string FitStem(string stem, IEnumerable<string> endings)
        {
            var limit = MaxFileNameBytes - PendingOverheadBytes - endings.Max(e => Encoding.UTF8.GetByteCount(e));

            if (Encoding.UTF8.GetByteCount(stem) <= limit)
            {
                return stem;
            }

            while (stem.Length > 0 && Encoding.UTF8.GetByteCount(stem) > limit)
            {
                stem = stem.Substring(0, stem.Length - (stem.Length > 1 && char.IsLowSurrogate(stem[^1]) ? 2 : 1));
            }

            return stem.TrimEnd(' ', '.');
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
            public Language Language { get; set; }
            public List<string> LanguageTags { get; set; }
            public string Extension { get; set; }
            public int Copy { get; set; }
        }
    }
}
