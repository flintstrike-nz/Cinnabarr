using System;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.Games
{
    public interface IGameCoverService
    {
        // The path of the cover image, downloading it the first time; null when the game has none.
        string GetCoverPath(Game game);
    }

    /// <summary>Keeps a game's cover on disk so the browser never talks to the provider.</summary>
    public class GameCoverService : IGameCoverService
    {
        private readonly IHttpClient _httpClient;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public GameCoverService(IHttpClient httpClient, IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, Logger logger)
        {
            _httpClient = httpClient;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public string GetCoverPath(Game game)
        {
            if (string.IsNullOrEmpty(game.CoverUrl))
            {
                return null;
            }

            var folder = Path.Combine(_appFolderInfo.AppDataFolder, "game-covers");
            var path = Path.Combine(folder, $"{game.Id}.jpg");

            if (_diskProvider.FileExists(path))
            {
                return path;
            }

            try
            {
                _diskProvider.CreateFolder(folder);
                _httpClient.DownloadFile(game.CoverUrl, path);

                return _diskProvider.FileExists(path) ? path : null;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not download the cover for {0}", game.Title);
                return null;
            }
        }
    }
}
