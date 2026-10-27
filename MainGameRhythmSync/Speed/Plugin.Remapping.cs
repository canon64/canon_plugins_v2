using System;
using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        internal bool TryRemap(string param, ref float value)
        {
            var s = Settings;
            if (s == null || !s.Enabled)
                return false;

            if (s.ForceVanillaSpeed)
                return false;

            if (!s.EnableBpmSpeedRemap.GetValueOrDefault(true))
                return false;

            if (s.ApplyOnlyInsideHScene && !_insideHScene)
                return false;

            bool isSpeed = string.Equals(param, "speed", StringComparison.Ordinal);
            bool isSpeedBody = string.Equals(param, "speedBody", StringComparison.Ordinal);
            if ((isSpeed && !s.AffectsSpeed) || (isSpeedBody && !s.AffectsSpeedBody))
                return false;

            if (!isSpeed && !isSpeedBody)
                return false;

            GetEffectiveSourceRange(out float fromMin, out float fromMax);
            if (s.IgnoreValuesBelowSourceMin && value < fromMin)
                return false;

            fromMax = Mathf.Max(fromMin + 0.0001f, fromMax);
            float toMin = s.TargetMinSpeed;
            float toMax = Mathf.Max(toMin, s.TargetMaxSpeed);

            float t = Mathf.InverseLerp(fromMin, fromMax, value);
            float mapped = Mathf.Lerp(toMin, toMax, t);

            if (Mathf.Approximately(mapped, value))
                return false;

            float before = value;
            value = mapped;

            if (s.VerboseLog && s.EnablePerFrameTrace && Time.unscaledTime >= _nextVerboseLogTime)
            {
                _nextVerboseLogTime = Time.unscaledTime + Mathf.Max(0.1f, s.LogIntervalSec);
                LogInfo($"map param={param} in={before:0.###} out={mapped:0.###} insideH={_insideHScene}");
            }

            return true;
        }

        // The group driver owns all Animator.speed writes. Solo motions still use
        // the configured range, derived from the gauge (never from our last output).
        internal float GetSoloManualSpeed(float gauge)
        {
            var s = Settings;
            if (!_masturbationSourceRangeActive || s == null || !s.Enabled ||
                s.ForceVanillaSpeed || !s.EnableBpmSpeedRemap.GetValueOrDefault(true))
                return 1f;
            return Mathf.Lerp(s.TargetMinSpeed, Mathf.Max(s.TargetMinSpeed, s.TargetMaxSpeed),
                Mathf.Clamp01(gauge));
        }

        internal bool TryHijackWaitSpeedProc(HFlag flags, bool isLock, AnimationCurve curve)
        {
            var s = Settings;
            if (s == null || !s.Enabled)
                return false;

            if (s.ForceVanillaSpeed)
                return false;

            if (flags == null || curve == null)
                return false;

            // User expectation: SpeedTimeline OFF means no speed hijack at all.
            if (!s.EnableVideoTimeSpeedCues)
                return false;

            // Timeline有効時は、動画再生中のcue制御だけを許可する。
            // 再生停止中/未準備時にAutoSonyuへフォールバックして全開固定しない。
            if (s.EnableVideoTimeSpeedCues)
            {
                return TryHijackWaitSpeedProcByTimeline(flags, curve);
            }

            if (s.ApplyOnlyInsideHScene && !_insideHScene)
                return false;

            if (!s.EnableAutoSonyuHijack)
                return false;

            if (s.AutoSonyuHijackRequireAutoLock && !isLock)
                return false;

            if (!IsHijackTargetMode(flags.mode, s))
                return false;

            float desiredSpeed = s.AutoSonyuHijackUseSourceMax ? s.TargetMaxSpeed : s.AutoSonyuHijackFixedSourceSpeed;
            desiredSpeed = Mathf.Max(0f, desiredSpeed);

            GetEffectiveSourceRange(out float fromMin, out float fromMax);
            fromMax = Mathf.Max(fromMin + 0.0001f, fromMax);
            float sourceForGauge = Mathf.Clamp(desiredSpeed, fromMin, fromMax);
            float calc = Mathf.Clamp01(Mathf.InverseLerp(fromMin, fromMax, sourceForGauge));

            flags.speedCalc = calc;
            // Keep in-game gauge semantics (1..SourceMax) and only boost animator param via TryRemap.
            flags.speed = sourceForGauge;

            if (flags.voice != null)
            {
                float threshold = flags.speedMaxBody * flags.speedVoiceChangeSpeedRate;
                if (!flags.voice.speedMotion && flags.speedCalc > threshold)
                {
                    flags.voice.speedMotion = true;
                }
                else if (flags.voice.speedMotion && flags.speedCalc < flags.speedMaxBody - threshold)
                {
                    flags.voice.speedMotion = false;
                }
            }

            if (s.VerboseLog && s.EnablePerFrameTrace && Time.unscaledTime >= _nextHijackLogTime)
            {
                _nextHijackLogTime = Time.unscaledTime + Mathf.Max(0.1f, s.LogIntervalSec);
                LogInfo($"hijack waitSpeed mode={flags.mode} autoLock={isLock} speed={flags.speed:0.###} speedCalc={flags.speedCalc:0.###} desiredAnimSpeed={desiredSpeed:0.###}");
            }

            return true;
        }

        internal bool TryHijackWaitSpeedProcAibu(HFlag flags)
        {
            var s = Settings;
            if (s == null || !s.Enabled || flags == null)
                return false;

            if (s.ForceVanillaSpeed)
                return false;

            // Timeline hijack takes priority.
            if (IsTimelineHijackActive())
            {
                GetEffectiveSourceRange(out float effMin_, out float effMax_);
                float speedCap = Mathf.Max(0.0001f, flags.speedMaxBody > 0f ? flags.speedMaxBody : effMax_);
                float desired = _timelineGaugeOverrideEnabled && _timelineGaugeOverride01 >= 0f
                    ? speedCap * Mathf.Clamp01(_timelineGaugeOverride01)
                    : Mathf.Clamp(s.TargetMaxSpeed, 0f, speedCap);

                flags.speed = desired;
                flags.speedItem = desired;
                flags.speedUpClac = new Vector2(desired, desired);
                flags.timeNoClick = 0f;
                UpdateAibuVoiceSpeed(flags);

                if (s.VerboseLog && s.EnablePerFrameTrace && Time.unscaledTime >= _nextTimelineHijackLogTime)
                {
                    _nextTimelineHijackLogTime = Time.unscaledTime + Mathf.Max(0.1f, s.LogIntervalSec);
                    LogInfo(
                        $"timeline hijack aibu speed={flags.speed:0.###} speedMaxBody={flags.speedMaxBody:0.###} " +
                        $"gaugeOverride={(_timelineGaugeOverrideEnabled ? _timelineGaugeOverride01.ToString("0.###") : "auto")}");
                }

                return true;
            }

            return false;
        }

        private static void UpdateAibuVoiceSpeed(HFlag flags)
        {
            if (flags == null || flags.voice == null)
                return;

            float threshold = flags.speedMaxBody * flags.speedVoiceChangeSpeedRate;
            if (!flags.voice.speedMotion && flags.speed > threshold)
            {
                flags.voice.speedMotion = true;
            }
            else if (flags.voice.speedMotion && flags.speed < flags.speedMaxBody - threshold)
            {
                flags.voice.speedMotion = false;
            }
        }

        private static bool IsHijackTargetMode(HFlag.EMode mode, PluginSettings s)
        {
            if (mode == HFlag.EMode.sonyu)
                return true;

            if (s.AutoSonyuHijackAlsoSonyu3P && mode == HFlag.EMode.sonyu3P)
                return true;

            if (s.AutoSonyuHijackAlsoSonyu3PMMF && mode == HFlag.EMode.sonyu3PMMF)
                return true;

            return false;
        }
    }
}
