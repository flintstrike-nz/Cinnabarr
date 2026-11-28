using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Games
{
    /// <summary>How a platform's games are played (docs/cinnabarr/JELLYFIN.md section 4).</summary>
    public enum GamePlayMode
    {
        // Runs in the viewer's browser (EmulatorJS), even on a TV.
        Browser,

        // Runs in the browser where the device keeps up; streamed from the server once cinnabarr-play exists.
        Auto,

        // Only streamed from the server (cinnabarr-play, not built yet): too heavy for a browser.
        Stream
    }

    public class GamePlatform
    {
        public string Key { get; set; }
        public string Name { get; set; }

        // ROM / disc image extensions, lower case with the dot.
        public string[] Extensions { get; set; }

        // Folder names (lower case, no punctuation) people use for the platform.
        public string[] FolderNames { get; set; }

        // The EmulatorJS core that plays it in the browser, or null when there is none.
        public string Core { get; set; }

        public GamePlayMode Play { get; set; }

        // The libretro core the server-side player (cinnabarr-play, G7) will load, or null.
        public string LibretroCore { get; set; }

        // The emulator will not start without a BIOS (or, for Wii U, keys) the owner supplies.
        public bool NeedsBios { get; set; }
    }

    public static class GamePlatforms
    {
        // A disc image bigger than this is taken to be a PlayStation 2 game when nothing else says which (a CD is at most about 700 MB).
        private const long Ps2MinimumBytes = 900L * 1024 * 1024;

        public static readonly IReadOnlyList<GamePlatform> All = new List<GamePlatform>
        {
            new GamePlatform
            {
                Key = "nes",
                Name = "Nintendo Entertainment System",
                Extensions = new[] { ".nes", ".fds", ".unf", ".unif", ".zip", ".7z" },
                FolderNames = new[] { "nes", "famicom", "nintendoentertainmentsystem", "fds", "famicomdisksystem" },
                Core = "nes",
                Play = GamePlayMode.Browser,
                LibretroCore = "fceumm"
            },
            new GamePlatform
            {
                Key = "snes",
                Name = "Super Nintendo",
                Extensions = new[] { ".sfc", ".smc", ".fig", ".swc", ".zip", ".7z" },
                FolderNames = new[] { "snes", "supernintendo", "supernes", "superfamicom", "sfc", "snesmsu1" },
                Core = "snes",
                Play = GamePlayMode.Browser,
                LibretroCore = "snes9x"
            },
            new GamePlatform
            {
                Key = "sms",
                Name = "Sega Master System",
                Extensions = new[] { ".sms", ".zip", ".7z" },
                FolderNames = new[] { "sms", "mastersystem", "segamastersystem", "master" },
                Core = "segaMS",
                Play = GamePlayMode.Browser,
                LibretroCore = "genesis_plus_gx"
            },
            new GamePlatform
            {
                Key = "md",
                Name = "Sega Mega Drive",
                Extensions = new[] { ".md", ".gen", ".smd", ".bin", ".zip", ".7z" },
                FolderNames = new[] { "md", "megadrive", "segamegadrive", "genesis", "segagenesis", "megadrivegenesis" },
                Core = "segaMD",
                Play = GamePlayMode.Browser,
                LibretroCore = "genesis_plus_gx"
            },
            new GamePlatform
            {
                Key = "n64",
                Name = "Nintendo 64",
                Extensions = new[] { ".z64", ".n64", ".v64", ".zip", ".7z" },
                FolderNames = new[] { "n64", "nintendo64" },
                Core = "n64",
                Play = GamePlayMode.Auto,
                LibretroCore = "mupen64plus_next"
            },
            new GamePlatform
            {
                Key = "psx",
                Name = "PlayStation",
                Extensions = new[] { ".cue", ".m3u", ".chd", ".pbp", ".iso", ".img" },
                FolderNames = new[] { "psx", "ps1", "psone", "playstation", "playstation1", "sonyplaystation" },
                Core = "psx",
                Play = GamePlayMode.Auto,
                LibretroCore = "swanstation",
                NeedsBios = true
            },
            new GamePlatform
            {
                Key = "ps2",
                Name = "PlayStation 2",
                Extensions = new[] { ".iso", ".chd", ".cso", ".m3u" },
                FolderNames = new[] { "ps2", "playstation2", "sonyplaystation2" },
                Play = GamePlayMode.Stream,
                LibretroCore = "pcsx2",
                NeedsBios = true
            },
            new GamePlatform
            {
                Key = "gc",
                Name = "Nintendo GameCube",
                Extensions = new[] { ".iso", ".gcm", ".rvz", ".gcz", ".ciso", ".m3u" },
                FolderNames = new[] { "gc", "gcn", "ngc", "gamecube", "nintendogamecube" },
                Play = GamePlayMode.Stream,
                LibretroCore = "dolphin"
            },
            new GamePlatform
            {
                Key = "wii",
                Name = "Nintendo Wii",
                Extensions = new[] { ".iso", ".wbfs", ".rvz", ".gcz", ".wia" },
                FolderNames = new[] { "wii", "nintendowii" },
                Play = GamePlayMode.Stream,
                LibretroCore = "dolphin"
            },
            new GamePlatform
            {
                // Standalone Cemu only; there is no libretro core (G8). Needs the owner's keys.
                Key = "wiiu",
                Name = "Nintendo Wii U",
                Extensions = new[] { ".wua", ".wud", ".wux" },
                FolderNames = new[] { "wiiu", "nintendowiiu" },
                Play = GamePlayMode.Stream,
                NeedsBios = true
            }
        };

        public static GamePlatform Get(string key)
        {
            return All.FirstOrDefault(p => p.Key == key);
        }

        // Extensions more than one platform uses, or that other files use too (.md is also Markdown, .bin is also a
        // CD track): these need a folder name, or for discs the disc header, to say which platform.
        private static readonly string[] Ambiguous = { ".zip", ".7z", ".iso", ".chd", ".m3u", ".md", ".bin", ".rvz", ".gcz", ".ciso" };

        /// <summary>
        /// Which platform a file is for. A folder name on the way (SNES, ps2, "Master System") decides. Without one, the
        /// extension does when only one platform uses it; a GameCube or Wii disc goes by its header, and any other disc
        /// image by size.
        /// </summary>
        public static GamePlatform Detect(IEnumerable<string> folderNames, string extension, long size, byte[] header = null)
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

            var disc = DiscSignature.Identify(header);

            if (disc != null && candidates.Any(p => p.Key == disc))
            {
                return Get(disc);
            }

            if (extension == ".iso" || extension == ".chd")
            {
                return Get(size >= Ps2MinimumBytes ? "ps2" : "psx");
            }

            return null;
        }

        // Whether Detect could use the first bytes of a file with this extension.
        public static bool NeedsHeader(string extension)
        {
            extension = (extension ?? string.Empty).ToLowerInvariant();

            return extension == ".iso" || extension == ".rvz" || extension == ".gcz" || extension == ".wia";
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

    /// <summary>Tells GameCube and Wii discs apart from the first bytes of the file.</summary>
    public static class DiscSignature
    {
        public const int HeaderLength = 0x60;

        // Returns "gc", "wii" or null.
        public static string Identify(byte[] header)
        {
            if (header == null || header.Length < 0x20)
            {
                return null;
            }

            // A plain disc image (.iso, .gcm): the magic words in the disc header.
            if (BigEndian(header, 0x18) == 0x5D1C9EA3)
            {
                return "wii";
            }

            if (BigEndian(header, 0x1C) == 0xC2339F3D)
            {
                return "gc";
            }

            // Dolphin's WIA and RVZ: the disc type follows the 0x48-byte file header (1 = GameCube, 2 = Wii).
            if (header.Length >= 0x4C && (Ascii(header, 0, "WIA\u0001") || Ascii(header, 0, "RVZ\u0001")))
            {
                return DiscType(BigEndian(header, 0x48));
            }

            // Dolphin's GCZ: magic 0xB10BC001 and a sub-type (0 = GameCube, 1 = Wii), little-endian.
            if (LittleEndian(header, 0) == 0xB10BC001)
            {
                var subType = LittleEndian(header, 4);

                return subType == 0 ? "gc" : subType == 1 ? "wii" : null;
            }

            if (Ascii(header, 0, "WBFS"))
            {
                return "wii";
            }

            return null;
        }

        private static string DiscType(uint value)
        {
            return value == 1 ? "gc" : value == 2 ? "wii" : null;
        }

        private static uint BigEndian(byte[] b, int offset)
        {
            return ((uint)b[offset] << 24) | ((uint)b[offset + 1] << 16) | ((uint)b[offset + 2] << 8) | b[offset + 3];
        }

        private static uint LittleEndian(byte[] b, int offset)
        {
            return b[offset] | ((uint)b[offset + 1] << 8) | ((uint)b[offset + 2] << 16) | ((uint)b[offset + 3] << 24);
        }

        private static bool Ascii(byte[] b, int offset, string text)
        {
            if (b.Length < offset + text.Length)
            {
                return false;
            }

            for (var i = 0; i < text.Length; i++)
            {
                if (b[offset + i] != text[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
