using System;
using MainGameVoiceFaceEventBridge;
using UnityEngine;

namespace MainGameVoiceImpactBoost
{
    internal sealed class VoiceImpactBoostService
    {
        private readonly PluginConfig _config;
        private readonly PluginFileLogger _logger;
        private float _lastFireUnscaledTime = -999f;
        private bool _oneShotFiredThisScene;

        public VoiceImpactBoostService(PluginConfig config, PluginFileLogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public void ResetOneShotState()
        {
            _oneShotFiredThisScene = false;
        }

        public void HandleBellyBokoPeak(float intensity)
        {
            try
            {
                LogVerbose("peak received intensity=" + intensity.ToString("0.000"));
                if (!_config.Enabled.Value || !CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom)
                {
                    LogVerbose("peak rejected enabled=" + _config.Enabled.Value + " blankRoom=" + CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom);
                    return;
                }

                if (intensity < _config.MinIntensity.Value)
                {
                    LogVerbose(
                        "skip (intensity=" + intensity.ToString("0.00") + " < min="
                        + _config.MinIntensity.Value.ToString("0.00") + ")");
                    return;
                }

                float now = Time.unscaledTime;
                float cooldownSec = _config.CooldownMs.Value * 0.001f;
                if (now - _lastFireUnscaledTime < cooldownSec)
                {
                    LogVerbose("skip (cooldown)");
                    return;
                }

                PluginConfig.TriggerMode mode = _config.Mode.Value;
                if (mode == PluginConfig.TriggerMode.OneShot && _oneShotFiredThisScene)
                {
                    LogVerbose("skip (oneshot already fired)");
                    return;
                }

                if (mode == PluginConfig.TriggerMode.BeatSyncGated)
                {
                    float beatSyncIntensity = BeatSyncIntensityReader.Read();
                    if (beatSyncIntensity < _config.BeatSyncMinIntensity.Value)
                    {
                        LogVerbose(
                            "skip (beatsync=" + beatSyncIntensity.ToString("0.00") + " < "
                            + _config.BeatSyncMinIntensity.Value.ToString("0.00") + ")");
                        return;
                    }
                }

                bool accepted = _config.MoanModeEnabled.Value
                    ? PublicApi.TryRequestImpactCue(Plugin.GUID)
                    : PublicApi.TryRequestTransientVolumeBoost(
                    _config.PeakMultiplier.Value,
                    _config.AttackMs.Value,
                    _config.HoldMs.Value,
                    _config.ReleaseMs.Value,
                    _config.SilenceMs.Value,
                    _config.SilenceFadeOutMs.Value,
                    _config.SilenceFadeInMs.Value,
                    _config.Easing.Value);

                if (!accepted)
                {
                    LogVerbose("impact rejected by bridge mode=" + (_config.MoanModeEnabled.Value ? "cue" : "boost") + " intensity=" + intensity.ToString("0.000"));
                    return;
                }

                _lastFireUnscaledTime = now;
                if (mode == PluginConfig.TriggerMode.OneShot)
                {
                    _oneShotFiredThisScene = true;
                }

                LogVerbose(
                    "BOOST fire intensity=" + intensity.ToString("0.000")
                    + " peak=" + _config.PeakMultiplier.Value.ToString("0.00")
                    + " env=" + _config.AttackMs.Value + "/" + _config.HoldMs.Value + "/" + _config.ReleaseMs.Value + "ms"
                    + " silence=" + _config.SilenceMs.Value + "ms"
                    + " silenceFade=" + _config.SilenceFadeOutMs.Value + "/" + _config.SilenceFadeInMs.Value + "ms"
                    + " easing=" + _config.Easing.Value);
            }
            catch (Exception ex)
            {
                _logger.LogInfo("OnBellyBokoPeak handler error: " + ex.Message);
            }
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
