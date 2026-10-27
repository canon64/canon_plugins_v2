using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        private enum GaugeTransitionEasing
        {
            Linear = 0,
            EaseInQuad = 1,
            EaseOutQuad = 2,
            EaseInOutQuad = 3,
            EaseInSine = 4,
            EaseOutSine = 5,
            EaseInOutSine = 6,
            SmoothStep = 7
        }

        private void UpdateVideoTimeSpeedCues()
        {
            var s = Settings;
            if (s == null || !s.EnableVideoTimeSpeedCues)
            {
                ResetVideoCueRuntime(clearTriggerOnce: true);
                return;
            }

            if (s.ForceVanillaSpeed)
            {
                ResetVideoCueRuntime(clearTriggerOnce: true);
                return;
            }

            if (_videoCueTimeline == null || _videoCueTimeline.Count == 0)
            {
                ResetVideoCueRuntime(clearTriggerOnce: true);
                return;
            }

            if (!_videoRoomPlaybackAvailable || !_videoRoomPlaybackPrepared)
            {
                ResetVideoCueRuntime(clearTriggerOnce: true);
                return;
            }

            if (!_videoRoomPlaybackPlaying)
            {
                return;
            }

            double currentTimeSec = Math.Max(0d, _videoRoomPlaybackTimeSec);

            if (_videoCuePrevTimeSec >= 0d && currentTimeSec + 0.25d < _videoCuePrevTimeSec)
            {
                _videoCueLoopCount++;
                StopTimelineGaugeTransition();
                if (_videoCueResetOnLoop)
                {
                    _videoCueTriggeredIndices.Clear();
                }

                LogInfo(
                    $"[video-cue] playback rewind detected prev={_videoCuePrevTimeSec:0.###} " +
                    $"current={currentTimeSec:0.###} loop={_videoCueLoopCount} " +
                    $"resetOnLoop={_videoCueResetOnLoop}");
            }

            UpdateTimelineGaugeTransition();

            List<int> hitCueIndices = null;
            for (int i = 0; i < _videoCueTimeline.Count; i++)
            {
                var cue = _videoCueTimeline[i];
                if (cue == null || !cue.Enabled || cue.TimeSec < 0d)
                {
                    continue;
                }

                if (_videoCueTriggeredIndices.Contains(i))
                {
                    continue;
                }

                if (cue.TriggerOnce && _videoCueTriggeredOnceIndices.Contains(i))
                {
                    continue;
                }

                if (!ShouldTriggerVideoCue(cue.TimeSec, _videoCuePrevTimeSec, currentTimeSec))
                {
                    continue;
                }

                if (hitCueIndices == null)
                {
                    hitCueIndices = new List<int>();
                }

                hitCueIndices.Add(i);
            }

            if (hitCueIndices != null && hitCueIndices.Count > 0)
            {
                int applyCueIndex = hitCueIndices[0];
                double latestCueTime = _videoCueTimeline[applyCueIndex].TimeSec;
                for (int i = 1; i < hitCueIndices.Count; i++)
                {
                    int idx = hitCueIndices[i];
                    double cueTime = _videoCueTimeline[idx].TimeSec;
                    if (cueTime >= latestCueTime)
                    {
                        latestCueTime = cueTime;
                        applyCueIndex = idx;
                    }
                }

                foreach (int idx in hitCueIndices)
                {
                    _videoCueTriggeredIndices.Add(idx);
                    if (_videoCueTimeline[idx].TriggerOnce)
                    {
                        _videoCueTriggeredOnceIndices.Add(idx);
                    }
                }

                var applyCue = _videoCueTimeline[applyCueIndex];
                bool gaugeApplied = ApplyCueGaugeTarget(applyCue, out string gaugeApplyResult);

                bool presetApplied = false;
                int presetIndex = -1;
                if (!string.IsNullOrWhiteSpace(applyCue.PresetName))
                {
                    presetApplied = TryApplyVideoCuePreset(applyCue.PresetName, out presetIndex);
                    if (!presetApplied)
                    {
                        LogWarn(
                            $"[video-cue] preset not found cueTime={applyCue.TimeSec:0.###} " +
                            $"videoTime={currentTimeSec:0.###} preset={applyCue.PresetName}");
                    }
                }

                bool actionsApplied = ExecuteVideoCueActions(applyCue, currentTimeSec);

                if (presetApplied || actionsApplied || gaugeApplied)
                {
                    LogInfo(
                        $"[video-cue] triggered cueTime={applyCue.TimeSec:0.###} " +
                        $"videoTime={currentTimeSec:0.###} preset={(string.IsNullOrWhiteSpace(applyCue.PresetName) ? "-" : applyCue.PresetName)} " +
                        $"presetIndex={(presetApplied ? presetIndex.ToString() : "-")} " +
                        $"actionsApplied={(actionsApplied ? "yes" : "no")} " +
                        $"gauge={gaugeApplyResult}");
                }
                else
                {
                    LogWarn(
                        $"[video-cue] cue fired but nothing applied cueTime={applyCue.TimeSec:0.###} " +
                        $"videoTime={currentTimeSec:0.###}");
                }
            }

            _videoCuePrevTimeSec = currentTimeSec;
        }

        private bool ApplyCueGaugeTarget(VideoTimeSpeedCue cue, out string result)
        {
            result = "auto";
            if (cue == null)
            {
                _timelineGaugeOverrideEnabled = false;
                _timelineGaugeOverride01 = -1f;
                StopTimelineGaugeTransition();
                return false;
            }

            if (cue.GaugePos01 < 0f)
            {
                _timelineGaugeOverrideEnabled = false;
                _timelineGaugeOverride01 = -1f;
                StopTimelineGaugeTransition();
                result = "auto";
                return false;
            }

            float target01 = Mathf.Clamp01(cue.GaugePos01);
            float transitionSec = Mathf.Max(0f, cue.GaugeTransitionSec);
            if (transitionSec <= 0.0001f)
            {
                _timelineGaugeOverrideEnabled = true;
                _timelineGaugeOverride01 = target01;
                StopTimelineGaugeTransition();
                result = $"instant:{target01:0.###}";
                return true;
            }

            float start01 = GetCurrentTimelineGauge01();
            StartTimelineGaugeTransition(start01, target01, transitionSec, cue.GaugeEasing, out string easingName, out bool easingFallback);
            result = $"transition:{start01:0.###}->{target01:0.###} sec={transitionSec:0.###} easing={easingName}";
            if (easingFallback)
            {
                LogWarn($"[video-cue] unknown GaugeEasing={cue.GaugeEasing}; fallback=linear");
            }

            return true;
        }

        private float GetCurrentTimelineGauge01()
        {
            if (_timelineGaugeOverrideEnabled && _timelineGaugeOverride01 >= 0f)
            {
                return Mathf.Clamp01(_timelineGaugeOverride01);
            }

            var s = Settings;
            if (s == null)
            {
                return 0f;
            }

            GetEffectiveSourceRange(out float effMin, out float effMax);
            effMax = Mathf.Max(effMin + 0.0001f, effMax);
            float sourceForGauge = Mathf.Clamp(s.TargetMaxSpeed, effMin, effMax);
            return Mathf.Clamp01(Mathf.InverseLerp(effMin, effMax, sourceForGauge));
        }

        private void StartTimelineGaugeTransition(
            float start01,
            float target01,
            float durationSec,
            string easingRaw,
            out string easingName,
            out bool easingFallback)
        {
            _timelineGaugeTransitionStart01 = Mathf.Clamp01(start01);
            _timelineGaugeTransitionTarget01 = Mathf.Clamp01(target01);
            _timelineGaugeTransitionDurationSec = Mathf.Max(0.0001f, durationSec);
            _timelineGaugeTransitionStartUnscaledTime = Mathf.Max(0f, Time.unscaledTime);
            _timelineGaugeTransitionEasing = ParseGaugeTransitionEasing(easingRaw, out easingName, out easingFallback);
            _timelineGaugeTransitionActive = true;

            _timelineGaugeOverrideEnabled = true;
            _timelineGaugeOverride01 = _timelineGaugeTransitionStart01;
        }

        private void StopTimelineGaugeTransition()
        {
            _timelineGaugeTransitionActive = false;
            _timelineGaugeTransitionStart01 = -1f;
            _timelineGaugeTransitionTarget01 = -1f;
            _timelineGaugeTransitionStartUnscaledTime = -1f;
            _timelineGaugeTransitionDurationSec = 0f;
            _timelineGaugeTransitionEasing = GaugeTransitionEasing.Linear;
        }

        private void UpdateTimelineGaugeTransition()
        {
            if (!_timelineGaugeTransitionActive)
            {
                return;
            }

            float duration = Mathf.Max(0.0001f, _timelineGaugeTransitionDurationSec);
            float elapsed = Mathf.Max(0f, Time.unscaledTime - _timelineGaugeTransitionStartUnscaledTime);
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = EvaluateGaugeTransitionEasing(_timelineGaugeTransitionEasing, t);
            float value = Mathf.Lerp(_timelineGaugeTransitionStart01, _timelineGaugeTransitionTarget01, eased);

            _timelineGaugeOverrideEnabled = true;
            _timelineGaugeOverride01 = Mathf.Clamp01(value);

            if (t >= 0.9999f)
            {
                _timelineGaugeOverride01 = _timelineGaugeTransitionTarget01;
                StopTimelineGaugeTransition();
            }
        }

        private static GaugeTransitionEasing ParseGaugeTransitionEasing(string raw, out string easingName, out bool fallbackUsed)
        {
            fallbackUsed = false;
            string key = raw?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                easingName = "linear";
                return GaugeTransitionEasing.Linear;
            }

            switch (key.ToLowerInvariant())
            {
                case "linear":
                    easingName = "linear";
                    return GaugeTransitionEasing.Linear;
                case "easein":
                case "easeinquad":
                    easingName = "easeInQuad";
                    return GaugeTransitionEasing.EaseInQuad;
                case "easeout":
                case "easeoutquad":
                    easingName = "easeOutQuad";
                    return GaugeTransitionEasing.EaseOutQuad;
                case "easeinout":
                case "easeinoutquad":
                    easingName = "easeInOutQuad";
                    return GaugeTransitionEasing.EaseInOutQuad;
                case "easeinsine":
                    easingName = "easeInSine";
                    return GaugeTransitionEasing.EaseInSine;
                case "easeoutsine":
                    easingName = "easeOutSine";
                    return GaugeTransitionEasing.EaseOutSine;
                case "easeinoutsine":
                    easingName = "easeInOutSine";
                    return GaugeTransitionEasing.EaseInOutSine;
                case "smoothstep":
                    easingName = "smoothStep";
                    return GaugeTransitionEasing.SmoothStep;
                default:
                    fallbackUsed = true;
                    easingName = "linear";
                    return GaugeTransitionEasing.Linear;
            }
        }

        private static float EvaluateGaugeTransitionEasing(GaugeTransitionEasing easing, float t)
        {
            t = Mathf.Clamp01(t);
            switch (easing)
            {
                case GaugeTransitionEasing.EaseInQuad:
                    return t * t;
                case GaugeTransitionEasing.EaseOutQuad:
                    return 1f - ((1f - t) * (1f - t));
                case GaugeTransitionEasing.EaseInOutQuad:
                    return t < 0.5f
                        ? (2f * t * t)
                        : (1f - (Mathf.Pow(-2f * t + 2f, 2f) / 2f));
                case GaugeTransitionEasing.EaseInSine:
                    return 1f - Mathf.Cos((t * Mathf.PI) * 0.5f);
                case GaugeTransitionEasing.EaseOutSine:
                    return Mathf.Sin((t * Mathf.PI) * 0.5f);
                case GaugeTransitionEasing.EaseInOutSine:
                    return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
                case GaugeTransitionEasing.SmoothStep:
                    return t * t * (3f - (2f * t));
                default:
                    return t;
            }
        }

        private void ReloadVideoCueTimelineFromConfiguredPath(bool migrateLegacyIfMissing)
        {
            string rawPath = _cfgVideoTimeCueFilePath != null
                ? (_cfgVideoTimeCueFilePath.Value ?? string.Empty)
                : "SpeedTimeline.json";

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                rawPath = "SpeedTimeline.json";
            }

            string resolvedPath = ResolveVideoCueFilePath(rawPath);
            _videoCueFileResolvedPath = resolvedPath;

            List<VideoTimeSpeedCue> legacyCues = null;
            bool legacyResetOnLoop = true;
            if (migrateLegacyIfMissing && Settings != null)
            {
                legacyCues = Settings.VideoTimeSpeedCues;
                legacyResetOnLoop = Settings.VideoTimeCuesResetOnLoop;
            }

            var timeline = VideoCueStore.LoadOrCreate(
                resolvedPath,
                legacyResetOnLoop,
                legacyCues,
                LogInfo,
                LogWarn,
                LogError);

            _videoCueResetOnLoop = timeline?.ResetOnLoop ?? true;
            _videoCueTimeline = timeline?.Cues ?? new List<VideoTimeSpeedCue>();
            ResetVideoCueRuntime(clearTriggerOnce: true);

            LogInfo(
                $"[video-cue] timeline loaded path={resolvedPath} " +
                $"cues={_videoCueTimeline.Count} resetOnLoop={_videoCueResetOnLoop}");
        }

        private string ResolveVideoCueFilePath(string configuredPath)
        {
            string raw = configuredPath ?? string.Empty;
            raw = raw.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = "SpeedTimeline.json";
            }

            string resolved;
            if (Path.IsPathRooted(raw))
            {
                resolved = Path.GetFullPath(raw);
            }
            else
            {
                resolved = Path.GetFullPath(Path.Combine(_pluginDir, raw));
            }

            return resolved;
        }

        private void ResetVideoCueRuntime(bool clearTriggerOnce)
        {
            _videoCuePrevTimeSec = -1d;
            _videoCueLoopCount = 0;
            _videoCueTriggeredIndices.Clear();
            _timelineGaugeOverrideEnabled = false;
            _timelineGaugeOverride01 = -1f;
            StopTimelineGaugeTransition();
            if (clearTriggerOnce)
            {
                _videoCueTriggeredOnceIndices.Clear();
            }
        }

        private static bool ShouldTriggerVideoCue(double cueTimeSec, double prevTimeSec, double currentTimeSec)
        {
            if (cueTimeSec < 0d)
            {
                return false;
            }

            if (prevTimeSec < 0d)
            {
                return cueTimeSec <= currentTimeSec;
            }

            if (currentTimeSec >= prevTimeSec)
            {
                return cueTimeSec > prevTimeSec && cueTimeSec <= currentTimeSec;
            }

            // Backward seek / loop frame.
            return cueTimeSec <= currentTimeSec;
        }

        private bool TryApplyVideoCuePreset(string presetName, out int presetIndex)
        {
            presetIndex = -1;
            var s = Settings;
            if (s == null || s.BpmPresets == null || s.BpmPresets.Count == 0)
            {
                return false;
            }

            string key = presetName?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            for (int i = 0; i < s.BpmPresets.Count; i++)
            {
                var p = s.BpmPresets[i];
                if (p == null)
                {
                    continue;
                }

                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.AnimationName, key, StringComparison.OrdinalIgnoreCase))
                {
                    presetIndex = i;
                    break;
                }
            }

            if (presetIndex < 0)
            {
                return false;
            }

            ApplyPreset(presetIndex, saveSettings: false, reason: "video cue");
            return true;
        }
    }
}
