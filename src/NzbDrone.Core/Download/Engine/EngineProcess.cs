using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineProcess : IDisposable
    {
        int Id { get; }
        bool HasExited { get; }
        DateTime StartTimeUtc { get; }
        bool WaitForExit(TimeSpan timeout);

        // Requests termination without waiting; callers bound the wait with WaitForExit.
        void Kill();
    }

    public interface IEngineProcessLauncher
    {
        IEngineProcess Start(string path, IEnumerable<string> args, string apiKeyVariable, string apiKey, Action<string> onOutput, Action<string> onError);

        // Returns the running process with this pid, or null if there isn't one.
        IEngineProcess Find(int pid);
    }

    // Starts the engine directly rather than through IProcessProvider: its environment
    // dictionary lower-cases variable names (Linux is case-sensitive), it trace-logs
    // variable values (which would leak the API key) and its Kill waits without a limit.
    public class EngineProcessLauncher : IEngineProcessLauncher
    {
        public IEngineProcess Start(string path, IEnumerable<string> args, string apiKeyVariable, string apiKey, Action<string> onOutput, Action<string> onError)
        {
            var startInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            startInfo.Environment[apiKeyVariable] = apiKey;

            var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => onOutput(e.Data);
            process.ErrorDataReceived += (_, e) => onError(e.Data);

            process.Start();

            var engineProcess = new EngineProcess(process);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return engineProcess;
        }

        public IEngineProcess Find(int pid)
        {
            try
            {
                return new EngineProcess(Process.GetProcessById(pid));
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public class EngineProcess : IEngineProcess
    {
        private readonly Process _process;

        public EngineProcess(Process process)
        {
            _process = process;
        }

        public int Id => _process.Id;
        public bool HasExited => _process.HasExited;
        public DateTime StartTimeUtc => _process.StartTime.ToUniversalTime();

        public bool WaitForExit(TimeSpan timeout)
        {
            return _process.WaitForExit(timeout);
        }

        public void Kill()
        {
            try
            {
                _process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        }

        public void Dispose()
        {
            _process.Dispose();
        }
    }
}
