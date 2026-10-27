using System;
using System.IO;
using System.Text;
using BepInEx.Logging;
using MainGameLogRelay;

namespace StudioFacePresetTool.Services
{
    internal sealed class PluginLogger
    {
        private readonly ManualLogSource _logger;
        private readonly string _logPath;
        private readonly string _relayOwner;
        private readonly string _relayLogKey;
        private readonly object _sync = new object();
        private readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(false);

        private bool _detailEnabled;

        internal PluginLogger(ManualLogSource logger, string logPath, string relayOwner, string relayLogKey, bool detailEnabled)
        {
            _logger = logger;
            _logPath = logPath;
            _relayOwner = relayOwner;
            _relayLogKey = relayLogKey;
            _detailEnabled = detailEnabled;

            ResetLogFile();
            Info("logger initialized");
        }

        internal bool DetailEnabled
        {
            get => _detailEnabled;
            set
            {
                _detailEnabled = value;
                Info("detail log changed=" + _detailEnabled);
            }
        }

        internal void ConfigureRelay(bool enabled)
        {
            if (!LogRelayApi.IsAvailable)
            {
                Warn("relay unavailable; fallback to local logger only");
                return;
            }

            LogRelayApi.SetOwnerLogKey(_relayOwner, _relayLogKey);
            LogRelayApi.SetOwnerEnabled(_relayOwner, enabled);
            Info("relay configured enabled=" + enabled);
        }

        internal void Entry(string methodName)
        {
            Debug("ENTER " + methodName);
        }

        internal void Branch(string label, bool taken, string detail)
        {
            Debug("BRANCH " + label + " taken=" + taken + " detail=" + detail);
        }

        internal void Debug(string message)
        {
            if (!_detailEnabled)
                return;

            Emit(LogLevel.Debug, message);
        }

        internal void Info(string message)
        {
            Emit(LogLevel.Info, message);
        }

        internal void Warn(string message)
        {
            Emit(LogLevel.Warning, message);
        }

        internal void Error(string message)
        {
            Emit(LogLevel.Error, message);
        }

        internal void Error(string message, Exception ex)
        {
            Emit(LogLevel.Error, message + " ex=" + (ex == null ? "null" : ex.ToString()));
        }

        private void Emit(LogLevel level, string message)
        {
            string text = "[StudioFacePresetTool] " + message;
            switch (level)
            {
                case LogLevel.Debug:
                    _logger.LogDebug(text);
                    RelayDebug(text);
                    break;
                case LogLevel.Info:
                    _logger.LogInfo(text);
                    RelayInfo(text);
                    break;
                case LogLevel.Warning:
                    _logger.LogWarning(text);
                    RelayWarn(text);
                    break;
                case LogLevel.Error:
                    _logger.LogError(text);
                    RelayError(text);
                    break;
                default:
                    _logger.LogInfo(text);
                    RelayInfo(text);
                    break;
            }

            WriteFileLine(level, message);
        }

        private void RelayDebug(string message)
        {
            if (LogRelayApi.IsAvailable)
                LogRelayApi.Debug(_relayOwner, message);
        }

        private void RelayInfo(string message)
        {
            if (LogRelayApi.IsAvailable)
                LogRelayApi.Info(_relayOwner, message);
        }

        private void RelayWarn(string message)
        {
            if (LogRelayApi.IsAvailable)
                LogRelayApi.Warn(_relayOwner, message);
        }

        private void RelayError(string message)
        {
            if (LogRelayApi.IsAvailable)
                LogRelayApi.Error(_relayOwner, message);
        }

        private void ResetLogFile()
        {
            try
            {
                string dir = Path.GetDirectoryName(_logPath) ?? string.Empty;
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(_logPath, string.Empty, _utf8NoBom);
                WriteFileLine(LogLevel.Info, "log file reset");
            }
            catch
            {
                // no-op
            }
        }

        private void WriteFileLine(LogLevel level, string message)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "]"
                + "[" + level.ToString().ToUpperInvariant() + "] "
                + message
                + Environment.NewLine;

            lock (_sync)
            {
                try
                {
                    File.AppendAllText(_logPath, line, _utf8NoBom);
                }
                catch
                {
                    // no-op
                }
            }
        }
    }
}
