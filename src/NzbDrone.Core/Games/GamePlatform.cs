using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Games
{
    public class GamePlatform
    {
        public string Key { get; set; }
        public string Name { get; set; }

        // ROM / disc image extensions, lower case with the dot.
        public string[] Extensions { get; set; }

        // Folder names (lower case, no punctuation) people use for the platform.
        public string[] FolderNames { get; set; }

        // The EmulatorJS core that plays it (G3).
        public string Core { get; set; }
    }

    public static class GamePlatforms
    {
        // A disc image bigger than this is taken to be a PlayStation 2 game when nothing else says which (a CD is at most about 700 MB).
        private const long Ps2MinimumBytes = 900L * 1024 * 1024;

        public static readonly IReadOnlyList<GamePlatform> All = new List<GamePlatform>
        {
            new GamePlatform
            {
                Key = "sms",
                Name = "Sega Master System",
                Extensions = new[] { ".sms", ".zip", ".7z" },
                FolderNames = new[] { "sms", "mastersystem", "segamastersystem", "master" },
                Core = "segaMS"
            },
            new GamePlatform
            {
                Key = "snes",
                Name = "Super Nintendo",
                Extensions = new[] { ".sfc", ".smc", ".fig", ".swc", ".zip", ".7z" },
                FolderNames = new[] { "snes", "supernintendo", "supernes", "superfamicom", "sfc", "snesmsu1" },
                Core = "snes"
            },
            new GamePlatform
            {
                Key = "psx",
                Name = "PlayStation",
                Extensions = new[] { ".cue", ".m3u", ".chd", ".pbp", ".iso", ".img" },
                FolderNames = new[] { "psx", "ps1", "psone", "playstation", "playstation1", "sonyplaystation" },
                Core = "psx"
            },
            new GamePlatform
            {
                Key = "ps2",
                Name = "PlayStation 2",
                Extensions = new[] { ".iso", ".chd", ".cso", ".m3u" },
                FolderNames = new[] { "ps2", "playstation2", "sonyplaystation2" },
                Core = "ps2"
            }
        };

        public static GamePlatform Get(string key)
        {
            return All.FirstOrDefault(p => p.Key == key);
        }

        // Archives, disc images and playlists that more than one platform uses.
        private static readonly string[] Ambiguous = { ".zip", ".7z", ".iso", ".chd", ".m3u" };

        /// <summary>
        /// Which platform a file is for. A folder name on the way (SNES, ps2, "Master System") decides. Without one, the
        /// extension does when only one platform uses it; a disc image goes by size.
        /// </summary>
        public static GamePlatform Detect(IEnumerable<string> folderNames, string extension, long size)
        {
            extension = (extension ?? string.Empty).ToLowerInvariant();

            foreach (var folder in folderNames.Reverse())
            {
                var normalised = Normalise(folder);
                var byFolder = All.FirstOrDefault(p => p.FolderNames.Contains(normalised) && p.Extensions.Contains(extension));

                if (byFolder != null)
                {
                    return byFolder;
                }
            }

            var candidates = All.Where(p => p.Extensions.Contains(extension)).ToList();

            if (candidates.Count == 1 && !Ambiguous.Contains(extension))
            {
                return candidates[0];
            }

            if (extension == ".iso" || extension == ".chd")
            {
                return Get(size >= Ps2MinimumBytes ? "ps2" : "psx");
            }

            return null;
        }

        public static string Normalise(string folderName)
        {
            return new string((folderName ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        public static bool IsRomExtension(string extension)
        {
            return All.Any(p => p.Extensions.Contains((extension ?? string.Empty).ToLowerInvariant()));
        }
    }
}
