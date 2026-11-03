using System;

namespace NzbDrone.Core.Download.Engine
{
    // Supervision timings. Defaults are for production; tests shorten them.
    public class EngineTimings
    {
        public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan StartupPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
        public TimeSpan HealthInterval { get; set; } = TimeSpan.FromSeconds(10);
        public int MaxFailedHealthChecks { get; set; } = 3;
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public TimeSpan ProcessExitShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan KillTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan StableUptime { get; set; } = TimeSpan.FromSeconds(60);
    }
}
