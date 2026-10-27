using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        internal void EnforceTimelineAfterWaitSpeedProc(HFlag flags, AnimationCurve curve)
        {
            var s = Settings;
            if (s == null || !s.Enabled || flags == null)
                return;

            if (!IsTimelineHijackActive())
                return;

            TryHijackWaitSpeedProcByTimeline(flags, curve);
        }

        internal void EnforceTimelineAfterWaitSpeedProcAibu(HFlag flags)
        {
            var s = Settings;
            if (s == null || !s.Enabled || flags == null)
                return;

            if (!IsTimelineHijackActive())
                return;

            TryHijackWaitSpeedProcAibu(flags);
        }

        private bool TryHijackWaitSpeedProcByTimeline(HFlag flags, AnimationCurve curve)
        {
            if (!IsTimelineHijackActive())
                return false;

            var s = Settings;
            if (s == null)
                return false;

            // タイムライン時は本体 WaitSpeedProc を止める。
            // ゲージ位置(speedCalc)を確定し、speed はその位置に対するカーブ値で決める。
            GetEffectiveSourceRange(out float fromMin, out float fromMax);
            fromMax = Mathf.Max(fromMin + 0.0001f, fromMax);
            float sourceForGauge = Mathf.Clamp(s.TargetMaxSpeed, fromMin, fromMax);
            float calc = _timelineGaugeOverrideEnabled && _timelineGaugeOverride01 >= 0f
                ? Mathf.Clamp01(_timelineGaugeOverride01)
                : Mathf.Clamp01(Mathf.InverseLerp(fromMin, fromMax, sourceForGauge));

            float sourceForSpeed = curve != null
                ? curve.Evaluate(calc)
                : sourceForGauge;
            sourceForSpeed = Mathf.Clamp(sourceForSpeed, fromMin, fromMax);

            flags.speedCalc = calc;
            flags.speed = sourceForSpeed;
            flags.speedUpClac = new Vector2(calc, calc);
            flags.timeNoClick = 0f;

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

            if (s.VerboseLog && s.EnablePerFrameTrace && Time.unscaledTime >= _nextTimelineHijackLogTime)
            {
                _nextTimelineHijackLogTime = Time.unscaledTime + Mathf.Max(0.1f, s.LogIntervalSec);
                LogInfo(
                    $"timeline hijack waitSpeed mode={flags.mode} speedCalc={flags.speedCalc:0.###} " +
                    $"speed={flags.speed:0.###} targetMax={s.TargetMaxSpeed:0.###} " +
                    $"gaugeOverride={(_timelineGaugeOverrideEnabled ? _timelineGaugeOverride01.ToString("0.###") : "auto")}");
            }

            return true;
        }

        private bool IsTimelineHijackActive()
        {
            return TryCheckTimelineHijackActive(out _);
        }

        private bool TryCheckTimelineHijackActive(out string reason)
        {
            reason = null;
            var s = Settings;
            if (s == null)
            {
                reason = "settings-null";
                return false;
            }

            if (s.ForceVanillaSpeed)
            {
                reason = "force-vanilla";
                return false;
            }

            if (!s.EnableVideoTimeSpeedCues)
            {
                reason = "video-cues-disabled";
                return false;
            }

            if (_videoCueTimeline == null || _videoCueTimeline.Count == 0)
            {
                reason = "timeline-empty";
                return false;
            }

            if (!_videoRoomPlaybackAvailable || !_videoRoomPlaybackPrepared)
            {
                reason = !_videoRoomPlaybackAvailable ? "bridge-unavailable" : "bridge-not-prepared";
                return false;
            }

            if (!_videoRoomPlaybackPlaying)
            {
                reason = "video-not-playing";
                return false;
            }

            return true;
        }
    }
}
