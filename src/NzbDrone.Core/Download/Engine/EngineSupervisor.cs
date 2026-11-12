using System;
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
using NzbDrone.Common.Messaging;
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

    public class EngineStatusChangedEvent : IEvent
    {
        public EngineStatus Status { get; }

        public EngineStatusChangedEvent(EngineStatus status)
        {
            Status = status;
        }
    }

    public interface IEngineSupervisor
    {
        EngineStatus Status { get; }
        string EnginePath { get; }
    }

    // Starts and supervises cinnabarr-engine (the headless qBittorrent fork, roadmap §3)
    // and keeps the built-in "Cinnabarr Engine" download client pointed at it.
    public class EngineSupervisor : IEngineSupervisor,
                                    IHandle<ApplicationStartedEvent>,
                                    IHandle<ApplicationShutdownRequested>
    {
        public const string EngineDefinitionName = "Cinnabarr Engine";
        public const string EnginePathVariable = "CINNABARR_ENGINE_PATH";
        public const string ApiKeyVariable = "CINNABARR_ENGINE_API_KEY";

        private const string ApiKeyPrefix = "qbt_";
        private const int ApiKeyLength = 28;
        private const string ApiKeyChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly IEngineProcessLauncher _processLauncher;
        private readonly IHttpClient _httpClient;
        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly IEngineProfileBackup _engineProfileBackup;
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly IDownloadClientStatusService _downloadClientStatusService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        private readonly object _lock = new object();
        private bool _processExitHooked;
        private CancellationTokenSource _cancellation;
        private Task _monitor;
        private IEngineProcess _process;
        private IEngineProcess _exitedProcess;

        // Set while a pid from a previous session couldn't be verified (e.g. its start time
        // couldn't be read). Launches wait until it's resolved; that process is never killed.
        private volatile bool _staleCheckPending;
        private int _port;
        private string _apiKey;

        public EngineSupervisor(IAppFolderInfo appFolderInfo,
                                IDiskProvider diskProvider,
                                IEngineProcessLauncher processLauncher,
                                IHttpClient httpClient,
                                IEngineClientRegistry engineClientRegistry,
                                IEngineProfileBackup engineProfileBackup,
                                IDownloadClientFactory downloadClientFactory,
                                IDownloadClientStatusService downloadClientStatusService,
                                IEventAggregator eventAggregator,
                                Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _processLauncher = processLauncher;
            _httpClient = httpClient;
            _engineClientRegistry = engineClientRegistry;
            _engineProfileBackup = engineProfileBackup;
            _downloadClientFactory = downloadClientFactory;
            _downloadClientStatusService = downloadClientStatusService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public EngineStatus Status { get; private set; } = EngineStatus.NotStarted;

        public EngineTimings Timings { get; set; } = new EngineTimings();

        // Used for the restart backoff only, so tests can observe it without waiting.
        public Func<TimeSpan, CancellationToken, Task> BackoffDelay { get; set; } = Task.Delay;

        // Health check results are cached, so publish changes for EngineCheck to re-run.
        private void SetStatus(EngineStatus status)
        {
            if (Status == status)
            {
                return;
            }

            Status = status;
            _eventAggregator.PublishEvent(new EngineStatusChangedEvent(status));
        }

        public string EnginePath { get; private set; }

        private string EngineFolder => _engineClientRegistry.EngineFolder;

        private string PidFile => Path.Combine(EngineFolder, "engine.pid");

        public void Handle(ApplicationStartedEvent message)
        {
            EnginePath = ResolveEnginePath();

            if (EnginePath == null)
            {
                SetStatus(EngineStatus.NotFound);
                _logger.Warn("cinnabarr-engine was not found next to Cinnabarr and {0} is not set. Downloads are unavailable.", EnginePathVariable);
                return;
            }

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
                    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                    {
                        try
                        {
                            Shutdown(Timings.ProcessExitShutdownTimeout);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to stop cinnabarr-engine on exit");
                        }
                    };
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
            Shutdown(Timings.ShutdownTimeout);
        }

        private void Shutdown(TimeSpan timeout)
        {
            Task monitor;

            lock (_lock)
            {
                // Cancelling under the lock serialises with StartEngine: a launch either
                // completes first (and is stopped below) or sees the cancellation and is skipped.
                _cancellation?.Cancel();
                monitor = _monitor;
            }

            StopEngine(timeout);

            try
            {
                monitor?.Wait(timeout);
            }
            catch (AggregateException)
            {
                // The monitor ends with cancellation; nothing to report.
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

            var fileName = OsInfo.IsWindows ? "cinnabarr-engine.exe" : "cinnabarr-engine";
            var nextToApp = Path.Combine(_appFolderInfo.StartUpFolder, fileName);

            return _diskProvider.FileExists(nextToApp) ? nextToApp : null;
        }

        public string GetOrCreateApiKey()
        {
            var keyFile = Path.Combine(EngineFolder, "api.key");

            EnsurePrivateEngineFolder();

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

            _diskProvider.WriteAllText(keyFile, key);
            RestrictToOwner(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            return key;
        }

        // The engine folder holds the API key and the engine's own state, so on Unix it's
        // owner-only (0700) and the key file 0600, whatever the umask.
        private void EnsurePrivateEngineFolder()
        {
            _diskProvider.EnsureFolder(EngineFolder);
            RestrictToOwner(EngineFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            RestrictToOwner(Path.Combine(EngineFolder, "api.key"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        private void RestrictToOwner(string path, UnixFileMode mode)
        {
            if (OperatingSystem.IsWindows() || !(File.Exists(path) || Directory.Exists(path)))
            {
                return;
            }

            try
            {
                File.SetUnixFileMode(path, mode);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to restrict permissions on {0}", path);
            }
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
            // Tracked by the id stored next to the engine, so renaming the client in
            // settings doesn't make the next start create a second one.
            var all = _downloadClientFactory.All();
            var storedId = _engineClientRegistry.ReadStoredClientId();
            var definition = all.FirstOrDefault(d => d.Id == storedId && d.Implementation == nameof(QBittorrent))
                             ?? all.FirstOrDefault(d => d.Implementation == nameof(QBittorrent) && d.Name == EngineDefinitionName);

            var settings = definition?.Settings as QBittorrentSettings ?? new QBittorrentSettings();
            settings.Host = "127.0.0.1";
            settings.Port = port;
            settings.UseSsl = false;
            settings.UrlBase = string.Empty;
            settings.ApiKey = apiKey;
            settings.Username = string.Empty;
            settings.Password = string.Empty;

            // Every torrent in the engine is Cinnabarr's, so the built-in client doesn't route by
            // category (roadmap §4.4). With no category it polls all torrents, so imported or
            // hand-added ones are never filtered out of tracking.
            settings.TvCategory = string.Empty;
            settings.TvImportedCategory = string.Empty;

            // Held torrents are started by EngineTorrentPreparationService once the file whitelist is applied
            // (roadmap §4.8); the client holds them because it is the engine, not because of a saved setting.
            settings.InitialState = (int)QBittorrentState.Start;

            if (definition == null)
            {
                definition = _downloadClientFactory.Create(new DownloadClientDefinition
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
                _engineClientRegistry.SetClientId(definition?.Id ?? 0);
                return;
            }

            _engineClientRegistry.SetClientId(definition.Id);

            // Failures while the engine was down may have put the client into back-off;
            // clear it before the update triggers a queue refresh.
            _downloadClientStatusService.RecordSuccess(definition.Id);

            definition.Settings = settings;
            definition.Enable = true;
            _downloadClientFactory.Update(definition);
        }

        private async Task Supervise(CancellationToken token)
        {
            var backoff = Timings.InitialBackoff;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!PreviousEngineGone())
                    {
                        // Never run two engines on one profile: wait for the old one to go.
                        SetStatus(EngineStatus.Failed);
                        _logger.Error("The previous cinnabarr-engine is still running; not starting another. Retrying in {0}s", backoff.TotalSeconds);
                    }
                    else
                    {
                        // Stay Failed while retrying; only the first attempt reports Starting.
                        if (Status != EngineStatus.Failed)
                        {
                            SetStatus(EngineStatus.Starting);
                        }

                        _apiKey ??= GetOrCreateApiKey();

                        if (!StartEngine(token))
                        {
                            break;
                        }

                        if (await WaitUntilHealthy(token))
                        {
                            SetStatus(EngineStatus.Running);
                            _logger.Info("cinnabarr-engine is running on 127.0.0.1:{0}", _port);

                            EnsureDownloadClient(_port, _apiKey);

                            var runningSince = DateTime.UtcNow;
                            await WatchUntilUnhealthy(token);

                            // Only a run that stayed up for a while resets the backoff, so an engine
                            // that keeps crashing soon after starting still backs off.
                            if (DateTime.UtcNow - runningSince >= Timings.StableUptime)
                            {
                                backoff = Timings.InitialBackoff;
                            }
                        }

                        if (token.IsCancellationRequested)
                        {
                            break;
                        }

                        SetStatus(EngineStatus.Failed);
                        _logger.Error("cinnabarr-engine stopped responding; restarting in {0}s", backoff.TotalSeconds);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SetStatus(EngineStatus.Failed);
                    _logger.Error(ex, "cinnabarr-engine failed; restarting in {0}s", backoff.TotalSeconds);
                }

                try
                {
                    StopEngine();
                }
                catch (Exception ex)
                {
                    // Never let cleanup end supervision, and still back off below.
                    _logger.Error(ex, "Error while stopping cinnabarr-engine; will retry");
                }

                try
                {
                    await BackoffDelay(backoff, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error while waiting to restart cinnabarr-engine");
                }

                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, Timings.MaxBackoff.Ticks));
            }

            try
            {
                StopEngine();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while stopping cinnabarr-engine");
            }
        }

        private bool PreviousEngineGone()
        {
            if (_staleCheckPending)
            {
                KillStaleEngine();

                if (_staleCheckPending)
                {
                    return false;
                }
            }

            if (ProcessExited())
            {
                return true;
            }

            StopEngine();

            return ProcessExited();
        }

        private bool StartEngine(CancellationToken token)
        {
            // A profile restored from a backup replaces the live one here, while the engine is not running.
            _engineProfileBackup.ApplyStagedRestore();

            _port = GetFreeLoopbackPort();
            EnsurePrivateEngineFolder();

            var args = new[]
            {
                $"--profile={EngineFolder}",
                $"--webui-port={_port}",
                "--confirm-legal-notice"
            };

            IEngineProcess process;

            lock (_lock)
            {
                // Checked under the same lock Shutdown cancels under, so a shutdown can't
                // slip in between the check and the launch.
                if (token.IsCancellationRequested)
                {
                    return false;
                }

                _logger.Debug("Starting cinnabarr-engine: {0} on port {1}", EnginePath, _port);

                process = _processLauncher.Start(EnginePath,
                    args,
                    ApiKeyVariable,
                    _apiKey,
                    line => LogEngineOutput(line, LogLevel.Debug),
                    line => LogEngineOutput(line, LogLevel.Warn));

                // Own the process before anything else can throw, so failure paths stop it.
                // The previous child has exited (PreviousEngineGone) and no stop can still be
                // using it: shutdown cancels before stopping, and a cancelled token never
                // reaches this point. So it's safe to dispose it here, and only here.
                _exitedProcess?.Dispose();
                _exitedProcess = null;
                _process = process;
            }

            try
            {
                _diskProvider.WriteAllText(PidFile, FormatPidFile(process.Id, process.StartTimeUtc));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to write engine pid file");
            }

            return true;
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
            StopEngine(Timings.ShutdownTimeout);
        }

        private void StopEngine(TimeSpan timeout)
        {
            IEngineProcess process;

            lock (_lock)
            {
                // Keep _process set until the child has really exited, so a concurrent stop
                // (e.g. ProcessExit while the watchdog is stopping an unresponsive engine)
                // still sees it and can force it down within its own timeout.
                process = _process;
            }

            if (process == null)
            {
                return;
            }

            if (!HasExited(process))
            {
                try
                {
                    _httpClient.Post(BuildRequest("/api/v2/app/shutdown"));
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Engine shutdown request failed");
                }

                if (!process.WaitForExit(timeout))
                {
                    _logger.Warn("cinnabarr-engine didn't exit within {0}s; killing it", timeout.TotalSeconds);
                    process.Kill();
                }

                // Bounded: a process stuck in uninterruptible I/O must not hang shutdown.
                if (!process.WaitForExit(Timings.KillTimeout))
                {
                    // Leave the pid file so the next start can stop it.
                    _logger.Error("cinnabarr-engine (pid {0}) is still running after being killed", process.Id);
                    return;
                }
            }

            lock (_lock)
            {
                if (ReferenceEquals(_process, process))
                {
                    // Not disposed here: another stop (e.g. shutdown racing the watchdog)
                    // may still be using it. It's disposed when the next engine replaces it.
                    _process = null;
                    _exitedProcess = process;
                }
            }

            DeletePidFile();
        }

        private static bool HasExited(IEngineProcess process)
        {
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        public static string FormatPidFile(int pid, DateTime startTimeUtc)
        {
            return $"{pid}\n{startTimeUtc.Ticks}\n";
        }

        // The pid file stores the pid and the process start time, so a reused pid
        // (a different process that happens to get the same number) is never killed.
        public static bool IsSameProcess(string pidFileContents, int pid, DateTime startTimeUtc)
        {
            var lines = (pidFileContents ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return lines.Length >= 2 &&
                   int.TryParse(lines[0], out var storedPid) &&
                   long.TryParse(lines[1], out var storedTicks) &&
                   storedPid == pid &&
                   Math.Abs(startTimeUtc.Ticks - storedTicks) < TimeSpan.TicksPerSecond;
        }

        // An engine left running by a previous Cinnabarr process that didn't shut down cleanly
        // would hold the profile lock and stop the new engine from starting.
        private void KillStaleEngine()
        {
            IEngineProcess stale = null;
            var verified = false;

            _staleCheckPending = false;

            try
            {
                if (!_diskProvider.FileExists(PidFile))
                {
                    return;
                }

                var contents = _diskProvider.ReadAllText(PidFile);
                var firstLine = contents.Split('\n')[0].Trim();

                if (!int.TryParse(firstLine, out var pid))
                {
                    DeletePidFile();
                    return;
                }

                stale = _processLauncher.Find(pid);

                if (stale == null)
                {
                    DeletePidFile();
                    return;
                }

                if (!IsSameProcess(contents, pid, stale.StartTimeUtc))
                {
                    _logger.Debug("Pid {0} from the engine pid file now belongs to another process; leaving it alone", pid);
                    stale.Dispose();
                    stale = null;
                    DeletePidFile();
                    return;
                }

                verified = true;

                _logger.Warn("Stopping cinnabarr-engine left running by a previous session (pid {0})", pid);
                stale.Kill();

                if (stale.WaitForExit(Timings.KillTimeout))
                {
                    stale.Dispose();
                    stale = null;
                    DeletePidFile();
                    return;
                }

                _logger.Error("cinnabarr-engine from a previous session (pid {0}) didn't exit; not starting another until it does", pid);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to check or stop cinnabarr-engine left running by a previous session");
            }

            if (stale == null)
            {
                return;
            }

            if (verified)
            {
                // Confirmed to be our engine but it survived the kill: keep the pid file and
                // track it as the current child, so supervision keeps trying to stop it and
                // never launches a second engine on the locked profile.
                lock (_lock)
                {
                    _process = stale;
                }

                return;
            }

            // Couldn't verify it's ours: never kill it, keep the pid file, and hold off
            // launching until a later check resolves it.
            stale.Dispose();
            _staleCheckPending = true;
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
            var deadline = DateTime.UtcNow + Timings.StartupTimeout;

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

                await Task.Delay(Timings.StartupPollInterval, token);
            }

            return false;
        }

        private async Task WatchUntilUnhealthy(CancellationToken token)
        {
            var failures = 0;

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(Timings.HealthInterval, token);

                if (ProcessExited())
                {
                    _logger.Error("cinnabarr-engine exited unexpectedly");
                    return;
                }

                failures = Ping() ? 0 : failures + 1;

                if (failures >= Timings.MaxFailedHealthChecks)
                {
                    return;
                }
            }
        }

        private bool ProcessExited()
        {
            lock (_lock)
            {
                return _process == null || HasExited(_process);
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
