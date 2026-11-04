using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
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
        public const string StagingFolderName = ".embarr-staging";
        private const string StandIn = "x.";

        // Sonarr's subtitle tags (LanguageParser).
        private static readonly HashSet<string> SubtitleTags = new() { "forced", "foreign", "default", "cc", "psdh", "sdh" };
        private static readonly Regex TrailingTagsRegex = new(@"(?:[-_. ](?:forced|foreign|default|cc|psdh|sdh))+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RegionalCodeRegex = new(@"(?:^|[-_. ])(?<code>[a-z]{2}-[a-z]{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly HashSet<string> VobSubExtensions = new(StringComparer.OrdinalIgnoreCase) { ".idx", ".sub" };

        private static readonly Regex PendingRegex = new(@"\.[0-9a-f]{8}\.pending$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SampleRegex = new(@"(?:^|[\W_])sample(?:[\W_]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly IParsingService _parsingService;
        private readonly IBuildFileNames _buildFileNames;
        private readonly IConfigService _configService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public EngineFileMapper(IParsingService parsingService, IBuildFileNames buildFileNames, IConfigService configService, IRootFolderService rootFolderService, IDiskProvider diskProvider, Logger logger)
        {
            _parsingService = parsingService;
            _buildFileNames = buildFileNames;
            _configService = configService;
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
            _logger = logger;
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

            // With staging (roadmap §4.8), files download into <root>/.embarr-staging/<torrent>/ and the torrent saves
            // into the root folder, so moving a finished file to its final path is a rename on the same drive.
            string stagingFolder = null;
            var root = _configService.EngineUseStagingFolder ? GetStagingRoot(series, savePath) : null;

            if (root != null)
            {
                savePath = root;
                stagingFolder = StagingFolderName + "/" + downloadId.Substring(0, 8).ToLowerInvariant() + "/";
            }

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

                var folder = GetFolderName(file.Name);

                subtitles.Add(new MappedSubtitle
                {
                    File = file,
                    Index = index,
                    Video = video,
                    Language = ParseSubtitleLanguage(subtitleName, folder),
                    LanguageTags = ParseSubtitleTags(subtitleName, folder),
                    Extension = Path.GetExtension(subtitleName).ToLowerInvariant(),

                    // A VobSub index and its data file are one track and must keep the same name.
                    Track = VobSubExtensions.Contains(Path.GetExtension(subtitleName))
                        ? file.Name.Replace('\\', '/')[..^Path.GetExtension(subtitleName).Length]
                        : index.ToString()
                });
            }

            // Named like any imported subtitle (roadmap §4.11): the video's name, the language code, the tags and the
            // extension, e.g. "….en.forced.srt". Tracks of a video that would get the same name (a VobSub pair counts as
            // one track) are numbered first ("….1.en.srt", "….2.en.srt"); the number is saved as the subtitle's title,
            // so library renames keep it.
            foreach (var group in subtitles.GroupBy(s => (s.Video.Index,
                                                         BuildSuffix(s.Language, null, s.LanguageTags, string.Empty).ToLowerInvariant(),
                                                         VobSubExtensions.Contains(s.Extension) ? ".idx" : s.Extension)))
            {
                var tracks = group.GroupBy(s => s.Track).ToList();

                foreach (var (track, number) in tracks.Select((t, i) => (t, i + 1)))
                {
                    foreach (var subtitle in track)
                    {
                        subtitle.Title = tracks.Count > 1 ? number.ToString() : null;
                        subtitle.Suffix = BuildSuffix(subtitle.Language, subtitle.Title, subtitle.LanguageTags, subtitle.Extension);
                    }
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

                var videoFile = CreateFile(savePath, stagingFolder, pendingSuffix, video.Index, Path.Combine(folder, stem + extension), video.Episodes, null);

                videoFile.SceneName = video.SceneName;
                videoFile.OriginalName = video.OriginalName;
                map.Files.Add(videoFile);

                foreach (var subtitle in videoSubtitles)
                {
                    var subtitleFile = CreateFile(savePath, stagingFolder, pendingSuffix, subtitle.Index, Path.Combine(folder, stem + subtitle.Suffix), new List<Episode>(), video.Index);

                    subtitleFile.LanguageId = subtitle.Language.Id;
                    subtitleFile.LanguageTags = subtitle.LanguageTags;
                    subtitleFile.SubtitleTitle = subtitle.Title;
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

        // The language a subtitle's name gives (or its folder's, e.g. Subs/English/), parsed as Sonarr parses
        // subtitles. A name or folder that is only a code ("en.srt", "Subs/eng/") gets a stand-in prefix first.
        public static Language ParseSubtitleLanguage(string fileName, string folder = null)
        {
            var stem = StripTrailingTags(Path.GetFileNameWithoutExtension(fileName));

            // A regional code ("….pt-BR.srt"), which the parser would read as its country part.
            var regional = RegionalCodeRegex.Match(stem);
            var language = regional.Success ? IsoLanguages.Find(regional.Groups["code"].Value)?.Language ?? Language.Unknown : Language.Unknown;

            if (language == Language.Unknown)
            {
                language = LanguageParser.ParseSubtitleLanguage(StandIn + fileName);
            }

            // A full language name followed by tags ("….English.forced.srt", "….Portuguese (Brazil).sdh.srt"): parse
            // it without them.
            if (language == Language.Unknown)
            {
                language = LanguageParser.ParseSubtitleLanguage(StandIn + stem + Path.GetExtension(fileName));
            }

            if (language == Language.Unknown && folder.IsNotNullOrWhiteSpace())
            {
                language = LanguageParser.ParseSubtitleLanguage(StandIn + folder + Path.GetExtension(fileName));
            }

            return NormalizeSubtitleLanguage(language);
        }

        // A language without a two-letter code (for example "Original") can't appear in a subtitle name, and
        // Sonarr's library rename can't name it either: it counts as unknown.
        public static Language NormalizeSubtitleLanguage(Language language)
        {
            return language == null || language == Language.Unknown || IsoLanguages.Get(language) == null ? Language.Unknown : language;
        }

        // Removes only the trailing tags, keeping the separators of what comes before them.
        private static string StripTrailingTags(string stem)
        {
            return TrailingTagsRegex.Replace(stem, string.Empty);
        }

        // The tags (forced, sdh, cc…) at the end of a subtitle's name, whether or not the name gives a language, plus
        // a folder that is itself a tag (e.g. Subs/SDH/).
        public static List<string> ParseSubtitleTags(string fileName, string folder = null)
        {
            var tags = new List<string>();
            var tokens = Path.GetFileNameWithoutExtension(fileName).Split('-', '_', '.', ' ');

            // Read back from the end over tags and language words; stop at the first other word.
            foreach (var token in tokens.Reverse().Select(t => t.ToLowerInvariant()))
            {
                if (SubtitleTags.Contains(token))
                {
                    tags.Insert(0, token);
                }
                else if (token.Length == 0 || !IsLanguageWord(token))
                {
                    break;
                }
            }

            var folderTag = folder?.ToLowerInvariant();

            if (folderTag != null && SubtitleTags.Contains(folderTag) && !tags.Contains(folderTag))
            {
                tags.Add(folderTag);
            }

            return tags;
        }

        private static bool IsLanguageWord(string token)
        {
            return (token.Length is 2 or 3 && IsoLanguages.Find(token) != null) ||
                   Language.All.Any(l => string.Equals(l.Name, token, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildSuffix(Language language, string title, List<string> languageTags, string extension)
        {
            var suffix = new StringBuilder();

            if (title != null)
            {
                suffix.Append('.').Append(title);
            }

            var isoLanguage = NormalizeSubtitleLanguage(language) == Language.Unknown ? null : IsoLanguages.Get(language);

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

        // The root folder holding the series, when it is on the same drive as the files' final folder (a rename
        // can't cross drives); otherwise null and the files download next to their final path.
        private string GetStagingRoot(Series series, string savePath)
        {
            try
            {
                var root = _rootFolderService.GetBestRootFolderPath(series.Path);

                if (root.IsNullOrWhiteSpace() || !root.IsParentPath(savePath))
                {
                    return null;
                }

                var rootMount = _diskProvider.GetMount(root)?.RootDirectory;
                var finalMount = _diskProvider.GetMount(savePath)?.RootDirectory;

                if (rootMount == null || !rootMount.PathEquals(finalMount ?? string.Empty))
                {
                    _logger.Debug("'{0}' isn't on the same drive as its root folder; not staging it", savePath);
                    return null;
                }

                return root;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to check the drive of '{0}'; not staging it", savePath);
                return null;
            }
        }

        private static EngineLinkFile CreateFile(string savePath, string stagingFolder, string pendingSuffix, int index, string finalPath, List<Episode> episodes, int? videoIndex)
        {
            var finalRelative = Path.GetRelativePath(savePath, finalPath).Replace('\\', '/');
            var finalFolder = finalRelative.Contains('/') ? finalRelative.Substring(0, finalRelative.LastIndexOf('/') + 1) : string.Empty;
            var name = finalRelative.Substring(finalFolder.Length);
            var folder = stagingFolder ?? finalFolder;

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
            public string Title { get; set; }
            public string Track { get; set; }
        }
    }
}
