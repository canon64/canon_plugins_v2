using System;
using MainGirlHipHijack.Services;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private IEnumerator CapturePosePresetScreenshotCoroutine(PosePresetRuntime preset)
        {
            if (preset == null)
                yield break;

            yield return new WaitForEndOfFrame();

            string shotPath = GetPosePresetScreenshotPath(preset);
            if (string.IsNullOrEmpty(shotPath))
                yield break;

            try
            {
                ScreenCapture.CaptureScreenshot(shotPath);
            }
            catch (Exception ex)
            {
                LogError("[PosePreset] screenshot capture failed: " + ex.Message);
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < deadline)
            {
                try
                {
                    if (File.Exists(shotPath))
                    {
                        var fi = new FileInfo(shotPath);
                        if (fi.Length > 0)
                            break;
                    }
                }
                catch
                {
                    // ignore polling errors
                }

                yield return null;
            }

            _posePresetThumbDirty = true;
            LogInfo("[PosePreset] screenshot saved path=" + shotPath);
        }

        private Texture2D GetPosePresetThumbnail(PosePresetRuntime preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.id))
                return null;

            Texture2D cached;
            if (_posePresetThumbCache.TryGetValue(preset.id, out cached))
                return cached;

            string shotPath = GetPosePresetScreenshotPath(preset);
            if (string.IsNullOrEmpty(shotPath) || !File.Exists(shotPath))
                return null;

            try
            {
                byte[] bytes = File.ReadAllBytes(shotPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!ImageConversion.LoadImage(tex, bytes))
                {
                    Destroy(tex);
                    return null;
                }

                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                _posePresetThumbCache[preset.id] = tex;
                return tex;
            }
            catch (Exception ex)
            {
                LogError("[PosePreset] thumbnail load failed: " + ex.Message);
                return null;
            }
        }

        private void RefreshPosePresetThumbCacheIfNeeded()
        {
            if (!_posePresetThumbDirty)
                return;

            DisposePosePresetThumbCache();
            _posePresetThumbDirty = false;
        }

        private void DisposePosePresetThumbCache()
        {
            foreach (var kv in _posePresetThumbCache)
            {
                if (kv.Value != null)
                    Destroy(kv.Value);
            }
            _posePresetThumbCache.Clear();
        }

        private void HandlePosePresetThumbnailClick(PosePresetRuntime preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.id))
                return;

            float now = Time.unscaledTime;
            bool isDouble = string.Equals(_lastThumbClickedPresetId, preset.id, StringComparison.Ordinal)
                && (now - _lastThumbClickTime) <= PoseThumbnailDoubleClickWindow;

            _lastThumbClickedPresetId = preset.id;
            _lastThumbClickTime = now;

            if (!isDouble)
                return;

            bool exactMotion = IsCurrentPostureMatch(preset);
            bool legacyMotion = IsLegacyPosePresetForCurrentPosture(preset);
            if (!exactMotion && !legacyMotion)
                return;

            ApplyPosePreset(preset, requireCurrentPosture: exactMotion, reason: legacyMotion ? "thumb-double-click-legacy" : "thumb-double-click");
        }

        private bool TryApplyRandomAutoPoseForCurrentPosture(string reason)
        {
            EnsurePosePresetsLoaded();
            if (!_hSceneSession.HasCurrentAnimationInfo)
                return false;

            PosePresetRuntime target;
            if (!TryPickAutoPoseCandidate(
                _posePresets,
                IsAutoFemalePoseCandidateForCurrentPosture,
                preset => preset != null ? preset.id : null,
                _autoPoseLastAppliedPresetId,
                out target))
            {
                LogDebug("[PosePreset] auto female candidate none posture=" + BuildCurrentPostureHint());
                return false;
            }

            bool exactMotion = IsCurrentPostureMatch(target);
            bool legacyMotion = IsLegacyPosePresetForCurrentPosture(target);
            bool applied = ApplyPosePreset(
                target,
                requireCurrentPosture: exactMotion,
                reason: legacyMotion ? reason + "-legacy" : reason);
            if (applied)
            {
                _autoPosePendingApply = false;
                _autoPoseLoopCountSinceSwitch = 0;
            }

            return applied;
        }

        private bool TryApplyRandomAutoMalePoseForCurrentPosture(string reason)
        {
            if (MaleFeaturesTemporarilySealed)
                return false;

            EnsureMalePosePresetsLoaded();
            if (!_hSceneSession.HasCurrentAnimationInfo)
                return false;

            MalePosePresetItem target;
            if (!TryPickAutoPoseCandidate(
                _malePosePresets,
                IsAutoMalePoseCandidateForCurrentPosture,
                preset => preset != null ? preset.id : null,
                _autoMalePoseLastAppliedPresetId,
                out target))
            {
                LogDebug("[MalePosePreset] auto candidate none posture=" + BuildCurrentPostureHint());
                return false;
            }

            bool applied = ApplyMalePosePresetById(target.id, reason);
            if (applied)
            {
                _autoPosePendingApply = false;
                _autoPoseLoopCountSinceSwitch = 0;
            }

            return applied;
        }

        private bool TryApplyRandomAutoPoseSetForCurrentPosture(string reason)
        {
            EnsurePosePresetsLoaded();
            if (!MaleFeaturesTemporarilySealed)
                EnsureMalePosePresetsLoaded();
            if (!_hSceneSession.HasCurrentAnimationInfo)
                return false;

            int femaleCount = CountAutoPoseCandidates(
                _posePresets,
                IsAutoFemalePoseCandidateForCurrentPosture);
            int maleCount = MaleFeaturesTemporarilySealed
                ? 0
                : CountAutoPoseCandidates(
                    _malePosePresets,
                    IsAutoMalePoseCandidateForCurrentPosture);

            bool loopSwitch = string.Equals(reason, "auto-loop-threshold", StringComparison.Ordinal);
            bool femaleApplied = (!loopSwitch || femaleCount > 1) && TryApplyRandomAutoPoseForCurrentPosture(reason);
            bool maleApplied = (!loopSwitch || maleCount > 1) && TryApplyRandomAutoMalePoseForCurrentPosture(reason);
            if (!femaleApplied && !maleApplied)
            {
                if (femaleCount <= 0 && maleCount <= 0)
                {
                    LogDebug("[PosePreset] auto candidate none posture=" + BuildCurrentPostureHint());
                    DisableAllBodyIK();
                }
                return false;
            }

            return true;
        }

        private int CountAutoPoseCandidatesForCurrentPosture()
        {
            EnsurePosePresetsLoaded();
            if (!MaleFeaturesTemporarilySealed)
                EnsureMalePosePresetsLoaded();
            if (!_hSceneSession.HasCurrentAnimationInfo)
                return 0;

            int femaleCount = CountAutoPoseCandidates(
                _posePresets,
                IsAutoFemalePoseCandidateForCurrentPosture);
            int maleCount = MaleFeaturesTemporarilySealed
                ? 0
                : CountAutoPoseCandidates(
                    _malePosePresets,
                    IsAutoMalePoseCandidateForCurrentPosture);

            return Math.Max(femaleCount, maleCount);
        }

        private bool IsAutoFemalePoseCandidateForCurrentPosture(PosePresetRuntime preset)
        {
            if (preset == null || !preset.autoApply)
                return false;

            return IsCurrentPoseContextMatch(preset, out _)
                || IsLegacyPosePresetForCurrentPosture(preset);
        }

        private bool IsAutoMalePoseCandidateForCurrentPosture(MalePosePresetItem preset)
        {
            if (preset == null || !preset.autoApply)
                return false;

            return IsCurrentPostureMatch(preset)
                || IsLegacyMalePosePresetForCurrentPosture(preset);
        }

        private static int CountAutoPoseCandidates<T>(IList<T> presets, Predicate<T> isCandidate)
        {
            if (presets == null || isCandidate == null)
                return 0;

            int count = 0;
            for (int i = 0; i < presets.Count; i++)
            {
                if (isCandidate(presets[i]))
                    count++;
            }

            return count;
        }

        private static bool TryPickAutoPoseCandidate<T>(
            IList<T> presets,
            Predicate<T> isCandidate,
            Func<T, string> getId,
            string lastAppliedId,
            out T target)
        {
            target = default(T);
            if (presets == null || isCandidate == null)
                return false;

            var candidates = new List<T>();
            for (int i = 0; i < presets.Count; i++)
            {
                T preset = presets[i];
                if (isCandidate(preset))
                    candidates.Add(preset);
            }

            if (candidates.Count <= 0)
                return false;

            int selected = UnityEngine.Random.Range(0, candidates.Count);
            if (candidates.Count > 1 && getId != null && !string.IsNullOrEmpty(lastAppliedId))
            {
                for (int i = 0; i < 4; i++)
                {
                    T probe = candidates[selected];
                    if (!string.Equals(getId(probe), lastAppliedId, StringComparison.Ordinal))
                        break;
                    selected = UnityEngine.Random.Range(0, candidates.Count);
                }
            }

            target = candidates[selected];
            return true;
        }

        private void OnPostureContextChanged(int id, int mode, string name, string source)
        {
            HandleFemaleHeadAngleContextChange(source);
            ResetFemaleHandPoseRuntimeAndSettings(saveSettings: true);
            _autoPosePendingApply = _settings != null && _settings.AutoPoseEnabled;
            _autoPoseLoopReady = false;
            _autoPoseLoopCountSinceSwitch = 0;
            _autoPoseLastAppliedPresetId = null;
            _autoMalePoseLastAppliedPresetId = null;

            if (_settings != null && _settings.DetailLogEnabled)
            {
                LogInfo("[PosePreset] posture context changed source=" + source
                    + " id=" + id
                    + " mode=" + mode
                    + " name=" + (name ?? string.Empty)
                    + " autoPending=" + _autoPosePendingApply);
            }
        }

        private void OnPostureContextCleared()
        {
            HandleFemaleHeadAngleContextChange("posture-context-cleared");
            ResetFemaleHandPoseRuntimeAndSettings(saveSettings: true);
            _autoPosePendingApply = false;
            _autoPoseLoopReady = false;
            _autoPoseLoopCountSinceSwitch = 0;
            _autoPoseLastAppliedPresetId = null;
            _autoMalePoseLastAppliedPresetId = null;
            _autoPoseLastAnimatorStateHash = 0;
            _autoPoseLastAnimatorLoop = 0;
        }

        private void ProcessAutoPoseRuntime()
        {
            if (_settings == null || !_settings.AutoPoseEnabled)
                return;
            if (!_hSceneSession.HasCurrentAnimationInfo)
                return;

            if (_autoPosePendingApply)
            {
                bool applied = TryApplyRandomAutoPoseSetForCurrentPosture("auto-posture-enter");
                if (!applied
                    && GetCurrentAnimatorStateHashForPoseContext() != 0
                    && CountAutoPoseCandidatesForCurrentPosture() <= 0)
                {
                    _autoPosePendingApply = false;
                }
            }

            UpdateAutoPoseLoopCounter();
        }

        private void UpdateAutoPoseLoopCounter()
        {
            Animator anim = _femaleSession.AnimationBody;
            if (anim == null)
                anim = _femaleSession.Character != null ? _femaleSession.Character.animBody : null;
            if (anim == null)
                return;

            AnimatorStateInfo stateInfo;
            try
            {
                stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            }
            catch
            {
                return;
            }

            int hash = stateInfo.fullPathHash;
            int loop = Mathf.Max(0, Mathf.FloorToInt(stateInfo.normalizedTime));

            if (!_autoPoseLoopReady || hash != _autoPoseLastAnimatorStateHash)
            {
                bool isMotionChange = _autoPoseLoopReady && hash != _autoPoseLastAnimatorStateHash;
                _autoPoseLoopReady = true;
                _autoPoseLastAnimatorStateHash = hash;
                _autoPoseLastAnimatorLoop = loop;
                _autoPoseLoopCountSinceSwitch = 0;

                if (isMotionChange && _settings != null && _settings.AutoPoseEnabled && _hSceneSession.HasCurrentAnimationInfo)
                {
                    RequestAbandonAllBodyIKByPostureChange("animator-state-changed");
                    OnPostureContextChanged(
                        _hSceneSession.CurrentAnimationInfoId,
                        _hSceneSession.CurrentAnimationInfoMode,
                        _hSceneSession.CurrentAnimationInfoName,
                        "animator-state-changed");
                }
                return;
            }

            if (loop <= _autoPoseLastAnimatorLoop)
                return;

            int delta = loop - _autoPoseLastAnimatorLoop;
            _autoPoseLastAnimatorLoop = loop;
            _autoPoseLoopCountSinceSwitch += delta;

            int candidateCount = CountAutoPoseCandidatesForCurrentPosture();
            if (candidateCount <= 1)
            {
                _autoPoseLoopCountSinceSwitch = 0;
                return;
            }

            int threshold = Mathf.Max(1, _settings.AutoPoseSwitchAnimationLoops);
            if (_settings.DetailLogEnabled)
            {
                LogInfo("[PosePreset] auto loop +" + delta
                    + " total=" + _autoPoseLoopCountSinceSwitch + "/" + threshold
                    + " posture=" + BuildCurrentPostureHint());
            }

            if (_autoPoseLoopCountSinceSwitch < threshold)
                return;

            _autoPoseLoopCountSinceSwitch = 0;
            TryApplyRandomAutoPoseSetForCurrentPosture("auto-loop-threshold");
        }

        private float GetPoseTransitionSeconds()
        {
            if (_settings == null)
                return 0.5f;

            return Mathf.Clamp(_settings.PoseTransitionSeconds, 0f, 5f);
        }

        private PoseTransitionEasing GetPoseTransitionEasing()
        {
            if (_settings == null)
                return PoseTransitionEasing.SmoothStep;

            PoseTransitionEasing easing = _settings.PoseTransitionEasing;
            if (!Enum.IsDefined(typeof(PoseTransitionEasing), easing))
                easing = PoseTransitionEasing.SmoothStep;
            return easing;
        }

        private float EvaluatePoseTransitionEasing(float t01)
        {
            return EasingFunctions.EvaluatePoseTransition(t01, (int)GetPoseTransitionEasing());
        }
    }
}
