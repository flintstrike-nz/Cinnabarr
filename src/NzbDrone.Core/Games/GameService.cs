using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Games
{
    public interface IGameService
    {
        List<Game> GetAll();
        Game Get(int id);
        void Delete(int id, bool deleteFile);
        void Scan();
    }

    public class GameService : IGameService, IExecute<ScanGamesCommand>
    {
        private readonly IGameRepository _repository;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly Logger _logger;

        public GameService(IGameRepository repository,
                           IRootFolderService rootFolderService,
                           IDiskProvider diskProvider,
                           IRecycleBinProvider recycleBinProvider,
                           Logger logger)
        {
            _repository = repository;
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _logger = logger;
        }

        public List<Game> GetAll() => _repository.All().ToList();

        public Game Get(int id) => _repository.Get(id);

        public void Delete(int id, bool deleteFile)
        {
            var game = _repository.Get(id);

            if (deleteFile && _diskProvider.FileExists(game.Path))
            {
                _recycleBinProvider.DeleteFile(game.Path, "Games");
            }

            _repository.Delete(game);
        }

        public void Scan()
        {
            var roots = _rootFolderService.All().Where(r => r.Library == LibraryType.Games && _diskProvider.FolderExists(r.Path)).ToList();
            var existing = _repository.All().ToDictionary(g => g.Path, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var added = 0;

            foreach (var root in roots)
            {
                foreach (var found in FindGames(root.Path))
                {
                    seen.Add(found.Path);

                    if (existing.TryGetValue(found.Path, out var game))
                    {
                        if (game.Size != found.Size || game.PlatformKey != found.PlatformKey)
                        {
                            game.Size = found.Size;
                            game.PlatformKey = found.PlatformKey;
                            _repository.Update(game);
                        }

                        continue;
                    }

                    found.RootFolderPath = root.Path;
                    found.Added = DateTime.UtcNow;
                    _repository.Insert(found);
                    added++;
                }
            }

            // Forget games whose file is gone, but only from a root that could be read (an unplugged drive is not a delete).
            var readable = roots.Select(r => r.Path).ToHashSet(StringComparer.Ordinal);
            var gone = existing.Values.Where(g => readable.Contains(g.RootFolderPath) && !seen.Contains(g.Path)).ToList();

            if (gone.Count > 0)
            {
                _repository.DeleteMany(gone);
            }

            _logger.Info("Games: {0} new, {1} gone, {2} total", added, gone.Count, seen.Count);
        }

        public void Execute(ScanGamesCommand message)
        {
            Scan();
        }

        /// <summary>The games in one root folder: a .cue (with its tracks) or .m3u (with its discs) counts once.</summary>
        internal IEnumerable<Game> FindGames(string root)
        {
            var files = _diskProvider.GetFiles(root, true)
                                     .Where(f => GamePlatforms.IsRomExtension(System.IO.Path.GetExtension(f)))
                                     .OrderBy(f => f, StringComparer.Ordinal)
                                     .ToList();

            // In a folder with a playlist, the playlist is the game and the discs listed in it are not.
            var playlists = files.Where(f => IsExtension(f, ".m3u")).Select(System.IO.Path.GetDirectoryName).ToHashSet(StringComparer.Ordinal);

            foreach (var file in files)
            {
                var extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
                var folder = System.IO.Path.GetDirectoryName(file);

                if (extension != ".m3u" && playlists.Contains(folder) && (extension == ".cue" || extension == ".chd" || extension == ".iso" || extension == ".img"))
                {
                    continue;
                }

                var size = SizeOf(file, extension, folder);
                var relative = System.IO.Path.GetRelativePath(root, folder);
                var folders = relative == "." ? Enumerable.Empty<string>() : relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                var platform = GamePlatforms.Detect(new[] { System.IO.Path.GetFileName(root) }.Concat(folders), extension, size);

                if (platform == null)
                {
                    continue;
                }

                var (title, region) = GameTitleParser.Parse(System.IO.Path.GetFileNameWithoutExtension(file));

                yield return new Game
                {
                    PlatformKey = platform.Key,
                    Title = title,
                    SortTitle = GameTitleParser.SortTitle(title),
                    Region = region,
                    Path = file,
                    Size = size
                };
            }
        }

        private long SizeOf(string file, string extension, string folder)
        {
            var size = _diskProvider.GetFileSize(file);

            if (extension == ".cue")
            {
                var stem = System.IO.Path.GetFileNameWithoutExtension(file);

                foreach (var track in _diskProvider.GetFiles(folder, false).Where(f => IsExtension(f, ".bin") && System.IO.Path.GetFileName(f).StartsWith(stem, StringComparison.OrdinalIgnoreCase)))
                {
                    size += _diskProvider.GetFileSize(track);
                }
            }

            return size;
        }

        private static bool IsExtension(string file, string extension) => string.Equals(System.IO.Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase);
    }
}
