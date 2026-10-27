using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        [Serializable]
        private sealed class MalePosePresetStore
        {
            public string format = MalePosePresetFormat;
            public MalePosePresetItem[] presets = new MalePosePresetItem[0];
        }

        [Serializable]
        private sealed class MalePosePresetItem
        {
            public string id;
            public string name;
            public string createdAt;
            public string screenshotFile;
            public int postureId = int.MinValue;
            public int postureMode = int.MinValue;
            public string postureName;
            public string postureStrength = PoseMotionStrengthUnknown;
            public int animatorStateHash;
            public bool autoApply;
            public MalePoseSnapshot snapshot = new MalePoseSnapshot();
        }

        [Serializable]
        private sealed class MalePoseSnapshot
        {
            public bool maleHmdEnabled;
            public bool maleHeadIkEnabled;
            public bool maleHeadIkGizmoEnabled;
            public bool maleHeadIkGizmoVisible = true;
            public int maleHeadBoneSelection;
            public float maleHeadIkPositionWeight = 0.75f;
            public float maleHeadIkNeckWeight = 0.8f;
            public float maleHmdHeadRotationWeight = 1f;
            public float maleHmdPositionScale = 1f;
            public bool maleHmdUseLocalDelta = true;
            public float maleHmdLocalDeltaSmoothing = 0.35f;
            public bool maleHmdSwapHorizontalAxes = true;
            public bool maleHmdInvertHorizontalX;
            public bool maleHmdInvertHorizontalZ;
            public bool maleIkDebugVisible = true;
            public bool maleHmdDiagnosticLog;
            public float maleHmdDiagnosticLogInterval = 0.25f;
            public bool[] maleIkEnabled = new bool[MALE_IK_BONE_TOTAL];
            public float[] maleIkWeights = new float[MALE_IK_BONE_TOTAL];
            public bool maleLeftHandFollowEnabled;
            public bool maleRightHandFollowEnabled;
            public float maleHandFollowSnapDistance = 0.2f;
            public string maleLeftHandFollowBonePath;
            public string maleRightHandFollowBonePath;
            public Vector3 maleLeftHandFollowPosOffset = Vector3.zero;
            public Vector3 maleRightHandFollowPosOffset = Vector3.zero;
            public Quaternion maleLeftHandFollowRotOffset = Quaternion.identity;
            public Quaternion maleRightHandFollowRotOffset = Quaternion.identity;
            public MalePoseControlSnapshot[] maleControls = new MalePoseControlSnapshot[BIK_TOTAL];
            public bool hasHeadTarget;
            public Vector3 headTargetPos = Vector3.zero;
            public Quaternion headTargetRot = Quaternion.identity;
        }

        [Serializable]
        private sealed class MalePoseControlSnapshot
        {
            public bool enabled;
            public bool gizmoVisible = true;
            public float weight = 1f;
            public bool hasProxy;
            public Vector3 proxyPos = Vector3.zero;
            public Quaternion proxyRot = Quaternion.identity;
        }

        private const string MalePosePresetFormat = "MainGirlHipHijackMalePosePresetStoreV1";
        private static readonly UTF8Encoding MalePosePresetUtf8NoBom = new UTF8Encoding(false);

        private void InitMalePosePresetStorage()
        {
            _malePosePresetRootDir = Path.Combine(_pluginDir, "pose_presets_male");
            _malePosePresetShotsDir = Path.Combine(_malePosePresetRootDir, "shots");
            _malePosePresetIndexPath = Path.Combine(_malePosePresetRootDir, "index.json");
            Directory.CreateDirectory(_malePosePresetRootDir);
            Directory.CreateDirectory(_malePosePresetShotsDir);
        }

        private void EnsureMalePosePresetsLoaded()
        {
            if (_malePosePresetsLoaded)
                return;

            ReloadMalePosePresetIndex();
            _malePosePresetsLoaded = true;
        }

        private void ReloadMalePosePresetIndex()
        {
            try
            {
                _malePosePresets.Clear();
                if (File.Exists(_malePosePresetIndexPath))
                {
                    string json = File.ReadAllText(_malePosePresetIndexPath, Encoding.UTF8);
                    MalePosePresetStore store = JsonUtility.FromJson<MalePosePresetStore>(json);
                    if (store != null && store.presets != null)
                    {
                        for (int i = 0; i < store.presets.Length; i++)
                        {
                            MalePosePresetItem item = store.presets[i];
                            if (item == null)
                                continue;
                            NormalizeMalePosePresetItem(item);
                            _malePosePresets.Add(item);
                        }
                    }
                }
                _malePosePresetThumbDirty = true;
                LogInfo("[MalePosePreset] loaded count=" + _malePosePresets.Count);
            }
            catch (Exception ex)
            {
                _malePosePresets.Clear();
                _malePosePresetThumbDirty = true;
                LogError("[MalePosePreset] index load failed: " + ex.Message);
            }
        }

        private void SaveMalePosePresetIndex()
        {
            try
            {
                var store = new MalePosePresetStore();
                store.presets = _malePosePresets.ToArray();
                string json = JsonUtility.ToJson(store, true) + Environment.NewLine;
                File.WriteAllText(_malePosePresetIndexPath, json, MalePosePresetUtf8NoBom);
            }
            catch (Exception ex)
            {
                LogError("[MalePosePreset] index save failed: " + ex.Message);
            }
        }

        private static void NormalizeMalePosePresetItem(MalePosePresetItem item)
        {
            if (item == null)
                return;

            if (string.IsNullOrEmpty(item.id))
                item.id = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            if (string.IsNullOrWhiteSpace(item.name))
                item.name = "male_pose";
            if (string.IsNullOrWhiteSpace(item.createdAt))
                item.createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (string.IsNullOrWhiteSpace(item.screenshotFile))
                item.screenshotFile = item.id + ".png";
            item.postureStrength = NormalizePoseMotionStrength(item.postureStrength);
            NormalizeMalePoseSnapshot(item.snapshot);
        }

        private static void NormalizeMalePoseSnapshot(MalePoseSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            snapshot.maleHeadIkPositionWeight = Mathf.Clamp01(snapshot.maleHeadIkPositionWeight);
            snapshot.maleHeadIkNeckWeight = Mathf.Clamp01(snapshot.maleHeadIkNeckWeight);
            snapshot.maleHmdHeadRotationWeight = Mathf.Clamp01(snapshot.maleHmdHeadRotationWeight);
            snapshot.maleHmdPositionScale = Mathf.Clamp(snapshot.maleHmdPositionScale, 0f, 5f);
            snapshot.maleHmdLocalDeltaSmoothing = Mathf.Clamp01(snapshot.maleHmdLocalDeltaSmoothing);
            snapshot.maleHmdDiagnosticLogInterval = Mathf.Clamp(snapshot.maleHmdDiagnosticLogInterval, 0.05f, 2f);
            snapshot.maleHeadBoneSelection = Mathf.Clamp(snapshot.maleHeadBoneSelection, 0, 2);
            snapshot.maleHandFollowSnapDistance = Mathf.Clamp(snapshot.maleHandFollowSnapDistance, 0.02f, 0.8f);

            if (snapshot.maleIkEnabled == null || snapshot.maleIkEnabled.Length != MALE_IK_BONE_TOTAL)
                snapshot.maleIkEnabled = new bool[MALE_IK_BONE_TOTAL];
            if (snapshot.maleIkWeights == null || snapshot.maleIkWeights.Length != MALE_IK_BONE_TOTAL)
            {
                snapshot.maleIkWeights = new float[MALE_IK_BONE_TOTAL];
                for (int i = 0; i < snapshot.maleIkWeights.Length; i++)
                    snapshot.maleIkWeights[i] = 1f;
            }
            for (int i = 0; i < snapshot.maleIkWeights.Length; i++)
                snapshot.maleIkWeights[i] = Mathf.Clamp01(snapshot.maleIkWeights[i]);

            if (snapshot.maleControls == null || snapshot.maleControls.Length != BIK_TOTAL)
            {
                var fixedControls = new MalePoseControlSnapshot[BIK_TOTAL];
                for (int i = 0; i < BIK_TOTAL; i++)
                {
                    fixedControls[i] = snapshot.maleControls != null && i < snapshot.maleControls.Length && snapshot.maleControls[i] != null
                        ? snapshot.maleControls[i]
                        : new MalePoseControlSnapshot();
                }
                snapshot.maleControls = fixedControls;
            }
            else
            {
                for (int i = 0; i < snapshot.maleControls.Length; i++)
                {
                    if (snapshot.maleControls[i] == null)
                        snapshot.maleControls[i] = new MalePoseControlSnapshot();
                }
            }
            for (int i = 0; i < snapshot.maleControls.Length; i++)
                snapshot.maleControls[i].weight = Mathf.Clamp01(snapshot.maleControls[i].weight);
        }

        private string BuildNumberedMalePosePresetName(string requestedName)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName) ? "male_pose" : requestedName.Trim();
            int next = 1;

            for (int i = 0; i < _malePosePresets.Count; i++)
            {
                MalePosePresetItem preset = _malePosePresets[i];
                if (preset == null || string.IsNullOrEmpty(preset.name))
                    continue;

                int n;
                if (!TryParsePresetSequence(preset.name, baseName, out n))
                    continue;
                if (n >= next)
                    next = n + 1;
            }

            return baseName + "_" + next.ToString("000");
        }

        private void CaptureCurrentPostureForMalePose(MalePosePresetItem item)
        {
            if (item == null)
                return;

            if (_hSceneSession.HasCurrentAnimationInfo)
            {
                item.postureId = _hSceneSession.CurrentAnimationInfoId;
                item.postureMode = _hSceneSession.CurrentAnimationInfoMode;
                item.postureName = _hSceneSession.CurrentAnimationInfoName;
                item.postureStrength = GetCurrentMotionStrengthTagForPoseContext();
                item.animatorStateHash = GetCurrentAnimatorStateHashForPoseContext();
            }
            else
            {
                item.postureId = int.MinValue;
                item.postureMode = int.MinValue;
                item.postureName = null;
                item.postureStrength = PoseMotionStrengthUnknown;
                item.animatorStateHash = 0;
            }
        }

        private bool IsCurrentPostureMatch(MalePosePresetItem preset)
        {
            if (preset == null)
                return false;

            return IsCurrentPostureMatch(
                preset.postureId,
                preset.postureMode,
                preset.postureName,
                preset.postureStrength,
                preset.animatorStateHash);
        }

        private static string BuildMalePosePostureHint(MalePosePresetItem preset)
        {
            if (preset == null)
                return "id=(none), mode=(none), name=(none), strength=(none), anim=(none)";

            return BuildPostureHint(preset.postureId, preset.postureMode, preset.postureName, preset.postureStrength, preset.animatorStateHash);
        }

        private string GetMalePosePresetScreenshotPath(MalePosePresetItem preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.screenshotFile))
                return null;

            return Path.Combine(_malePosePresetShotsDir, preset.screenshotFile);
        }

        private MalePoseSnapshot BuildCurrentMalePoseSnapshot()
        {
            ResolveMaleRefs();
            var snap = new MalePoseSnapshot();
            Transform followRoot = GetPosePresetRootTransform();

            if (_settings != null)
            {
                snap.maleHmdEnabled = _settings.MaleHmdEnabled;
                snap.maleHeadIkEnabled = _settings.MaleHeadIkEnabled;
                snap.maleHeadIkGizmoEnabled = _settings.MaleHeadIkGizmoEnabled;
                snap.maleHeadIkGizmoVisible = _settings.MaleHeadIkGizmoVisible;
                snap.maleHeadBoneSelection = Mathf.Clamp((int)_settings.MaleHeadBoneSelection, 0, 2);
                snap.maleHeadIkPositionWeight = _settings.MaleHeadIkPositionWeight;
                snap.maleHeadIkNeckWeight = _settings.MaleHeadIkNeckWeight;
                snap.maleHmdHeadRotationWeight = _settings.MaleHmdHeadRotationWeight;
                snap.maleHmdPositionScale = _settings.MaleHmdPositionScale;
                snap.maleHmdUseLocalDelta = _settings.MaleHmdUseLocalDelta;
                snap.maleHmdLocalDeltaSmoothing = _settings.MaleHmdLocalDeltaSmoothing;
                snap.maleHmdSwapHorizontalAxes = _settings.MaleHmdSwapHorizontalAxes;
                snap.maleHmdInvertHorizontalX = _settings.MaleHmdInvertHorizontalX;
                snap.maleHmdInvertHorizontalZ = _settings.MaleHmdInvertHorizontalZ;
                snap.maleIkDebugVisible = _settings.MaleIkDebugVisible;
                snap.maleHmdDiagnosticLog = _settings.MaleHmdDiagnosticLog;
                snap.maleHmdDiagnosticLogInterval = _settings.MaleHmdDiagnosticLogInterval;
                snap.maleLeftHandFollowEnabled = _settings.MaleLeftHandFollowEnabled;
                snap.maleRightHandFollowEnabled = _settings.MaleRightHandFollowEnabled;
                snap.maleHandFollowSnapDistance = _settings.MaleHandFollowSnapDistance;
            }

            for (int i = 0; i < MALE_IK_BONE_TOTAL; i++)
            {
                snap.maleIkEnabled[i] = _settings != null && _settings.MaleIkEnabled != null && i < _settings.MaleIkEnabled.Length && _settings.MaleIkEnabled[i];
                snap.maleIkWeights[i] = _settings != null && _settings.MaleIkWeights != null && i < _settings.MaleIkWeights.Length
                    ? Mathf.Clamp01(_settings.MaleIkWeights[i])
                    : 1f;
            }

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                var item = new MalePoseControlSnapshot
                {
                    enabled = GetMaleControlEnabled(i),
                    gizmoVisible = GetMaleControlGizmoVisible(i),
                    weight = GetMaleControlWeight(i)
                };

                MaleControlState state = _maleControlStates[i];
                Transform bone = GetMaleControlBoneByIndex(i);
                if (state != null && state.Proxy != null)
                {
                    item.hasProxy = true;
                    item.proxyPos = state.Proxy.position;
                    item.proxyRot = state.Proxy.rotation;
                }
                else if (bone != null)
                {
                    item.hasProxy = true;
                    item.proxyPos = bone.position;
                    item.proxyRot = bone.rotation;
                }

                snap.maleControls[i] = item;
            }

            if (_maleSession.MaleHeadTarget != null)
            {
                snap.hasHeadTarget = true;
                snap.headTargetPos = _maleSession.MaleHeadTarget.position;
                snap.headTargetRot = _maleSession.MaleHeadTarget.rotation;
            }

            if (_maleSession.MaleLeftHandFollowBone != null && followRoot != null)
                snap.maleLeftHandFollowBonePath = BuildRelativePath(followRoot, _maleSession.MaleLeftHandFollowBone);
            if (_maleSession.MaleRightHandFollowBone != null && followRoot != null)
                snap.maleRightHandFollowBonePath = BuildRelativePath(followRoot, _maleSession.MaleRightHandFollowBone);
            snap.maleLeftHandFollowPosOffset = _maleSession.MaleLeftHandFollowPosOffset;
            snap.maleRightHandFollowPosOffset = _maleSession.MaleRightHandFollowPosOffset;
            snap.maleLeftHandFollowRotOffset = _maleSession.MaleLeftHandFollowRotOffset;
            snap.maleRightHandFollowRotOffset = _maleSession.MaleRightHandFollowRotOffset;

            NormalizeMalePoseSnapshot(snap);
            return snap;
        }

        private void ApplyMalePoseSnapshot(MalePoseSnapshot snap)
        {
            if (_settings == null || snap == null)
                return;

            NormalizeMalePoseSnapshot(snap);

            _settings.MaleHmdEnabled = snap.maleHmdEnabled;
            _settings.MaleHeadIkEnabled = snap.maleHeadIkEnabled;
            _settings.MaleHeadIkGizmoEnabled = snap.maleHeadIkGizmoEnabled;
            _settings.MaleHeadIkGizmoVisible = snap.maleHeadIkGizmoVisible;
            _settings.MaleHeadBoneSelection = (MaleHeadBoneSelectionMode)snap.maleHeadBoneSelection;
            _settings.MaleHeadIkPositionWeight = snap.maleHeadIkPositionWeight;
            _settings.MaleHeadIkNeckWeight = snap.maleHeadIkNeckWeight;
            _settings.MaleHmdHeadRotationWeight = snap.maleHmdHeadRotationWeight;
            _settings.MaleHmdPositionScale = snap.maleHmdPositionScale;
            _settings.MaleHmdUseLocalDelta = snap.maleHmdUseLocalDelta;
            _settings.MaleHmdLocalDeltaSmoothing = snap.maleHmdLocalDeltaSmoothing;
            _settings.MaleHmdSwapHorizontalAxes = snap.maleHmdSwapHorizontalAxes;
            _settings.MaleHmdInvertHorizontalX = snap.maleHmdInvertHorizontalX;
            _settings.MaleHmdInvertHorizontalZ = snap.maleHmdInvertHorizontalZ;
            _settings.MaleIkDebugVisible = snap.maleIkDebugVisible;
            _settings.MaleHmdDiagnosticLog = snap.maleHmdDiagnosticLog;
            _settings.MaleHmdDiagnosticLogInterval = snap.maleHmdDiagnosticLogInterval;
            _settings.MaleLeftHandFollowEnabled = snap.maleLeftHandFollowEnabled;
            _settings.MaleRightHandFollowEnabled = snap.maleRightHandFollowEnabled;
            _settings.MaleHandFollowSnapDistance = snap.maleHandFollowSnapDistance;

            if (_settings.MaleIkEnabled == null || _settings.MaleIkEnabled.Length != MALE_IK_BONE_TOTAL)
                _settings.MaleIkEnabled = new bool[MALE_IK_BONE_TOTAL];
            if (_settings.MaleIkWeights == null || _settings.MaleIkWeights.Length != MALE_IK_BONE_TOTAL)
                _settings.MaleIkWeights = new float[MALE_IK_BONE_TOTAL];
            for (int i = 0; i < MALE_IK_BONE_TOTAL; i++)
            {
                _settings.MaleIkEnabled[i] = snap.maleIkEnabled[i];
                _settings.MaleIkWeights[i] = snap.maleIkWeights[i];
            }

            if (_settings.MaleControlEnabled == null || _settings.MaleControlEnabled.Length != BIK_TOTAL)
                _settings.MaleControlEnabled = new bool[BIK_TOTAL];
            if (_settings.MaleControlGizmoVisible == null || _settings.MaleControlGizmoVisible.Length != BIK_TOTAL)
            {
                _settings.MaleControlGizmoVisible = new bool[BIK_TOTAL];
                for (int i = 0; i < _settings.MaleControlGizmoVisible.Length; i++)
                    _settings.MaleControlGizmoVisible[i] = true;
            }
            if (_settings.MaleControlWeights == null || _settings.MaleControlWeights.Length != BIK_TOTAL)
            {
                _settings.MaleControlWeights = new float[BIK_TOTAL];
                for (int i = 0; i < _settings.MaleControlWeights.Length; i++)
                    _settings.MaleControlWeights[i] = 1f;
            }
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                MalePoseControlSnapshot item = snap.maleControls[i] ?? new MalePoseControlSnapshot();
                _settings.MaleControlEnabled[i] = item.enabled;
                _settings.MaleControlGizmoVisible[i] = item.gizmoVisible;
                _settings.MaleControlWeights[i] = item.weight;
            }

            _maleSession.MaleHeadBoneSelectionCached = int.MinValue;
            _maleSession.MaleHeadBone = null;
            _maleSession.MaleHeadBoneName = null;
            ResolveMaleRefs();

            if (_settings.MaleHeadIkGizmoEnabled)
            {
                EnsureMaleHeadTargetGizmo();
                if (snap.hasHeadTarget && _maleSession.MaleHeadTarget != null)
                    _maleSession.MaleHeadTarget.SetPositionAndRotation(snap.headTargetPos, snap.headTargetRot);
                else
                    SnapMaleHeadTargetToCurrentHead();
            }
            else
            {
                DestroyMaleHeadTargetGizmo();
            }

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_settings.MaleControlEnabled[i])
                {
                    UpdateMaleControlGizmoVisibility(i);
                    continue;
                }
                EnsureMaleControlProxy(i);
                MalePoseControlSnapshot item = snap.maleControls[i] ?? new MalePoseControlSnapshot();
                MaleControlState state = _maleControlStates[i];
                if (item.hasProxy && state != null && state.Proxy != null)
                    state.Proxy.SetPositionAndRotation(item.proxyPos, item.proxyRot);
                UpdateMaleControlGizmoVisibility(i);
            }

            Transform followRoot = GetPosePresetRootTransform();
            _maleSession.MaleLeftHandFollowBone = null;
            _maleSession.MaleRightHandFollowBone = null;
            if (_settings.MaleLeftHandFollowEnabled && followRoot != null && !string.IsNullOrEmpty(snap.maleLeftHandFollowBonePath))
                _maleSession.MaleLeftHandFollowBone = FindByRelativePath(followRoot, snap.maleLeftHandFollowBonePath);
            if (_settings.MaleRightHandFollowEnabled && followRoot != null && !string.IsNullOrEmpty(snap.maleRightHandFollowBonePath))
                _maleSession.MaleRightHandFollowBone = FindByRelativePath(followRoot, snap.maleRightHandFollowBonePath);
            _maleSession.MaleLeftHandFollowPosOffset = snap.maleLeftHandFollowPosOffset;
            _maleSession.MaleRightHandFollowPosOffset = snap.maleRightHandFollowPosOffset;
            _maleSession.MaleLeftHandFollowRotOffset = snap.maleLeftHandFollowRotOffset;
            _maleSession.MaleRightHandFollowRotOffset = snap.maleRightHandFollowRotOffset;

            _maleSession.HasMaleHmdBaseline = false;
            _maleSession.HasMaleHmdLocalDelta = false;
            SaveSettings();
        }

        private void SaveCurrentMalePosePresetWithScreenshot(string requestedName)
        {
            EnsureMalePosePresetsLoaded();
            ResolveMaleRefs();

            DateTime now = DateTime.Now;
            string id = now.ToString("yyyyMMdd_HHmmss_fff");
            var preset = new MalePosePresetItem
            {
                id = id,
                name = BuildNumberedMalePosePresetName(requestedName),
                createdAt = now.ToString("yyyy-MM-dd HH:mm:ss"),
                screenshotFile = id + ".png",
                autoApply = false,
                snapshot = BuildCurrentMalePoseSnapshot()
            };
            CaptureCurrentPostureForMalePose(preset);

            _malePosePresets.Insert(0, preset);
            SaveMalePosePresetIndex();
            _malePosePresetThumbDirty = true;
            StartCoroutine(CaptureMalePosePresetScreenshotCoroutine(preset));
            LogInfo("[MalePosePreset] saved id=" + preset.id + " name=" + preset.name + " posture=" + BuildMalePosePostureHint(preset));
        }

        private bool ApplyMalePosePresetById(string id, string reason)
        {
            EnsureMalePosePresetsLoaded();
            if (string.IsNullOrEmpty(id))
                return false;

            for (int i = 0; i < _malePosePresets.Count; i++)
            {
                MalePosePresetItem preset = _malePosePresets[i];
                if (preset == null || !string.Equals(preset.id, id, StringComparison.Ordinal))
                    continue;

                ApplyMalePoseSnapshot(preset.snapshot);
                _autoMalePoseLastAppliedPresetId = preset.id;
                LogInfo("[MalePosePreset] applied id=" + preset.id + " name=" + preset.name + " reason=" + reason);
                return true;
            }

            LogWarn("[MalePosePreset] apply failed: not found id=" + id);
            return false;
        }

        private void OverwriteMalePosePresetById(string id)
        {
            EnsureMalePosePresetsLoaded();
            if (string.IsNullOrEmpty(id))
                return;

            for (int i = 0; i < _malePosePresets.Count; i++)
            {
                MalePosePresetItem preset = _malePosePresets[i];
                if (preset == null || !string.Equals(preset.id, id, StringComparison.Ordinal))
                    continue;

                preset.snapshot = BuildCurrentMalePoseSnapshot();
                CaptureCurrentPostureForMalePose(preset);
                _malePosePresets[i] = preset;
                SaveMalePosePresetIndex();
                _malePosePresetThumbDirty = true;
                StartCoroutine(CaptureMalePosePresetScreenshotCoroutine(preset));
                LogInfo("[MalePosePreset] overwritten id=" + id);
                return;
            }
        }

        private void DeleteMalePosePresetById(string id)
        {
            EnsureMalePosePresetsLoaded();
            if (string.IsNullOrEmpty(id))
                return;

            int removeIndex = -1;
            MalePosePresetItem preset = null;
            for (int i = 0; i < _malePosePresets.Count; i++)
            {
                if (_malePosePresets[i] == null || !string.Equals(_malePosePresets[i].id, id, StringComparison.Ordinal))
                    continue;
                removeIndex = i;
                preset = _malePosePresets[i];
                break;
            }

            if (removeIndex < 0 || preset == null)
                return;

            _malePosePresets.RemoveAt(removeIndex);
            SaveMalePosePresetIndex();

            Texture2D tex;
            if (_malePosePresetThumbCache.TryGetValue(id, out tex) && tex != null)
                Destroy(tex);
            _malePosePresetThumbCache.Remove(id);

            string shotPath = GetMalePosePresetScreenshotPath(preset);
            try
            {
                if (!string.IsNullOrEmpty(shotPath) && File.Exists(shotPath))
                    File.Delete(shotPath);
            }
            catch (Exception ex)
            {
                LogError("[MalePosePreset] screenshot delete failed: " + ex.Message);
            }

            _malePosePresetThumbDirty = true;
            LogInfo("[MalePosePreset] deleted id=" + id);
        }

        private IEnumerator CaptureMalePosePresetScreenshotCoroutine(MalePosePresetItem preset)
        {
            if (preset == null)
                yield break;

            yield return new WaitForEndOfFrame();

            string shotPath = GetMalePosePresetScreenshotPath(preset);
            if (string.IsNullOrEmpty(shotPath))
                yield break;

            try
            {
                ScreenCapture.CaptureScreenshot(shotPath);
            }
            catch (Exception ex)
            {
                LogError("[MalePosePreset] screenshot capture failed: " + ex.Message);
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
                }

                yield return null;
            }

            _malePosePresetThumbDirty = true;
            LogInfo("[MalePosePreset] screenshot saved path=" + shotPath);
        }

        private Texture2D GetMalePosePresetThumbnail(MalePosePresetItem preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.id))
                return null;

            Texture2D cached;
            if (_malePosePresetThumbCache.TryGetValue(preset.id, out cached))
                return cached;

            string shotPath = GetMalePosePresetScreenshotPath(preset);
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
                _malePosePresetThumbCache[preset.id] = tex;
                return tex;
            }
            catch (Exception ex)
            {
                LogError("[MalePosePreset] thumbnail load failed: " + ex.Message);
                return null;
            }
        }

        private void RefreshMalePosePresetThumbCacheIfNeeded()
        {
            if (!_malePosePresetThumbDirty)
                return;

            DisposeMalePosePresetThumbCache();
            _malePosePresetThumbDirty = false;
        }

        private void DisposeMalePosePresetThumbCache()
        {
            foreach (var kv in _malePosePresetThumbCache)
            {
                if (kv.Value != null)
                    Destroy(kv.Value);
            }

            _malePosePresetThumbCache.Clear();
        }

        private void HandleMalePosePresetThumbnailClick(MalePosePresetItem preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.id))
                return;

            float now = Time.unscaledTime;
            bool isDouble = string.Equals(_lastMaleThumbClickedPresetId, preset.id, StringComparison.Ordinal)
                && (now - _lastMaleThumbClickTime) <= PoseThumbnailDoubleClickWindow;

            _lastMaleThumbClickedPresetId = preset.id;
            _lastMaleThumbClickTime = now;

            if (!isDouble)
                return;

            ApplyMalePosePresetById(preset.id, "thumb-double-click");
        }

        private void DrawMalePosePresetSection()
        {
            EnsureMalePosePresetsLoaded();
            RefreshMalePosePresetThumbCacheIfNeeded();

            GUILayout.Space(8f);
            GUILayout.Label("── 男ポーズ保存/読込（スクショ付き） ──");
            GUILayout.Label("現在体位: " + BuildCurrentPostureHint());
            GUILayout.Label("サムネをダブルクリックで読込");

            GUILayout.BeginHorizontal();
            GUILayout.Label("名前", GUILayout.Width(38f));
            _malePosePresetNameDraft = GUILayout.TextField(_malePosePresetNameDraft ?? string.Empty, GUILayout.Width(160f));
            if (GUILayout.Button("男保存+撮影", GUILayout.Width(92f)))
                SaveCurrentMalePosePresetWithScreenshot(_malePosePresetNameDraft);
            if (GUILayout.Button("男再読込", GUILayout.Width(70f)))
                ReloadMalePosePresetIndex();
            GUILayout.EndHorizontal();

            bool anyVisible = false;
            for (int i = 0; i < _malePosePresets.Count; i++)
            {
                MalePosePresetItem preset = _malePosePresets[i];
                if (preset == null)
                    continue;
                bool exactMotion = IsCurrentPostureMatch(preset);
                bool legacyMotion = IsLegacyMalePosePresetForCurrentPosture(preset);
                if (!exactMotion && !legacyMotion)
                    continue;

                anyVisible = true;
                GUILayout.BeginHorizontal("box");

                Texture2D thumb = GetMalePosePresetThumbnail(preset);
                GUIContent thumbContent = thumb != null ? new GUIContent(thumb) : new GUIContent("No\nImage");
                if (GUILayout.Button(thumbContent, GUILayout.Width(96f), GUILayout.Height(54f)))
                    HandleMalePosePresetThumbnailClick(preset);

                GUILayout.BeginVertical(GUILayout.Width(230f));
                GUILayout.Label(string.IsNullOrEmpty(preset.name) ? "<no name>" : preset.name);
                GUILayout.Label(string.IsNullOrEmpty(preset.createdAt) ? "<no time>" : preset.createdAt);
                GUILayout.Label("体位: " + BuildMalePosePostureHint(preset));
                if (legacyMotion)
                    GUILayout.Label("未紐付け: 現在モーションに紐付け可");
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUILayout.Width(72f));
                bool auto = GUILayout.Toggle(preset.autoApply, "auto");
                if (auto != preset.autoApply)
                {
                    preset.autoApply = auto;
                    SaveMalePosePresetIndex();
                }
                if (GUILayout.Button("読込", GUILayout.Width(60f)))
                    ApplyMalePosePresetById(preset.id, "ui-load");
                if (legacyMotion && GUILayout.Button("紐付け", GUILayout.Width(60f)))
                    BindMalePosePresetToCurrentMotion(preset);
                if (GUILayout.Button("上書き", GUILayout.Width(60f)))
                    OverwriteMalePosePresetById(preset.id);
                if (GUILayout.Button("削除", GUILayout.Width(60f)))
                    DeleteMalePosePresetById(preset.id);
                GUILayout.EndVertical();

                GUILayout.EndHorizontal();
            }

            if (!anyVisible)
                GUILayout.Label("この体位+モーションの保存済み男ポーズなし");
        }
    }
}
