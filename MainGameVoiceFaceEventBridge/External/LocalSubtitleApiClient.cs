using System;
using System.Net;
using System.Reflection;

namespace MainGameVoiceFaceEventBridge.External
{
    // Called on the playback thread (Unity Update), before queuing any HTTP work.
    // SubtitleApi enqueues on the existing UI queue; no Unity objects are touched here.
    internal sealed class LocalSubtitleApiClient
    {
        private MethodInfo _tryShow;
        private Type _backendType;

        internal bool TrySend(SubtitleEventClientOptions options, SubtitleEventMessage message)
        {
            if (options == null || message == null || !IsLocalTarget(options)) return false;
            if (_tryShow == null && !ResolveApi()) return false;
            try
            {
                object backend = Enum.Parse(_backendType,
                    string.IsNullOrWhiteSpace(message.Backend) ? "Auto" : message.Backend, true);
                return (bool)_tryShow.Invoke(null, new object[]
                {
                    message.Text, backend, message.DisplayMode, Math.Max(0.1f, message.HoldSeconds)
                });
            }
            catch
            {
                // An absent/older SubtitleCore or a rejected request keeps the HTTP fallback.
                return false;
            }
        }

        private bool ResolveApi()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type api = assembly.GetType("MainGameSubtitleCore.SubtitleApi", false);
                Type backend = assembly.GetType("MainGameSubtitleCore.SubtitleBackend", false);
                if (api == null || backend == null) continue;
                MethodInfo method = api.GetMethod("TryShow", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), backend, typeof(string), typeof(float) }, null);
                if (method == null) continue;
                _backendType = backend;
                _tryShow = method;
                return true;
            }
            return false;
        }

        private static bool IsLocalTarget(SubtitleEventClientOptions options)
        {
            string host = (options.Host ?? string.Empty).Trim();
            bool loopback = host.Length == 0 || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(host, out IPAddress address) && IPAddress.IsLoopback(address));
            string endpoint = (options.EndpointPath ?? string.Empty).Trim().Trim('/');
            // A custom local endpoint/port may belong to another game instance.
            return loopback && (options.Port <= 0 || options.Port == 18766)
                && (endpoint.Length == 0 || string.Equals(endpoint, "subtitle-event", StringComparison.Ordinal));
        }
    }
}
