using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MainGameCameraControl
{
    internal sealed partial class Plugin
    {
        private string _lastPoseKey = string.Empty;
        private string _lastPoseDisplayName = string.Empty;
        private bool _poseTrackingInitialized;
        private HSceneProc _poseScene;
        private float _nextPoseSceneLookupTime;
        private HSceneProc.AnimationListInfo _cachedPoseInfo;
        private HFlag.EMode _cachedPoseMode;
        private int _cachedPoseId;
        private string _cachedPoseName;
        private string _cachedPoseKey = string.Empty;
        private string _cachedPoseDisplayName = string.Empty;

        private bool TryGetCurrentPoseInfo(out string key, out string displayName)
        {
            key = string.Empty;
            displayName = string.Empty;
            try
            {
                if (_poseScene == null && Time.unscaledTime >= _nextPoseSceneLookupTime)
                {
                    _nextPoseSceneLookupTime = Time.unscaledTime + 0.5f;
                    _poseScene = UnityEngine.Object.FindObjectOfType<HSceneProc>();
                }
                HSceneProc proc = _poseScene;
                if (proc == null || proc.flags == null || proc.flags.nowAnimationInfo == null)
                {
                    _cachedPoseInfo = null;
                    return false;
                }

                HSceneProc.AnimationListInfo info = proc.flags.nowAnimationInfo;
                if (!ReferenceEquals(_cachedPoseInfo, info) || _cachedPoseMode != info.mode ||
                    _cachedPoseId != info.id || _cachedPoseName != info.nameAnimation)
                {
                    _cachedPoseInfo = info;
                    _cachedPoseMode = info.mode;
                    _cachedPoseId = info.id;
                    _cachedPoseName = info.nameAnimation;
                    _cachedPoseKey = info.mode.ToString() + ":" + info.id.ToString();
                    _cachedPoseDisplayName = string.IsNullOrWhiteSpace(info.nameAnimation) ? _cachedPoseKey : info.nameAnimation;
                }
                key = _cachedPoseKey;
                displayName = _cachedPoseDisplayName;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private CameraPreset FindPresetByName(string name)
        {
            string normalized = (name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
                return null;

            return _presets.FirstOrDefault(x =>
                x != null
                && !string.IsNullOrWhiteSpace(x.Name)
                && string.Equals(x.Name.Trim(), normalized, StringComparison.Ordinal));
        }

        private static CameraPoseOverride FindCameraPoseOverride(CameraPreset preset, string key)
        {
            if (preset == null || string.IsNullOrWhiteSpace(key))
                return null;

            CameraPoseOverride[] overrides = preset.PoseOverrides ?? new CameraPoseOverride[0];
            for (int i = 0; i < overrides.Length; i++)
            {
                CameraPoseOverride entry = overrides[i];
                if (entry != null && string.Equals(entry.Key, key, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        private static float[] CloneFloatArray(float[] values)
        {
            if (values == null)
                return null;

            float[] copy = new float[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }

        private static CameraPreset ClonePreset(CameraPreset preset)
        {
            if (preset == null)
                return null;

            return new CameraPreset
            {
                Name = preset.Name,
                TargetPosition = CloneFloatArray(preset.TargetPosition),
                CameraDirection = CloneFloatArray(preset.CameraDirection),
                Rotation = CloneFloatArray(preset.Rotation),
                Fov = preset.Fov,
                QuickCycleEnabled = preset.QuickCycleEnabled,
                UseBoneLink = preset.UseBoneLink,
                BoneTarget = preset.BoneTarget,
                BoneName = preset.BoneName,
                LookAtOffsetLocal = CloneFloatArray(preset.LookAtOffsetLocal),
                LookAtWorldY = preset.LookAtWorldY,
                SaveCameraPosition = preset.SaveCameraPosition,
                CameraOffsetLocal = CloneFloatArray(preset.CameraOffsetLocal),
                CameraOffsetBonePos = CloneFloatArray(preset.CameraOffsetBonePos),
                CameraOffsetWorld = CloneFloatArray(preset.CameraOffsetWorld),
                RotationOffset = CloneFloatArray(preset.RotationOffset),
                LegacyPosition = CloneFloatArray(preset.LegacyPosition),
                LegacyLookAt = CloneFloatArray(preset.LegacyLookAt),
                LegacyDistance = preset.LegacyDistance,
                UsePoseOverrides = preset.UsePoseOverrides,
                PoseOverrides = preset.PoseOverrides,

                // まず基底を複製し、体位に相対方向の保存値があれば解決時に差し替える。
                // 旧データには体位別の相対方向がないため、基底を互換用に残す。
                UseRelativeDirection = preset.UseRelativeDirection,
                RelativeFromLookAt = preset.RelativeFromLookAt,
                RelativeDirLocal = CloneFloatArray(preset.RelativeDirLocal),
                RelativeDistance = preset.RelativeDistance,
                RollWithBone = preset.RollWithBone,
                FrameBoneName = preset.FrameBoneName
            };
        }

        // 呼び出し時、現在の体位に一致するオーバーライドがあればその値で上書きしたクローンを返す（Name は保持）
        private CameraPreset ResolveCameraPresetForCurrentPose(CameraPreset preset)
        {
            if (preset == null || !preset.UsePoseOverrides)
                return preset;

            if (!TryGetCurrentPoseInfo(out string key, out _))
                return preset;

            CameraPoseOverride pose = FindCameraPoseOverride(preset, key);
            if (pose == null)
                return preset;

            CameraPreset resolved = ClonePreset(preset);
            resolved.UseBoneLink = pose.UseBoneLink;
            resolved.BoneTarget = pose.BoneTarget;
            resolved.BoneName = pose.BoneName;
            resolved.TargetPosition = CloneFloatArray(pose.TargetPosition) ?? resolved.TargetPosition;
            resolved.CameraDirection = CloneFloatArray(pose.CameraDirection) ?? resolved.CameraDirection;
            resolved.Rotation = CloneFloatArray(pose.Rotation) ?? resolved.Rotation;
            resolved.LookAtOffsetLocal = CloneFloatArray(pose.LookAtOffsetLocal) ?? resolved.LookAtOffsetLocal;
            resolved.LookAtWorldY = pose.LookAtWorldY;
            resolved.Fov = pose.Fov > 0f ? pose.Fov : resolved.Fov;

            // 方向・距離・基準点・ロールを同じ保存時点の組として復元する。
            // 明示OFFも反映し、古い共通の相対方向が体位の保存位置を上書きしないようにする。
            if (pose.UseRelativeDirection.HasValue)
            {
                resolved.UseRelativeDirection = pose.UseRelativeDirection.Value;
                resolved.RelativeDirLocal = CloneFloatArray(pose.RelativeDirLocal);
                resolved.RelativeDistance = pose.RelativeDistance;
                resolved.RelativeFromLookAt = pose.RelativeFromLookAt;
                resolved.RollWithBone = pose.RollWithBone;
                resolved.FrameBoneName = pose.FrameBoneName;
            }

            // ---- カメラ位置は体位を最優先。位置とボーンは必ず同じ出所に揃える ----
            // 体位はボーン自体(BoneName/BoneTarget)も差し替えるので、基底のオフセット(別ボーン基準)を
            // 体位のボーンに当てると bone.TransformPoint(別ボーン用オフセット) になって着地点が暴れる。
            // （これが「呼んだ瞬間に場所が違う」「顔の時すごく下がる」の正体。）
            // 1) 体位が自前の位置を持つ → 基底を完全無視して体位の位置を使う。
            // 2) 体位に位置が無く、ボーンが基底と同一 → 基底の位置を使ってよい（同じボーン基準）。
            // 3) 体位に位置が無く、ボーンも基底と違う → 別ボーン基準の基底オフセットは使えないので
            //    位置は使わず注視追従のみ（暴れる着地を防ぐ）。
            string posSource;
            bool sameBoneAsBase = string.Equals(resolved.BoneName ?? string.Empty, preset.BoneName ?? string.Empty, StringComparison.Ordinal)
                && resolved.BoneTarget == preset.BoneTarget;
            if (PoseHasStoredOffset(pose))
            {
                resolved.SaveCameraPosition = pose.SaveCameraPosition;
                resolved.CameraOffsetLocal = CloneFloatArray(pose.CameraOffsetLocal);
                resolved.CameraOffsetBonePos = CloneFloatArray(pose.CameraOffsetBonePos);
                resolved.CameraOffsetWorld = CloneFloatArray(pose.CameraOffsetWorld);
                posSource = "pose";
            }
            else if (sameBoneAsBase && PresetHasStoredOffset(preset))
            {
                resolved.SaveCameraPosition = preset.SaveCameraPosition;
                resolved.CameraOffsetLocal = CloneFloatArray(preset.CameraOffsetLocal);
                resolved.CameraOffsetBonePos = CloneFloatArray(preset.CameraOffsetBonePos);
                resolved.CameraOffsetWorld = CloneFloatArray(preset.CameraOffsetWorld);
                posSource = "base(same-bone)";
            }
            else
            {
                resolved.SaveCameraPosition = false;
                resolved.CameraOffsetLocal = null;
                resolved.CameraOffsetBonePos = null;
                resolved.CameraOffsetWorld = null;
                posSource = "none(bone-mismatch)";
            }

            LogInfo("[pose-resolve] preset=" + (preset.Name ?? string.Empty)
                + " key=" + key
                + " display=" + (pose.DisplayName ?? string.Empty)
                + " posSource=" + posSource
                + " usePos=" + HasPresetCameraPosition(resolved)
                + " bone=" + (resolved.BoneName ?? "?") + "/" + resolved.BoneTarget
                + " baseBone=" + (preset.BoneName ?? "?") + "/" + preset.BoneTarget
                + " offsetLocal=" + (resolved.CameraOffsetLocal != null ? ToVector3(resolved.CameraOffsetLocal).ToString("0.####") : "null")
                + " fov=" + resolved.Fov.ToString("0.##"));
            return resolved;
        }

        // 現在のカメラ状態を、指定プリセットの現在体位オーバーライドとして記録する
        private bool CapturePoseOverrideToPreset(CameraPreset preset, string key, string displayName)
        {
            if (preset == null || string.IsNullOrWhiteSpace(key))
                return false;

            CameraPreset snapshot = BuildCurrentPresetSnapshot(preset.Name ?? string.Empty);
            if (snapshot == null)
                return false;

            CameraPoseOverride pose = FindCameraPoseOverride(preset, key);
            if (pose == null)
            {
                List<CameraPoseOverride> list = new List<CameraPoseOverride>(preset.PoseOverrides ?? new CameraPoseOverride[0]);
                pose = new CameraPoseOverride { Key = key };
                list.Add(pose);
                preset.PoseOverrides = list.ToArray();
            }

            pose.DisplayName = displayName ?? string.Empty;
            pose.UseBoneLink = snapshot.UseBoneLink;
            pose.BoneTarget = snapshot.BoneTarget;
            pose.BoneName = snapshot.BoneName;
            pose.SaveCameraPosition = snapshot.SaveCameraPosition;
            pose.TargetPosition = CloneFloatArray(snapshot.TargetPosition);
            pose.CameraDirection = CloneFloatArray(snapshot.CameraDirection);
            pose.Rotation = CloneFloatArray(snapshot.Rotation);
            pose.LookAtOffsetLocal = CloneFloatArray(snapshot.LookAtOffsetLocal);
            pose.LookAtWorldY = snapshot.LookAtWorldY;
            pose.CameraOffsetLocal = CloneFloatArray(snapshot.CameraOffsetLocal);
            pose.CameraOffsetBonePos = CloneFloatArray(snapshot.CameraOffsetBonePos);
            pose.CameraOffsetWorld = CloneFloatArray(snapshot.CameraOffsetWorld);
            pose.UseRelativeDirection = snapshot.UseRelativeDirection;
            pose.RelativeDirLocal = CloneFloatArray(snapshot.RelativeDirLocal);
            pose.RelativeDistance = snapshot.RelativeDistance;
            pose.RelativeFromLookAt = snapshot.RelativeFromLookAt;
            pose.RollWithBone = snapshot.RollWithBone;
            pose.FrameBoneName = snapshot.FrameBoneName;
            pose.Fov = snapshot.Fov;
            LogInfo("pose override save preset=" + (preset.Name ?? string.Empty)
                + " key=" + key
                + " display=" + pose.DisplayName
                + " bone=" + pose.UseBoneLink + ":" + pose.BoneTarget
                + " fov=" + pose.Fov.ToString("0.##"));
            return true;
        }

        // 現在の保存モードに従って現在カメラのスナップショット CameraPreset を作る（保存ロジックの再利用）
        private CameraPreset BuildCurrentPresetSnapshot(string name)
        {
            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
                return null;

            BaseCameraControl_Ver2.CameraData data = _ctrlCamera.GetCameraData();
            data.Fov = ResolveCurrentBaseFov(data.Fov);

            switch (_selectedSaveMode)
            {
                case SaveModeBoneLink:
                    return BuildBoneLinkedPreset(name, data);
                default:
                    return new CameraPreset
                    {
                        Name = name,
                        TargetPosition = ToArray(data.Pos),
                        CameraDirection = ToArray(data.Dir),
                        Rotation = ToArray(data.Rot),
                        Fov = data.Fov
                    };
            }
        }

        // UI「体位を上書き」: アクティブプリセットの現在体位オーバーライドを保存
        private void CapturePoseOverrideForActivePreset()
        {
            CameraPreset preset = FindPresetByName(_activeCameraPresetName);
            if (preset == null)
            {
                SetStatus("体位保存: アクティブなプリセットが無い");
                return;
            }

            if (!TryGetCurrentPoseInfo(out string key, out string displayName))
            {
                SetStatus("体位保存: 体位を取得できない");
                return;
            }

            preset.UsePoseOverrides = true;
            if (!CapturePoseOverrideToPreset(preset, key, displayName))
            {
                SetStatus("体位保存に失敗");
                return;
            }

            CommitPresets("pose-override-save");
            SetStatus("体位保存: " + (preset.Name ?? string.Empty) + " / " + displayName);
        }

        private void UpdatePosePresetTracking()
        {
            bool hasPose = TryGetCurrentPoseInfo(out string key, out string displayName);
            if (!hasPose)
            {
                _lastPoseKey = string.Empty;
                _lastPoseDisplayName = string.Empty;
                _poseTrackingInitialized = false;
                return;
            }

            if (!_posePresetAutoApply)
            {
                _lastPoseKey = key;
                _lastPoseDisplayName = displayName;
                _poseTrackingInitialized = true;
                return;
            }

            if (!_poseTrackingInitialized)
            {
                RefreshCameraControllerCache();
                if (!_hasCameraController || _ctrlCamera == null)
                    return;

                _lastPoseKey = key;
                _lastPoseDisplayName = displayName;
                _poseTrackingInitialized = true;
                ApplyActivePosePreset(key);
                return;
            }

            if (string.Equals(key, _lastPoseKey, StringComparison.Ordinal))
                return;

            _lastPoseKey = key;
            _lastPoseDisplayName = displayName;
            ApplyActivePosePreset(key);
        }

        private void ApplyActivePosePreset(string key)
        {
            if (string.IsNullOrWhiteSpace(_activeCameraPresetName))
                return;

            CameraPreset preset = FindPresetByName(_activeCameraPresetName);
            if (preset == null || !preset.UsePoseOverrides)
                return;

            if (FindCameraPoseOverride(preset, key) == null)
                return;

            LoadPreset(preset);
            LogInfo("pose auto-apply key=" + key + " preset=" + _activeCameraPresetName);
        }
    }
}
