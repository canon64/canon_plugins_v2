using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace SimpleAfterimage
{
    public sealed partial class Plugin
    {
        private bool _runtimeLoggingEnabled = false;
        private void LogPlugin(string level, string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (string.IsNullOrEmpty(_pluginLogPath)) return;
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                File.AppendAllText(_pluginLogPath, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        [System.Diagnostics.Conditional("SIMPLEAFTERIMAGE_DIAGNOSTICS")]
        private void LogGlowDecision(string reason, bool force = false)
        {
            bool verbose = _cfgVerboseLog != null && _cfgVerboseLog.Value;

            if (force || !string.Equals(_lastGlowDecision, reason, StringComparison.Ordinal))
            {
                _lastGlowDecision = reason;
                _nextGlowHeartbeatTime = Time.unscaledTime + 5f;
                LogPlugin("GLOW", reason);
                return;
            }

            if (verbose && Time.unscaledTime >= _nextGlowHeartbeatTime)
            {
                _nextGlowHeartbeatTime = Time.unscaledTime + 5f;
                LogPlugin("GLOW", "heartbeat: " + reason);
            }
        }
    }
}
