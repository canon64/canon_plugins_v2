using System;
using System.Threading.Tasks;

namespace MainGameBlankMapAdd.Media.Images
{
    // Called only by the main thread. Work delegates must use plain data / IO only.
    // One worker at a time, one pending request: slow network IO cannot grow a queue.
    internal sealed class SlideshowBackgroundJob<T>
    {
        private sealed class Outcome
        {
            internal T Value;
            internal Exception Error;
        }

        private Task<Outcome> _running;
        private Func<T> _pending;
        private long _version, _runningVersion;
        internal bool HasWork => _running != null || _pending != null;

        internal void Request(Func<T> work)
        {
            _version++;
            _pending = work ?? throw new ArgumentNullException(nameof(work));
            StartPending();
        }

        internal void Reset()
        {
            _version++;
            _pending = null;
            // Never wait for a network call. Its result will be discarded by TryTake.
        }

        internal bool TryTake(out T value, out Exception error)
        {
            value = default;
            error = null;
            if (_running == null || !_running.IsCompleted) return false;
            Outcome outcome = _running.GetAwaiter().GetResult();
            bool current = _runningVersion == _version;
            _running = null;
            StartPending();
            if (!current) return false;
            value = outcome.Value;
            error = outcome.Error;
            return true;
        }

        private void StartPending()
        {
            if (_running != null || _pending == null) return;
            Func<T> work = _pending;
            _pending = null;
            _runningVersion = _version;
            _running = Task.Run(() =>
            {
                try { return new Outcome { Value = work() }; }
                catch (Exception ex) { return new Outcome { Error = ex }; }
            });
        }
    }
}
