using System;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace MainGameVrScreenEffectProbe
{
    internal sealed class ProbeFileLogger
    {
        private bool _runtimeLoggingEnabled = false;
        internal bool RuntimeEnabled => _runtimeLoggingEnabled;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _sync = new object();
        private readonly ManualLogSource _bepInLogger;
        private readonly string _path;

        internal ProbeFileLogger(string path, ManualLogSource bepInLogger)
        {
            _path = path;
            _bepInLogger = bepInLogger;
            if (!_runtimeLoggingEnabled) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? string.Empty);
                File.WriteAllText(_path, string.Empty, Utf8NoBom);
            }
            catch (Exception ex)
            {
                _bepInLogger?.LogWarning("log reset failed: " + ex.Message);
            }
        }

        internal string Path => _path;

        internal void Write(bool enabled, ProbeLogLevel threshold, ProbeLogLevel level, string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (!enabled || level > threshold)
                return;

            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                + " [" + level.ToString().ToUpperInvariant() + "] "
                + message
                + Environment.NewLine;
            try
            {
                lock (_sync)
                {
                    File.AppendAllText(_path, line, Utf8NoBom);
                }
            }
            catch (Exception ex)
            {
                _bepInLogger?.LogWarning("file log write failed: " + ex.Message);
            }

            if (level <= ProbeLogLevel.Warning)
            {
                if (level == ProbeLogLevel.Error)
                    _bepInLogger?.LogError(message);
                else
                    _bepInLogger?.LogWarning(message);
            }
        }
    }
}
