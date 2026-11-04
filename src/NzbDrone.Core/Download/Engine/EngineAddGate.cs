using System;
using System.Threading;

namespace NzbDrone.Core.Download.Engine
{
    // Torrent adds and orphan deletion take turns (roadmap §4.13). A torrent added (or re-added) to the engine can own
    // partial files that are already on disk, with old timestamps, and the engine only lists it once it has loaded it:
    // deletion therefore waits for any add in progress, and doesn't run at all until the last add has had time to show.
    public interface IEngineAddGate
    {
        // Held while a torrent is sent to the engine.
        IDisposable BeginAdd();

        // Held while orphans are re-checked and deleted; null (and nothing held) while a recent add may not be listed
        // by the engine yet.
        IDisposable TryBeginDelete();
    }

    public class EngineAddGate : IEngineAddGate
    {
        public static readonly TimeSpan AddSettleTime = TimeSpan.FromMinutes(5);

        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private DateTime _lastAdd = DateTime.MinValue;

        public IDisposable BeginAdd()
        {
            _semaphore.Wait();

            return new Releaser(() =>
            {
                _lastAdd = DateTime.UtcNow;
                _semaphore.Release();
            });
        }

        public IDisposable TryBeginDelete()
        {
            _semaphore.Wait();

            if (DateTime.UtcNow - _lastAdd < AddSettleTime)
            {
                _semaphore.Release();

                return null;
            }

            return new Releaser(() => _semaphore.Release());
        }

        private sealed class Releaser : IDisposable
        {
            private Action _release;

            public Releaser(Action release)
            {
                _release = release;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _release, null)?.Invoke();
            }
        }
    }
}
