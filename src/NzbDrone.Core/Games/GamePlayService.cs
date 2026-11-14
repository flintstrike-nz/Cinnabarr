using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Games
{
    public class GamePlayInfo
    {
        public bool Playable { get; set; }
        public string Reason { get; set; }
        public string Core { get; set; }
        public bool HasState { get; set; }
        public bool NeedsBios { get; set; }
        public bool HasBios { get; set; }
    }

    public interface IGamePlayService
    {
        GamePlayInfo GetInfo(Game game);

        // The file the browser emulator loads: the ROM itself, or a zip of a .cue and its tracks.
        string GetPlayablePath(Game game);

        string GetStatePath(Game game);

        // The state file if there is one, else null.
        string FindState(Game game);
        Task<bool> SaveStateAsync(Game game, Stream content);

        // The owner's BIOS for a platform if they put one there, else null. Only known platforms are looked up.
        string FindBios(string platformKey);
    }

    /// <summary>
    /// What it takes to play a game in the browser. The emulator itself (EmulatorJS) is loaded by the browser from its
    /// own site; Cinnabarr only serves the game, the player's save state and, if the owner put one there, a BIOS.
    /// </summary>
    public class GamePlayService : IGamePlayService
    {
        // Platforms the browser emulator has a core for. PlayStation 2 has none.
        private static readonly string[] Playable = { "sms", "snes", "psx" };

        // Platforms whose emulator will not start without a BIOS the owner supplies.
        private static readonly string[] NeedBios = { "psx" };

        private static readonly Regex CueFile = new Regex("^\\s*FILE\\s+\"(?<file>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public GamePlayService(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public GamePlayInfo GetInfo(Game game)
        {
            var platform = GamePlatforms.Get(game.PlatformKey);
            var info = new GamePlayInfo { Core = platform?.Core };

            if (platform == null || !Playable.Contains(platform.Key))
            {
                info.Reason = $"{platform?.Name ?? game.PlatformKey} cannot be played in the browser yet.";
                return info;
            }

            if (!_diskProvider.FileExists(game.Path))
            {
                info.Reason = "The game file is missing.";
                return info;
            }

            info.Playable = true;
            info.HasState = _diskProvider.FileExists(GetStatePath(game));
            info.NeedsBios = NeedBios.Contains(platform.Key);
            info.HasBios = FindBios(platform.Key) != null;

            return info;
        }

        public string GetPlayablePath(Game game)
        {
            if (!string.Equals(Path.GetExtension(game.Path), ".cue", StringComparison.OrdinalIgnoreCase))
            {
                return game.Path;
            }

            var folder = Path.Combine(_appFolderInfo.AppDataFolder, "game-cache");
            var zip = Path.Combine(folder, $"{game.Id}-{game.Size}.zip");

            if (_diskProvider.FileExists(zip))
            {
                return zip;
            }

            _diskProvider.CreateFolder(folder);

            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(game.Path, Path.GetFileName(game.Path), CompressionLevel.NoCompression);

                foreach (var track in CueTracks(File.ReadAllText(game.Path)))
                {
                    var trackPath = Path.Combine(Path.GetDirectoryName(game.Path), track);

                    if (_diskProvider.FileExists(trackPath))
                    {
                        archive.CreateEntryFromFile(trackPath, track, CompressionLevel.NoCompression);
                    }
                    else
                    {
                        _logger.Warn("Track {0} of {1} is missing", track, game.Title);
                    }
                }
            }

            return zip;
        }

        public string GetStatePath(Game game)
        {
            return Path.Combine(_appFolderInfo.AppDataFolder, "game-saves", $"{game.Id}.state");
        }

        public string FindState(Game game)
        {
            var path = GetStatePath(game);

            return _diskProvider.FileExists(path) ? path : null;
        }

        public async Task<bool> SaveStateAsync(Game game, Stream content)
        {
            var path = GetStatePath(game);
            var temp = path + ".tmp";

            _diskProvider.CreateFolder(Path.GetDirectoryName(path));

            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                await content.CopyToAsync(file);
            }

            // An empty upload must never replace a real save.
            if (new FileInfo(temp).Length == 0)
            {
                _diskProvider.DeleteFile(temp);
                return false;
            }

            File.Move(temp, path, true);

            return true;
        }

        public string FindBios(string platformKey)
        {
            var path = GetBiosPath(GamePlatforms.Get(platformKey)?.Key);

            return path != null && _diskProvider.FileExists(path) ? path : null;
        }

        private string GetBiosPath(string platformKey)
        {
            return platformKey == null ? null : Path.Combine(_appFolderInfo.AppDataFolder, "bios", $"{platformKey}.bin");
        }

        /// <summary>The track files a .cue sheet lists.</summary>
        internal static IEnumerable<string> CueTracks(string cue)
        {
            return CueFile.Matches(cue).Select(m => Path.GetFileName(m.Groups["file"].Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
