using System;
using System.Diagnostics;
using System.Reflection;

namespace MainGameVoiceFaceEventBridge.External
{
    // Cache entry points, never the current Astral state. Used on the main thread only.
    internal sealed class AstralMethodCache : IDisposable
    {
        internal const string TypeName = "AstralMode.Plugin, AstralMode";
        private readonly Func<string, Type> _resolveType;
        private readonly Func<double> _clock;
        private MethodInfo _state, _enter, _exit;
        private double _retryAt = double.NegativeInfinity;
        private bool _disposed;

        internal AstralMethodCache(Func<string, Type> resolveType = null, Func<double> clock = null)
        {
            _resolveType = resolveType ?? (name => Type.GetType(name, false));
            _clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        }

        internal bool IsActive()
        {
            Resolve();
            return _state != null && (bool)_state.Invoke(null, null);
        }

        internal bool TryTransition(bool enter, out bool accepted)
        {
            Resolve();
            MethodInfo method = enter ? _enter : _exit;
            accepted = method != null && (bool)method.Invoke(null, null);
            return method != null;
        }

        private void Resolve()
        {
            if (_disposed || (_state != null && _enter != null && _exit != null)) return;
            double now = _clock();
            if (now < _retryAt) return;
            _retryAt = now + 5.0;
            Type type = _resolveType(TypeName);
            if (type == null) return;
            _state = Find(type, "IsAstralActive");
            _enter = Find(type, "RequestEnterAstral");
            _exit = Find(type, "RequestExitAstral");
        }

        private static MethodInfo Find(Type type, string name)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            return method != null && method.ReturnType == typeof(bool) ? method : null;
        }

        public void Dispose()
        {
            _state = _enter = _exit = null;
            _disposed = true;
        }
    }
}
