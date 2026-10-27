using BepInEx.Logging;
using System;
using System.IO;
using System.Text;

namespace MainGameVrHipIkVoiceImpactBoost
{
    internal sealed class PluginFileLogger
    {
        private bool _enabled = false;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly ManualLogSource _logger;
        private readonly string _path;

        internal PluginFileLogger(ManualLogSource logger, string pluginDir, string fileName)
        {
            _logger = logger;
            if (!_enabled) return;
            Directory.CreateDirectory(pluginDir);
            _path = Path.Combine(pluginDir, fileName);
            File.WriteAllText(
                _path,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [INFO] log start" + Environment.NewLine,
                Utf8NoBom);
        }

        internal void LogInfo(string message)
        {
            if (!_enabled) return;
            _logger.LogInfo(message);
            Append("[INFO] " + message);
        }

        internal void LogWarning(string message)
        {
            if (!_enabled) return;
            _logger.LogWarning(message);
            Append("[WARN] " + message);
        }

        internal void LogError(string message)
        {
            if (!_enabled) return;
            _logger.LogError(message);
            Append("[ERROR] " + message);
        }

        private void Append(string line)
        {
            try
            {
                File.AppendAllText(
                    _path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + line + Environment.NewLine,
                    Utf8NoBom);
            }
            catch
            {
            }
        }
    }
}
