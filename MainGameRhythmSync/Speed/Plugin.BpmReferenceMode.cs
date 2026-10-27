using System;
using System.Globalization;
using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        private enum BpmReferenceMode
        {
            Sonyu = 0,
            Houshi = 1,
            MasturbationStage1 = 2,
            MasturbationStage2 = 3,
            MasturbationClimax = 4,
            MasturbationOLoop = 5
        }

        private const float MasturbationStage1BpmMin = 15.8f;
        private const float MasturbationStage1BpmMax = 97.8f;
        private const float MasturbationStage2BpmMin = 23.6f;
        private const float MasturbationStage2BpmMax = 149.6f;
        private const float MasturbationClimaxBpmMin = 39.0f;
        private const float MasturbationClimaxBpmMax = 257.0f;
        private const float MasturbationOLoopBpmMin = 31.7f;
        private const float MasturbationOLoopBpmMax = 430.0f;

        private BpmReferenceMode _activeBpmReferenceMode = BpmReferenceMode.Sonyu;
        private string _lastBpmReferenceAnimationName = string.Empty;
        private float _nextBpmReferenceModeScanTime;

        private void UpdateBpmReferenceMode(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextBpmReferenceModeScanTime)
            {
                return;
            }

            _nextBpmReferenceModeScanTime = Time.unscaledTime + 0.25f;
            if (!TryResolveBpmReferenceMode(out BpmReferenceMode mode, out string animationName, out string source))
            {
                return;
            }

            string animationKey = $"{source}:{animationName}";
            bool modeChanged = mode != _activeBpmReferenceMode;
            bool animationChanged = !string.Equals(
                animationKey,
                _lastBpmReferenceAnimationName,
                StringComparison.OrdinalIgnoreCase);

            _activeBpmReferenceMode = mode;
            _lastBpmReferenceAnimationName = animationKey;

            if (!modeChanged && !animationChanged)
            {
                return;
            }

            var settings = Settings;
            if (settings == null)
            {
                return;
            }

            float beforeTargetMin = settings.TargetMinSpeed;
            float beforeTargetMax = settings.TargetMaxSpeed;

            bool isMasturbation = IsHardcodedMasturbationReferenceMode(mode);
            if (isMasturbation && !_masturbationSourceRangeActive)
            {
                _masturbationSourceRangeActive = true;
                LogInfo($"source range switched to masturbation: effective=[{MasturbationEffectiveSourceMin:0.###}..{MasturbationEffectiveSourceMax:0.###}] stored=[{settings.SourceMinSpeed:0.###}..{settings.SourceMaxSpeed:0.###}]");
            }
            else if (!isMasturbation && _masturbationSourceRangeActive)
            {
                _masturbationSourceRangeActive = false;
                LogInfo($"source range restored to stored: [{settings.SourceMinSpeed:0.###}..{settings.SourceMaxSpeed:0.###}]");
            }

            bool pushConfigEntries = !isMasturbation;
            ApplyStoredCalibrationToWorkingValues(settings, mode, pushConfigEntries);
            EnsureAppliedBpmRangeInitialized(settings);
            ApplyAppliedRangeToTargetSpeeds(settings, settings.AppliedBpmMin, settings.AppliedBpmMax);

            LogInfo(
                $"bpm reference applied: mode={GetBpmReferenceModeLabel(mode)} src={source} anim={animationName} " +
                $"ref=[{settings.BpmReferenceAtSourceMin:0.##}/{settings.BpmReferenceAtSpeed3:0.##}] " +
                $"applied=[{settings.AppliedBpmMin:0.##}/{settings.AppliedBpmMax:0.##}] " +
                $"target=[{beforeTargetMin:0.###}/{beforeTargetMax:0.###}]=>[{settings.TargetMinSpeed:0.###}/{settings.TargetMaxSpeed:0.###}]");
            if (modeChanged)
            {
                LogInfo($"bpm reference mode switched: {GetBpmReferenceModeLabel(mode)} ({animationName}, src={source})");
            }
        }

        private BpmReferenceMode ResolveCurrentBpmReferenceMode()
        {
            if (TryResolveBpmReferenceMode(out BpmReferenceMode mode, out string animationName, out string source))
            {
                _activeBpmReferenceMode = mode;
                _lastBpmReferenceAnimationName = $"{source}:{animationName}";
                return mode;
            }

            return _activeBpmReferenceMode;
        }

        private bool TryResolveBpmReferenceMode(out BpmReferenceMode mode, out string animationName, out string source)
        {
            mode = _activeBpmReferenceMode;
            animationName = string.Empty;
            source = "none";

            if (TryResolveBpmReferenceModeFromFlags(out BpmReferenceMode fromFlagsMode, out string fromFlagsAnim, out string fromFlagsSource))
            {
                mode = fromFlagsMode;
                animationName = string.IsNullOrWhiteSpace(fromFlagsAnim) ? "<no-anim-info>" : fromFlagsAnim;
                source = fromFlagsSource;
                return true;
            }

            // Important: do not classify mode from animator clip names (e.g. M_SLoop1 / M_WLoop1).
            // Those names can diverge from actual H mode and caused wrong BPM base selection.
            return false;
        }

        private bool TryResolveBpmReferenceModeFromFlags(
            out BpmReferenceMode mode,
            out string animationName,
            out string source)
        {
            mode = _activeBpmReferenceMode;
            animationName = string.Empty;
            source = string.Empty;

            if (_hSceneProc == null || _hSceneProc.flags == null)
            {
                return false;
            }

            HFlag flags = _hSceneProc.flags;
            HSceneProc.AnimationListInfo nowInfo = flags.nowAnimationInfo;
            animationName = nowInfo?.nameAnimation ?? string.Empty;

            bool isMasturbationByFlags = IsMasturbationReferenceTarget(flags.mode);
            bool isMasturbationByNowInfo = nowInfo != null && IsMasturbationReferenceTarget(nowInfo.mode);
            if (isMasturbationByFlags || isMasturbationByNowInfo)
            {
                if (TryResolveMasturbationReferenceMode(out BpmReferenceMode masturbationMode, out string clipName))
                {
                    mode = masturbationMode;
                    animationName = string.IsNullOrWhiteSpace(clipName) ? animationName : clipName;
                    source = "animator.clip(masturbation)";
                    return true;
                }

                mode = BpmReferenceMode.MasturbationStage1;
                if (string.IsNullOrWhiteSpace(animationName))
                {
                    animationName = "masturbation";
                }

                source = isMasturbationByFlags
                    ? "flags.mode(masturbation-fallback)"
                    : "flags.nowAnimationInfo.mode(masturbation-fallback)";
                return true;
            }

            if (TryMapHModeToReferenceMode(flags.mode, out BpmReferenceMode modeByFlags))
            {
                mode = modeByFlags;
                source = "flags.mode";
                return true;
            }

            if (nowInfo != null && TryMapHModeToReferenceMode(nowInfo.mode, out BpmReferenceMode modeByNowInfo))
            {
                mode = modeByNowInfo;
                source = "flags.nowAnimationInfo.mode";
                return true;
            }

            return false;
        }

        private static bool TryMapHModeToReferenceMode(HFlag.EMode hMode, out BpmReferenceMode mode)
        {
            switch (hMode)
            {
                case HFlag.EMode.houshi:
                case HFlag.EMode.houshi3P:
                case HFlag.EMode.houshi3PMMF:
                    mode = BpmReferenceMode.Houshi;
                    return true;

                case HFlag.EMode.sonyu:
                case HFlag.EMode.sonyu3P:
                case HFlag.EMode.sonyu3PMMF:
                    mode = BpmReferenceMode.Sonyu;
                    return true;

                default:
                    mode = BpmReferenceMode.Sonyu;
                    return false;
            }
        }

        private static bool IsMasturbationReferenceTarget(HFlag.EMode mode)
        {
            string name = mode.ToString();
            return string.Equals(name, "masturbation", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryResolveMasturbationReferenceMode(out BpmReferenceMode mode, out string clipName)
        {
            mode = BpmReferenceMode.MasturbationStage1;
            clipName = string.Empty;

            if (!TryGetPrimaryFemale(out ChaControl female) || female == null)
            {
                return false;
            }

            Animator animator = female.animBody;
            if (animator == null)
            {
                return false;
            }

            var clips = animator.GetCurrentAnimatorClipInfo(0);
            if (clips == null || clips.Length <= 0 || clips[0].clip == null)
            {
                return false;
            }

            clipName = NormalizeMasturbationTraceToken(clips[0].clip.name);
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return false;
            }

            return TryMapMasturbationClipToReferenceMode(clipName, out mode);
        }

        private static bool TryMapMasturbationClipToReferenceMode(string clipName, out BpmReferenceMode mode)
        {
            mode = BpmReferenceMode.MasturbationStage1;
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return false;
            }

            string normalized = clipName.Trim().ToLowerInvariant();
            if (normalized.Contains("oloop"))
            {
                mode = BpmReferenceMode.MasturbationOLoop;
                return true;
            }

            if (normalized.Contains("sloop"))
            {
                mode = BpmReferenceMode.MasturbationClimax;
                return true;
            }

            if (normalized.Contains("mloop"))
            {
                mode = BpmReferenceMode.MasturbationStage2;
                return true;
            }

            if (normalized.Contains("wloop"))
            {
                mode = BpmReferenceMode.MasturbationStage1;
                return true;
            }

            return false;
        }

        private static bool IsHardcodedMasturbationReferenceMode(BpmReferenceMode mode)
        {
            switch (mode)
            {
                case BpmReferenceMode.MasturbationStage1:
                case BpmReferenceMode.MasturbationStage2:
                case BpmReferenceMode.MasturbationClimax:
                case BpmReferenceMode.MasturbationOLoop:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetHardcodedMasturbationCalibrationPair(BpmReferenceMode mode, out float minRef, out float maxRef)
        {
            switch (mode)
            {
                case BpmReferenceMode.MasturbationStage1:
                    minRef = MasturbationStage1BpmMin;
                    maxRef = MasturbationStage1BpmMax;
                    return true;
                case BpmReferenceMode.MasturbationStage2:
                    minRef = MasturbationStage2BpmMin;
                    maxRef = MasturbationStage2BpmMax;
                    return true;
                case BpmReferenceMode.MasturbationClimax:
                    minRef = MasturbationClimaxBpmMin;
                    maxRef = MasturbationClimaxBpmMax;
                    return true;
                case BpmReferenceMode.MasturbationOLoop:
                    minRef = MasturbationOLoopBpmMin;
                    maxRef = MasturbationOLoopBpmMax;
                    return true;
                default:
                    minRef = 0f;
                    maxRef = 1f;
                    return false;
            }
        }

        private static BpmReferenceMode ClassifyBpmReferenceMode(string animationName, BpmReferenceMode fallback)
        {
            if (string.IsNullOrWhiteSpace(animationName))
            {
                return fallback;
            }

            string name = animationName.Trim().ToLowerInvariant();
            if (name.Contains("houshi") ||
                name.Contains("fera") ||
                name.Contains("service"))
            {
                return BpmReferenceMode.Houshi;
            }

            if (name.Contains("sonyu") ||
                name.Contains("insert"))
            {
                return BpmReferenceMode.Sonyu;
            }

            return fallback;
        }

        private static string GetBpmReferenceModeLabel(BpmReferenceMode mode)
        {
            switch (mode)
            {
                case BpmReferenceMode.Houshi:
                    return "奉仕";
                case BpmReferenceMode.MasturbationStage1:
                    return "オナニー第1";
                case BpmReferenceMode.MasturbationStage2:
                    return "オナニー第2";
                case BpmReferenceMode.MasturbationClimax:
                    return "オナニー(イキ)";
                case BpmReferenceMode.MasturbationOLoop:
                    return "オナニー(OLoop)";
                default:
                    return "挿入";
            }
        }

        private static void NormalizeCalibrationPair(ref float minRef, ref float maxRef)
        {
            if (minRef < 0f)
            {
                minRef = 0f;
            }

            if (maxRef < 1f)
            {
                maxRef = 1f;
            }

            if (minRef > 0f && maxRef <= minRef)
            {
                maxRef = minRef + 1f;
            }
        }

        private static void GetStoredCalibrationPair(
            PluginSettings settings,
            BpmReferenceMode mode,
            out float minRef,
            out float maxRef)
        {
            if (TryGetHardcodedMasturbationCalibrationPair(mode, out minRef, out maxRef))
            {
                NormalizeCalibrationPair(ref minRef, ref maxRef);
                return;
            }

            minRef = 0f;
            maxRef = 1f;
            if (settings == null)
            {
                return;
            }

            if (mode == BpmReferenceMode.Houshi)
            {
                minRef = settings.BpmReferenceAtSourceMinHoushi;
                maxRef = settings.BpmReferenceAtSpeed3Houshi;
            }
            else
            {
                minRef = settings.BpmReferenceAtSourceMinSonyu;
                maxRef = settings.BpmReferenceAtSpeed3Sonyu;
            }

            NormalizeCalibrationPair(ref minRef, ref maxRef);
        }

        private static void SetStoredCalibrationPair(
            PluginSettings settings,
            BpmReferenceMode mode,
            float minRef,
            float maxRef)
        {
            if (IsHardcodedMasturbationReferenceMode(mode))
            {
                return;
            }

            if (settings == null)
            {
                return;
            }

            NormalizeCalibrationPair(ref minRef, ref maxRef);
            if (mode == BpmReferenceMode.Houshi)
            {
                settings.BpmReferenceAtSourceMinHoushi = minRef;
                settings.BpmReferenceAtSpeed3Houshi = maxRef;
            }
            else
            {
                settings.BpmReferenceAtSourceMinSonyu = minRef;
                settings.BpmReferenceAtSpeed3Sonyu = maxRef;
            }
        }

        private void ApplyStoredCalibrationToWorkingValues(
            PluginSettings settings,
            BpmReferenceMode mode,
            bool pushConfigEntries)
        {
            if (settings == null)
            {
                return;
            }

            GetStoredCalibrationPair(settings, mode, out float minRef, out float maxRef);
            settings.BpmReferenceAtSourceMin = minRef;
            settings.BpmReferenceAtSpeed3 = maxRef;
            _bpmMinInput = minRef.ToString("0.##", CultureInfo.InvariantCulture);
            _bpmMaxInput = maxRef.ToString("0.##", CultureInfo.InvariantCulture);

            if (pushConfigEntries)
            {
                PushCalibrationToConfigEntries(minRef, maxRef);
            }
        }

        private void SaveCalibrationForModeAndApplyWorking(
            PluginSettings settings,
            BpmReferenceMode mode,
            float minRef,
            float maxRef,
            bool pushConfigEntries)
        {
            if (settings == null)
            {
                return;
            }

            SetStoredCalibrationPair(settings, mode, minRef, maxRef);
            ApplyStoredCalibrationToWorkingValues(settings, mode, pushConfigEntries);
        }

        private void SyncCalibrationConfigEntriesForMode(PluginSettings settings, BpmReferenceMode mode)
        {
            if (settings == null || _cfgBpmReferenceAtSourceMin == null || _cfgBpmReferenceAtSourceMax == null)
            {
                return;
            }

            if (IsHardcodedMasturbationReferenceMode(mode))
            {
                return;
            }

            GetStoredCalibrationPair(settings, mode, out float minRef, out float maxRef);
            settings.BpmReferenceAtSourceMin = minRef;
            settings.BpmReferenceAtSpeed3 = maxRef;

            _suppressConfigSync = true;
            _cfgBpmReferenceAtSourceMin.Value = minRef;
            _cfgBpmReferenceAtSourceMax.Value = maxRef;
            _suppressConfigSync = false;
        }

        private static BpmReferenceMode ClassifyBpmReferenceModeForPreset(string animationName)
        {
            return ClassifyBpmReferenceMode(animationName, BpmReferenceMode.Sonyu);
        }
    }
}
