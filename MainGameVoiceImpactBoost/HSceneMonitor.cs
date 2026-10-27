using System;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameVoiceImpactBoost
{
    internal sealed class HSceneMonitor
    {
        private readonly PluginConfig _config;
        private readonly PluginFileLogger _logger;
        private readonly Action _resetOneShotState;
        private HSceneProc _currentProc;
        private float _nextCheckAt;
        private float _nextDiagnosticAt;

        public HSceneMonitor(PluginConfig config, PluginFileLogger logger, Action resetOneShotState)
        {
            _config = config;
            _logger = logger;
            _resetOneShotState = resetOneShotState;
        }

        public void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (Time.unscaledTime - _nextCheckAt < 1f)
            {
                return;
            }

            _nextCheckAt = Time.unscaledTime;
            HSceneProc proc = UnityEngine.Object.FindObjectOfType<HSceneProc>();
            if (_config.VerboseLog.Value && Time.unscaledTime >= _nextDiagnosticAt)
            {
                _nextDiagnosticAt = Time.unscaledTime + 10f;
                _logger.LogInfo("heartbeat hscene=" + (proc != null) + " enabled=" + _config.Enabled.Value
                    + " moanMode=" + _config.MoanModeEnabled.Value + " motion=" + (proc?.flags?.nowAnimStateName ?? "<none>"));
            }
            if (proc == null && _currentProc != null)
            {
                _resetOneShotState();
                LogVerbose("hscene ended, oneshot reset");
            }
            else if (proc != null && _currentProc == null)
            {
                _resetOneShotState();
                LogVerbose("hscene started, oneshot reset");
            }

            _currentProc = proc;
        }

        private void LogVerbose(string message)
        {
            if (_config.VerboseLog.Value)
            {
                _logger.LogInfo(message);
            }
        }
    }
}
