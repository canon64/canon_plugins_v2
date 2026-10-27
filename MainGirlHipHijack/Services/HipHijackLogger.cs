using System;
using System.IO;
using BepInEx.Logging;
using MainGameLogRelay;

namespace MainGirlHipHijack.Services
{
    internal sealed class HipHijackLogger
    {
        private readonly string _pluginName;
        private readonly string _relayOwner;
        private readonly string _relayLogKey;
        private readonly string _inputOwner;
        private readonly string _inputLogKey;
        private readonly ManualLogSource _fallback;
        private readonly Func<bool> _isFileLogEnabled;
        private readonly Func<bool> _isVerboseEnabled;
        private readonly SimpleFileLogger _file = new SimpleFileLogger();

        internal HipHijackLogger(
            string pluginDirectory,
            string pluginName,
            string relayOwner,
            string relayLogKey,
            string inputOwner,
            string inputLogKey,
            ManualLogSource fallback,
            Func<bool> isFileLogEnabled,
            Func<bool> isVerboseEnabled)
        {
            _pluginName = pluginName;
            _relayOwner = relayOwner;
            _relayLogKey = relayLogKey;
            _inputOwner = inputOwner;
            _inputLogKey = inputLogKey;
            _fallback = fallback;
            _isFileLogEnabled = isFileLogEnabled;
            _isVerboseEnabled = isVerboseEnabled;
            _file.Initialize(Path.Combine(pluginDirectory ?? string.Empty, "MainGirlHipHijack.log"), true);
        }

        internal void PreconfigureRelayRouting()
        {
            if (!LogRelayApi.IsAvailable) return;
            LogRelayApi.SetOwnerLogKey(_relayOwner, _relayLogKey);
            LogRelayApi.SetOwnerLogKey(_inputOwner, _inputLogKey);
        }

        internal void ApplyRelayState(bool enabled)
        {
            if (!LogRelayApi.IsAvailable) return;
            LogRelayApi.SetOwnerLogKey(_relayOwner, _relayLogKey);
            LogRelayApi.SetOwnerEnabled(_relayOwner, enabled);
            LogRelayApi.SetOwnerLogKey(_inputOwner, _inputLogKey);
            LogRelayApi.SetOwnerEnabled(_inputOwner, enabled);
        }

        internal void Info(string message)
        {
            if (_isFileLogEnabled()) _file.Write("INFO", message);
            if (LogRelayApi.IsAvailable) LogRelayApi.Info(_relayOwner, message);
            else _fallback.LogInfo("[" + _pluginName + "] " + message);
        }

        internal void Warn(string message)
        {
            if (_isFileLogEnabled()) _file.Write("WARN", message);
            if (LogRelayApi.IsAvailable) LogRelayApi.Warn(_relayOwner, message);
            else _fallback.LogWarning("[" + _pluginName + "] " + message);
        }

        internal void Error(string message)
        {
            if (_isFileLogEnabled()) _file.Write("ERROR", message);
            if (LogRelayApi.IsAvailable) LogRelayApi.Error(_relayOwner, message);
            else _fallback.LogError("[" + _pluginName + "] " + message);
        }

        internal void Debug(string message)
        {
            if (!_isVerboseEnabled()) return;
            if (LogRelayApi.IsAvailable) LogRelayApi.Debug(_relayOwner, message);
            else _fallback.LogInfo("[" + _pluginName + "] " + message);
        }

        internal void DiagnosticInfo(string message)
        {
            if (!_isFileLogEnabled()) _file.Write("INFO", message);
            Info(message);
        }

        internal void DiagnosticWarning(string message)
        {
            if (!_isFileLogEnabled()) _file.Write("WARN", message);
            Warn(message);
        }
    }
}
