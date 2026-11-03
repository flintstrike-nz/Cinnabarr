using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineSupervisorLifecycleFixture : CoreTest<EngineSupervisor>
    {
        private FakeLauncher _launcher;
        private ConcurrentQueue<TimeSpan> _backoffs;
        private volatile bool _healthy;

        [SetUp]
        public void Setup()
        {
            _launcher = new FakeLauncher();
            _backoffs = new ConcurrentQueue<TimeSpan>();
            _healthy = true;

            Mocker.SetConstant<IEngineProcessLauncher>(_launcher);

            Mocker.GetMock<IAppFolderInfo>().SetupGet(s => s.AppDataFolder).Returns(@"C:\Test\AppData".AsOsAgnostic());
            Mocker.GetMock<IAppFolderInfo>().SetupGet(s => s.StartUpFolder).Returns(@"C:\Test\App".AsOsAgnostic());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(It.Is<string>(p => Path.GetFileNameWithoutExtension(p) == "embarr-engine")))
                .Returns(true);

            Mocker.GetMock<IDownloadClientFactory>()
                .Setup(s => s.All())
                .Returns(new List<DownloadClientDefinition>());

            Mocker.GetMock<IDownloadClientFactory>()
                .Setup(s => s.Create(It.IsAny<DownloadClientDefinition>()))
                .Returns<DownloadClientDefinition>(d =>
                {
                    d.Id = 1;
                    return d;
                });

            Mocker.GetMock<IHttpClient>()
                .Setup(s => s.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), "v5", _healthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable));

            // app/shutdown: a cooperative engine exits when asked.
            Mocker.GetMock<IHttpClient>()
                .Setup(s => s.Post(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r =>
                {
                    _launcher.Latest?.RequestShutdown();
                    return new HttpResponse(r, new HttpHeader(), string.Empty);
                });

            Subject.Timings = new EngineTimings
            {
                StartupTimeout = TimeSpan.FromMilliseconds(200),
                StartupPollInterval = TimeSpan.FromMilliseconds(10),
                HealthInterval = TimeSpan.FromMilliseconds(20),
                MaxFailedHealthChecks = 3,
                ShutdownTimeout = TimeSpan.FromMilliseconds(100),
                ProcessExitShutdownTimeout = TimeSpan.FromMilliseconds(100),
                KillTimeout = TimeSpan.FromMilliseconds(50),
                InitialBackoff = TimeSpan.FromSeconds(1),
                MaxBackoff = TimeSpan.FromSeconds(4),
                StableUptime = TimeSpan.FromMinutes(10)
            };

            // Record the requested backoff but don't actually wait for it.
            Subject.BackoffDelay = (delay, token) =>
            {
                _backoffs.Enqueue(delay);
                return Task.Delay(5, token);
            };
        }

        [TearDown]
        public void TearDown()
        {
            Subject.Handle(new ApplicationShutdownRequested());

            // These tests drive failures on purpose; the supervisor's warnings and errors
            // about them are expected. Behaviour is asserted explicitly in each test.
            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }

        private static void WaitFor(Func<bool> condition, string because)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);

            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail($"Timed out waiting for {because}");
                }

                Thread.Sleep(10);
            }
        }

        [Test]
        public void should_start_engine_report_running_and_add_download_client()
        {
            var created = 0;

            Mocker.GetMock<IDownloadClientFactory>()
                .Setup(s => s.Create(It.IsAny<DownloadClientDefinition>()))
                .Callback(() => Interlocked.Increment(ref created))
                .Returns<DownloadClientDefinition>(d =>
                {
                    d.Id = 1;
                    return d;
                });

            Subject.Handle(new ApplicationStartedEvent());

            // Running is set just before the client is created, so wait for the creation itself.
            WaitFor(() => Volatile.Read(ref created) > 0, "the download client to be created");

            Subject.Status.Should().Be(EngineStatus.Running);
            _launcher.Started.Should().HaveCount(1);
            _launcher.Started[0].ApiKey.Should().StartWith("qbt_");
            Mocker.GetMock<IDownloadClientFactory>().Verify(v => v.Create(It.IsAny<DownloadClientDefinition>()), Times.Once());
        }

        [Test]
        public void should_stop_engine_on_shutdown()
        {
            Subject.Handle(new ApplicationStartedEvent());
            WaitFor(() => Subject.Status == EngineStatus.Running, "the engine to be running");

            Subject.Handle(new ApplicationShutdownRequested());

            _launcher.Started.Should().OnlyContain(p => p.HasExited);
        }

        [Test]
        public void should_restart_with_growing_backoff_when_startup_times_out()
        {
            _healthy = false;

            Subject.Handle(new ApplicationStartedEvent());

            WaitFor(() => _backoffs.Count >= 4, "four backoffs");

            Subject.Status.Should().Be(EngineStatus.Failed);
            _backoffs.Take(4).Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
            _launcher.Started.Take(_launcher.Started.Count - 1).Should().OnlyContain(p => p.HasExited);
        }

        [Test]
        public void should_restart_after_consecutive_failed_health_checks()
        {
            Subject.Handle(new ApplicationStartedEvent());
            WaitFor(() => Subject.Status == EngineStatus.Running, "the engine to be running");

            _healthy = false;

            WaitFor(() => _launcher.Started.Count >= 2, "a second launch");

            _launcher.Started[0].HasExited.Should().BeTrue();
        }

        [Test]
        public void should_restart_when_engine_exits()
        {
            Subject.Handle(new ApplicationStartedEvent());
            WaitFor(() => Subject.Status == EngineStatus.Running, "the engine to be running");

            _launcher.Started[0].Crash();

            WaitFor(() => _launcher.Started.Count >= 2, "a second launch");
            WaitFor(() => Subject.Status == EngineStatus.Running, "the engine to be running again");
        }

        [Test]
        public void should_stop_child_and_not_relaunch_when_shut_down_during_startup()
        {
            _healthy = false;

            Subject.Handle(new ApplicationStartedEvent());
            WaitFor(() => _launcher.Started.Count >= 1, "the first launch");

            Subject.Handle(new ApplicationShutdownRequested());
            var launches = _launcher.Started.Count;

            Thread.Sleep(300);

            _launcher.Started.Should().HaveCount(launches);
            _launcher.Started.Should().OnlyContain(p => p.HasExited);
        }

        [Test]
        public void should_not_launch_another_engine_while_the_previous_one_wont_die()
        {
            _healthy = false;
            _launcher.NextIsStuck = true;

            Subject.Handle(new ApplicationStartedEvent());

            WaitFor(() => _backoffs.Count >= 3, "several retries");

            _launcher.Started.Should().HaveCount(1);
            _launcher.Started[0].HasExited.Should().BeFalse();
            Subject.Status.Should().Be(EngineStatus.Failed);

            _launcher.Started[0].Unstick();
        }

        [Test]
        public void should_not_launch_while_a_stale_engine_from_a_previous_session_wont_die()
        {
            var stale = new FakeProcess(4242, "qbt_old", stuck: true);
            _launcher.Stale = stale;

            var pidFile = Path.Combine(@"C:\Test\AppData".AsOsAgnostic(), "engine", "engine.pid");
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(pidFile)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.ReadAllText(pidFile)).Returns(EngineSupervisor.FormatPidFile(stale.Id, stale.StartTimeUtc));

            Subject.Handle(new ApplicationStartedEvent());

            WaitFor(() => _backoffs.Count >= 2, "several retries");

            _launcher.Started.Should().BeEmpty();
            Subject.Status.Should().Be(EngineStatus.Failed);
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(pidFile), Times.Never());

            // Once the old engine finally exits, the new one starts.
            stale.Unstick();
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(pidFile)).Returns(false);

            WaitFor(() => Subject.Status == EngineStatus.Running, "the new engine to be running");
            _launcher.Started.Should().HaveCount(1);
        }

        private class FakeLauncher : IEngineProcessLauncher
        {
            private readonly object _lock = new object();
            private readonly List<FakeProcess> _started = new List<FakeProcess>();
            private int _nextPid = 1000;

            public bool NextIsStuck { get; set; }

            public FakeProcess Stale { get; set; }

            public List<FakeProcess> Started
            {
                get
                {
                    lock (_lock)
                    {
                        return _started.ToList();
                    }
                }
            }

            public FakeProcess Latest
            {
                get
                {
                    lock (_lock)
                    {
                        return _started.LastOrDefault();
                    }
                }
            }

            public IEngineProcess Start(string path, IEnumerable<string> args, string apiKeyVariable, string apiKey, Action<string> onOutput, Action<string> onError)
            {
                lock (_lock)
                {
                    var process = new FakeProcess(_nextPid++, apiKey, NextIsStuck);
                    NextIsStuck = false;
                    _started.Add(process);
                    return process;
                }
            }

            public IEngineProcess Find(int pid)
            {
                return Stale != null && Stale.Id == pid ? Stale : null;
            }
        }

        private class FakeProcess : IEngineProcess
        {
            private readonly ManualResetEventSlim _exited = new ManualResetEventSlim(false);
            private volatile bool _stuck;

            public FakeProcess(int id, string apiKey, bool stuck)
            {
                Id = id;
                ApiKey = apiKey;
                _stuck = stuck;
            }

            public int Id { get; }
            public string ApiKey { get; }
            public DateTime StartTimeUtc { get; } = DateTime.UtcNow;
            public bool HasExited => _exited.IsSet;

            public bool WaitForExit(TimeSpan timeout)
            {
                return _exited.Wait(timeout);
            }

            public void Kill()
            {
                if (!_stuck)
                {
                    _exited.Set();
                }
            }

            public void RequestShutdown()
            {
                Kill();
            }

            public void Crash()
            {
                _exited.Set();
            }

            public void Unstick()
            {
                _stuck = false;
                _exited.Set();
            }

            public void Dispose()
            {
            }
        }
    }
}
