using System;
using System.IO;
using BepInEx.Logging;

namespace MainGameVoiceImpactBoost
{
    internal sealed class PluginFileLogger
    {
        private readonly bool _enabled;
        private readonly ManualLogSource _logger;
        private readonly string _logFilePath;

        public PluginFileLogger(ManualLogSource logger, string pluginLocation, string fileName, bool enabled)
        {
            _enabled = enabled;
            _logger = logger;
            string pluginDir = Path.GetDirectoryName(pluginLocation) ?? Directory.GetCurrentDirectory();
            _logFilePath = Path.Combine(pluginDir, fileName);
            if (_enabled) { try { File.WriteAllText(_logFilePath, string.Empty); } catch { } }
        }

        public void LogInfo(string message)
        {
            if (!_enabled) return;
            _logger.LogInfo(message);
            Append("[INFO] " + message);
        }

        private void Append(string line)
        {
            try
            {
                File.AppendAllText(
                    _logFilePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + line + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
