using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.Engine
{
    public enum EngineStatus
    {
        NotStarted,
        NotFound,
        Starting,
        Running,
        Failed
    }

    public interface IEngineSupervisor
    {
        EngineStatus Status { get; }
        string EnginePath { get; }
    }

    // Starts and supervises embarr-engine (the headless qBittorrent fork, roadmap §3)
    // and keeps the built-in "Embarr Engine" download client pointed at it.
    public class EngineSupervisor : IEngineSupervisor,
                                    IHandle<ApplicationStartedEvent>,
                                    IHandle<ApplicationShutdownRequested>
    {
        public const string EngineDefinitionName = "Embarr Engine";
        public const string EnginePathVariable = "EMBARR_ENGINE_PATH";
        public const string ApiKeyVariable = "EMBARR_ENGINE_API_KEY";

        private const string ApiKeyPrefix = "qbt_";
        private const int ApiKeyLength = 28;
        private const string ApiKeyChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        private const int MaxFailedHealthChecks = 3;

        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly IProcessProvider _processProvider;
        private readonly IHttpClient _httpClient;
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly Logger _logger;

        private readonly object _lock = new object();
        private bool _processExitHooked;
        private CancellationTokenSource _cancellation;
        private Task _monitor;
        private Process _process;
        private int _port;
        private string _apiKey;

        public EngineSupervisor(IAppFolderInfo appFolderInfo,
                                IDiskProvider diskProvider,
                                IProcessProvider processProvider,
                                IHttpClient httpClient,
                                IDownloadClientFactory downloadClientFactory,
                                Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _processProvider = processProvider;
            _httpClient = httpClient;
            _downloadClientFactory = downloadClientFactory;
            _logger = logger;
        }

        public EngineStatus Status { get; private set; } = EngineStatus.NotStarted;
        public string EnginePath { get; private set; }

        public string EngineFolder => Path.Combine(_appFolderInfo.AppDataFolder, "engine");

        private string PidFile => Path.Combine(EngineFolder, "engine.pid");

        public void Handle(ApplicationStartedEvent message)
        {
            EnginePath = ResolveEnginePath();

            if (EnginePath == null)
            {
                Status = EngineStatus.NotFound;
                _logger.Warn("embarr-engine was not found next to Embarr and {0} is not set. Downloads are unavailable.", EnginePathVariable);
                return;
            }

            _apiKey = GetOrCreateApiKey();

            lock (_lock)
            {
                if (_cancellation != null && !_cancellation.IsCancellationRequested)
                {
                    return;
                }

                if (!_processExitHooked)
                {
                    // The host's SIGTERM/Ctrl+C shutdown doesn't publish ApplicationShutdownRequested,
                    // so also stop the engine when the process exits.
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown(TimeSpan.FromSeconds(5));
                    _processExitHooked = true;
                }

                KillStaleEngine();

                _cancellation = new CancellationTokenSource();
                var token = _cancellation.Token;
                _monitor = Task.Run(() => Supervise(token), token);
            }
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            Shutdown(ShutdownTimeout);
        }

        private void Shutdown(TimeSpan timeout)
        {
            lock (_lock)
            {
                _cancellation?.Cancel();
            }

            StopEngine(timeout);
        }

        public string ResolveEnginePath()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(EnginePathVariable);

            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                if (_diskProvider.FileExists(fromEnvironment))
                {
                    return fromEnvironment;
                }

                _logger.Warn("{0} points to '{1}', which doesn't exist", EnginePathVariable, fromEnvironment);
                return null;
            }

            var fileName = OsInfo.IsWindows ? "embarr-engine.exe" : "embarr-engine";
            var nextToApp = Path.Combine(_appFolderInfo.StartUpFolder, fileName);

            return _diskProvider.FileExists(nextToApp) ? nextToApp : null;
        }

        public string GetOrCreateApiKey()
        {
            var keyFile = Path.Combine(EngineFolder, "api.key");

            if (_diskProvider.FileExists(keyFile))
            {
                var existing = _diskProvider.ReadAllText(keyFile).Trim();

                if (IsValidApiKey(existing))
                {
                    return existing;
                }

                _logger.Warn("Ignoring invalid engine API key in {0}; generating a new one", keyFile);
            }

            var key = GenerateApiKey();

            _diskProvider.EnsureFolder(EngineFolder);
            _diskProvider.WriteAllText(keyFile, key);

            return key;
        }

        public static string GenerateApiKey()
        {
            return ApiKeyPrefix + RandomNumberGenerator.GetString(ApiKeyChars, ApiKeyLength);
        }

        public static bool IsValidApiKey(string key)
        {
            return key != null &&
                   key.Length == ApiKeyPrefix.Length + ApiKeyLength &&
                   key.StartsWith(ApiKeyPrefix, StringComparison.Ordinal) &&
                   key.Skip(ApiKeyPrefix.Length).All(c => ApiKeyChars.Contains(c));
        }

        public void EnsureDownloadClient(int port, string apiKey)
        {
            var definition = _downloadClientFactory.All()
                .FirstOrDefault(d => d.Implementation == nameof(QBittorrent) && d.Name == EngineDefinitionName);

            var settings = definition?.Settings as QBittorrentSettings ?? new QBittorrentSettings();
            settings.Host = "127.0.0.1";
            settings.Port = port;
            settings.UseSsl = false;
            settings.UrlBase = string.Empty;
            settings.ApiKey = apiKey;
            settings.Username = string.Empty;
            settings.Password = string.Empty;

            if (definition == null)
            {
                _downloadClientFactory.Create(new DownloadClientDefinition
                {
                    Name = EngineDefinitionName,
                    Implementation = nameof(QBittorrent),
                    ConfigContract = nameof(QBittorrentSettings),
                    Protocol = DownloadProtocol.Torrent,
                    Enable = true,
                    Priority = 1,
                    Settings = settings
                });

                _logger.Info("Added the built-in {0} download client", EngineDefinitionName);
                return;
            }

            definition.Settings = settings;
            definition.Enable = true;
            _downloadClientFactory.Update(definition);
        }

        private async Task Supervise(CancellationToken token)
        {
            var backoff = TimeSpan.FromSeconds(1);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    Status = EngineStatus.Starting;
                    StartEngine();

                    if (await WaitUntilHealthy(token))
                    {
                        Status = EngineStatus.Running;
                        backoff = TimeSpan.FromSeconds(1);
                        _logger.Info("embarr-engine is running on 127.0.0.1:{0}", _port);

                        EnsureDownloadClient(_port, _apiKey);
                        await WatchUntilUnhealthy(token);
                    }

                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    Status = EngineStatus.Failed;
                    _logger.Error("embarr-engine stopped responding; restarting in {0}s", backoff.TotalSeconds);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Status = EngineStatus.Failed;
                    _logger.Error(ex, "embarr-engine failed; restarting in {0}s", backoff.TotalSeconds);
                }

                StopEngine();

                try
                {
                    await Task.Delay(backoff, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
            }
        }

        private void StartEngine()
        {
            _port = GetFreeLoopbackPort();
            _diskProvider.EnsureFolder(EngineFolder);

            // Started directly rather than through IProcessProvider: its environment
            // dictionary lower-cases variable names (Linux is case-sensitive) and it
            // trace-logs variable values, which would leak the API key.
            var startInfo = new ProcessStartInfo(EnginePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add($"--profile={EngineFolder}");
            startInfo.ArgumentList.Add($"--webui-port={_port}");
            startInfo.ArgumentList.Add("--confirm-legal-notice");
            startInfo.Environment[ApiKeyVariable] = _apiKey;

            var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => LogEngineOutput(e.Data, LogLevel.Debug);
            process.ErrorDataReceived += (_, e) => LogEngineOutput(e.Data, LogLevel.Warn);

            _logger.Debug("Starting embarr-engine: {0} on port {1}", EnginePath, _port);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _diskProvider.WriteAllText(PidFile, process.Id.ToString());

            lock (_lock)
            {
                _process = process;
            }
        }

        private void LogEngineOutput(string line, LogLevel level)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                _logger.Log(level, "engine: {0}", line);
            }
        }

        private void StopEngine()
        {
            StopEngine(ShutdownTimeout);
        }

        private void StopEngine(TimeSpan timeout)
        {
            Process process;

            lock (_lock)
            {
                process = _process;
                _process = null;
            }

            if (process == null || process.HasExited)
            {
                DeletePidFile();
                return;
            }

            try
            {
                _httpClient.Post(BuildRequest("/api/v2/app/shutdown"));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Engine shutdown request failed");
            }

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                _logger.Warn("embarr-engine didn't exit within {0}s; killing it", timeout.TotalSeconds);
                _processProvider.Kill(process.Id);
            }

            DeletePidFile();
        }

        // An engine left running by a previous Embarr process that didn't shut down cleanly
        // would hold the profile lock and stop the new engine from starting.
        private void KillStaleEngine()
        {
            try
            {
                if (!_diskProvider.FileExists(PidFile) ||
                    !int.TryParse(_diskProvider.ReadAllText(PidFile).Trim(), out var pid) ||
                    !_processProvider.Exists(pid))
                {
                    return;
                }

                using var stale = Process.GetProcessById(pid);

                if (stale.ProcessName == "embarr-engine")
                {
                    _logger.Warn("Stopping embarr-engine left running by a previous session (pid {0})", pid);
                    _processProvider.Kill(pid);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to check for a stale embarr-engine process");
            }
            finally
            {
                DeletePidFile();
            }
        }

        private void DeletePidFile()
        {
            try
            {
                if (_diskProvider.FileExists(PidFile))
                {
                    _diskProvider.DeleteFile(PidFile);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to delete engine pid file");
            }
        }

        private async Task<bool> WaitUntilHealthy(CancellationToken token)
        {
            var deadline = DateTime.UtcNow + StartupTimeout;

            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();

                if (ProcessExited())
                {
                    return false;
                }

                if (Ping())
                {
                    return true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
            }

            return false;
        }

        private async Task WatchUntilUnhealthy(CancellationToken token)
        {
            var failures = 0;

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(HealthInterval, token);

                if (ProcessExited())
                {
                    _logger.Error("embarr-engine exited unexpectedly");
                    return;
                }

                failures = Ping() ? 0 : failures + 1;

                if (failures >= MaxFailedHealthChecks)
                {
                    return;
                }
            }
        }

        private bool ProcessExited()
        {
            lock (_lock)
            {
                return _process == null || _process.HasExited;
            }
        }

        private bool Ping()
        {
            try
            {
                var response = _httpClient.Get(BuildRequest("/api/v2/app/version"));
                return response.StatusCode == HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                _logger.Trace(ex, "Engine health check failed");
                return false;
            }
        }

        private HttpRequest BuildRequest(string resource)
        {
            var request = new HttpRequestBuilder(false, "127.0.0.1", _port, string.Empty)
                .Resource(resource)
                .SetHeader("Authorization", $"Bearer {_apiKey}")
                .Build();

            request.LogResponseContent = false;
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(5);

            return request;
        }

        private static int GetFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
