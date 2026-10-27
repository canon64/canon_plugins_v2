using System;
using System.Collections.Concurrent;
using System.Threading;

namespace MainGameBlankMapAdd.Application
{
    internal sealed class MainThreadDispatcher : IDisposable
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private readonly int _mainThreadId;
        private volatile bool _accepting = true;

        internal MainThreadDispatcher()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        internal bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        internal bool Enqueue(Action action)
        {
            if (action == null || !_accepting) return false;
            _queue.Enqueue(action);
            return true;
        }

        internal int Drain(Action<Exception> onError)
        {
            if (!IsMainThread)
                throw new InvalidOperationException("MainThreadDispatcher.Drain must run on the owning thread.");

            int count = 0;
            while (_queue.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception ex) { onError?.Invoke(ex); }
                count++;
            }
            return count;
        }

        internal bool TryInvoke<T>(Func<T> function, int timeoutMs, out T value, out string error)
        {
            value = default(T);
            error = string.Empty;
            if (function == null)
            {
                error = "function is null";
                return false;
            }

            if (IsMainThread)
            {
                try
                {
                    value = function();
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            T captured = default(T);
            Exception capturedError = null;
            using (var completed = new ManualResetEventSlim(false))
            {
                if (!Enqueue(() =>
                {
                    try { captured = function(); }
                    catch (Exception ex) { capturedError = ex; }
                    finally { completed.Set(); }
                }))
                {
                    error = "dispatcher is stopping";
                    return false;
                }

                if (!completed.Wait(Math.Max(1, timeoutMs)))
                {
                    error = "main-thread timeout";
                    return false;
                }
            }

            if (capturedError != null)
            {
                error = capturedError.Message;
                return false;
            }

            value = captured;
            return true;
        }

        public void Dispose()
        {
            _accepting = false;
            while (_queue.TryDequeue(out _)) { }
        }
    }
}
