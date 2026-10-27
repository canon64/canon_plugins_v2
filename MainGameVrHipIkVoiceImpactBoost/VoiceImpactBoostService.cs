using MainGameVoiceFaceEventBridge;
using UnityEngine;

namespace MainGameVrHipIkVoiceImpactBoost
{
    internal sealed class VoiceImpactBoostService
    {
        private readonly PluginSettings _settings;
        private readonly PluginFileLogger _logger;
        private float _lastFireUnscaledTime = -999f;

        internal VoiceImpactBoostService(PluginSettings settings, PluginFileLogger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        internal void TryRequestBoost(float speedMps, float deltaMeters, string source)
        {
            if (!_settings.Enabled || !CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom) return;
            float now = Time.unscaledTime;
            float cooldownSec = _settings.CooldownMs * 0.001f;
            if (now - _lastFireUnscaledTime < cooldownSec)
            {
                if (_settings.VerboseLog)
                {
                    _logger.LogInfo("skip cooldown speed=" + speedMps.ToString("0.000"));
                }
                return;
            }

            float impact = CalculateSpeedImpact(speedMps);
            float peakMultiplier = ResolvePeakMultiplier(impact);
            int silenceMs = ResolveSilenceMs(impact);

            bool accepted = _settings.MoanModeEnabled
                ? PublicApi.TryRequestImpactCue(Plugin.GUID)
                : PublicApi.TryRequestTransientVolumeBoost(
                peakMultiplier,
                _settings.AttackMs,
                _settings.HoldMs,
                _settings.ReleaseMs,
                silenceMs,
                ConvertEasing(_settings.Easing));

            if (!accepted)
            {
                if (_settings.VerboseLog)
                {
                    _logger.LogInfo("boost rejected speed=" + speedMps.ToString("0.000") + " source=" + source);
                }
                return;
            }

            _lastFireUnscaledTime = now;
            if (_settings.VerboseLog)
            {
                _logger.LogInfo(
                    "BOOST fire speed=" + speedMps.ToString("0.000")
                    + " delta=" + deltaMeters.ToString("0.0000")
                    + " source=" + source
                    + " impact=" + impact.ToString("0.00")
                    + " peak=" + peakMultiplier.ToString("0.00")
                    + " env=" + _settings.AttackMs + "/" + _settings.HoldMs + "/" + _settings.ReleaseMs + "ms"
                    + " silence=" + silenceMs + "ms"
                    + " easing=" + _settings.Easing);
            }
        }

        private float CalculateSpeedImpact(float speedMps)
        {
            if (!_settings.EnableSpeedScaling)
            {
                return 0f;
            }

            float min = _settings.VelocityThresholdMps;
            float max = Mathf.Max(min + 0.01f, _settings.SpeedMaxMps);
            float normalized = Mathf.Clamp01((speedMps - min) / (max - min));

            // SmoothStep keeps the response gradual near both ends instead of jumping hard.
            return normalized * normalized * (3f - 2f * normalized);
        }

        private float ResolvePeakMultiplier(float impact)
        {
            if (!_settings.EnableSpeedScaling)
            {
                return _settings.PeakMultiplier;
            }

            return Mathf.Lerp(_settings.MinPeakMultiplier, _settings.MaxPeakMultiplier, impact);
        }

        private int ResolveSilenceMs(float impact)
        {
            if (!_settings.EnableSpeedScaling)
            {
                return _settings.SilenceMs;
            }

            return Mathf.RoundToInt(Mathf.Lerp(_settings.MinSilenceMs, _settings.MaxSilenceMs, impact));
        }

        private static PublicApi.EasingShape ConvertEasing(PluginSettings.EasingMode mode)
        {
            switch (mode)
            {
                case PluginSettings.EasingMode.Linear:
                    return PublicApi.EasingShape.Linear;
                default:
                    return PublicApi.EasingShape.CosineInOut;
            }
        }
    }
}
