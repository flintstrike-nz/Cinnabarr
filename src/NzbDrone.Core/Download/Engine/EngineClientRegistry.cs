using System;
using System.IO;
using System.Threading;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Download.Engine
{
    // Knows where the engine profile lives and which download client is the built-in one.
    // Kept separate from EngineSupervisor, which depends on the download client factory,
    // so tracking and queue code can ask "is this the engine?" without a dependency cycle.
    public interface IEngineClientRegistry
    {
        string EngineFolder { get; }

        // True for the built-in "Cinnabarr Engine" download client.
        bool IsEngineClient(int downloadClientId);

        // The id stored in <engine>/client.id, or 0 if there is none.
        int ReadStoredClientId();

        // Publishes the built-in client's id and stores it for the next start.
        void SetClientId(int id);
    }

    public class EngineClientRegistry : IEngineClientRegistry
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        private int _clientId = -1;

        public EngineClientRegistry(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public string EngineFolder => Path.Combine(_appFolderInfo.AppDataFolder, "engine");

        private string ClientIdFile => Path.Combine(EngineFolder, "client.id");

        public bool IsEngineClient(int downloadClientId)
        {
            var clientId = Volatile.Read(ref _clientId);

            if (clientId < 0)
            {
                // Only fill in the initial value; never overwrite an id published meanwhile (SetClientId).
                Interlocked.CompareExchange(ref _clientId, ReadStoredClientId(), -1);
                clientId = Volatile.Read(ref _clientId);
            }

            return clientId > 0 && clientId == downloadClientId;
        }

        public int ReadStoredClientId()
        {
            try
            {
                return _diskProvider.FileExists(ClientIdFile) && int.TryParse(_diskProvider.ReadAllText(ClientIdFile).Trim(), out var id) ? id : 0;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to read the built-in download client id");
                return 0;
            }
        }

        public void SetClientId(int id)
        {
            if (id <= 0)
            {
                return;
            }

            Volatile.Write(ref _clientId, id);

            try
            {
                _diskProvider.WriteAllText(ClientIdFile, id.ToString());
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to store the built-in download client id");
            }
        }
    }
}
