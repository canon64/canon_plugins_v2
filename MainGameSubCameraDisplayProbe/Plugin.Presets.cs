using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private const int SaveModeNormal = 0;
        private const int SaveModeBoneLink = 1;
        private static readonly FieldInfo HSceneFemaleListField = typeof(HSceneProc).GetField(
            "lstFemale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static HSceneProc _cachedHSceneProc;
        private static float _nextHSceneProcSearchTime;
        private static ChaControl _cachedFallbackFemale;
        private static float _nextFallbackFemaleSearchTime;
        private ChaControl _cachedActiveBoneFemale;
        private Transform _cachedActiveBoneRoot;
        private Transform _cachedActiveBone;
        private int _cachedActiveBoneTarget = -1;
        private string _cachedActiveBoneName = string.Empty;
        private float _nextActiveBoneResolveTime;

        private static HSceneProc ResolveHSceneProcCached()
        {
            if (_cachedHSceneProc != null)
                return _cachedHSceneProc;
            if (!ReferenceEquals(_cachedHSceneProc, null))
            {
                _cachedHSceneProc = null;
                _cachedFallbackFemale = null;
            }
            float now = Time.unscaledTime;
            if (now < _nextHSceneProcSearchTime)
                return null;
            _nextHSceneProcSearchTime = now + 0.5f;
            _cachedHSceneProc = UnityEngine.Object.FindObjectOfType<HSceneProc>();
            return _cachedHSceneProc;
        }

        private void SaveCurrentPreset()
        {
            if (_actorLease != 0) { SaveExternalCameraPreset(null); return; }
            EnsureCameraProbe();
            if (_cameraAnchorObject == null || _subCamera == null)
            {
                SetStatus("サブカメラ未生成");
                return;
            }

            string name = string.IsNullOrWhiteSpace(_presetNameBuffer)
                ? "Preset " + ((_settings.Presets?.Length ?? 0) + 1)
                : _presetNameBuffer.Trim();
            name = MakeUniquePresetName(name, null);

            SubCameraPreset preset = _settings.SelectedSaveMode == SaveModeBoneLink
                ? BuildBoneLinkedPreset(name)
                : BuildNormalPreset(name);
            if (preset == null)
            {
                SetStatus("プリセット保存失敗");
                return;
            }

            List<SubCameraPreset> presets = new List<SubCameraPreset>(_settings.Presets ?? new SubCameraPreset[0]);
            presets.Add(preset);
            _settings.Presets = presets.ToArray();

            // 保存した新規プリセットをそのままアクティブ（赤）にする
            _presetNameBuffer = name;
            if (preset.UseBoneLink)
            {
                ActivateBoneFollow(preset);
            }
            else
            {
                ClearBoneFollow();
                _activeCameraPresetName = name;
            }

            SaveSettings();
            SetStatus("保存: " + name);
        }

        private void SaveOrOverwritePreset()
        {
            string name = (_presetNameBuffer ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                SetStatus("保存名なし");
                return;
            }

            SubCameraPreset existing = FindPresetByName(name);
            if (existing == null)
            {
                SaveCurrentPreset();
                return;
            }

            OverwritePreset(existing);
        }

        private SubCameraPreset BuildNormalPreset(string name)
        {
            Transform cameraTransform = _cameraAnchorObject.transform;
            return new SubCameraPreset
            {
                Name = name,
                UseBoneLink = false,
                SaveCameraPosition = true,
                CameraPosition = ToArray(cameraTransform.position),
                CameraRotation = ToArray(NormalizeEuler(cameraTransform.rotation.eulerAngles)),
                Fov = _settings.CameraFieldOfView,
                UsePoseOverrides = _settings.SaveCameraPoseOverrides,
                PoseOverrides = new CameraPoseOverride[0]
            };
        }

        private SubCameraPreset BuildBoneLinkedPreset(string name)
        {
            bool useActiveFollow = _boneFollowActive && _cameraAnchorObject != null;
            Transform bone = useActiveFollow ? ResolveActiveBone() : ResolveSelectedBone();
            if (bone == null || _cameraAnchorObject == null)
                return null;

            Transform cameraTransform = _cameraAnchorObject.transform;
            int boneTarget = useActiveFollow ? _activeBoneTarget : _settings.SelectedBoneTarget;
            string boneName = useActiveFollow ? _activeBoneName : bone.name;
            bool saveCameraPosition = useActiveFollow
                ? (_activeSaveCameraPosition || (_settings != null && _settings.SaveBoneCameraPosition))
                : (_settings != null && _settings.SaveBoneCameraPosition);
            Vector3 lookAtOffsetLocal = useActiveFollow ? _activeLookAtOffsetLocal : Vector3.zero;
            Vector3 lookAt = bone.TransformPoint(lookAtOffsetLocal);
            Vector3 cameraPositionOffset = cameraTransform.position - bone.position;
            return new SubCameraPreset
            {
                Name = name,
                UseBoneLink = true,
                BoneTarget = boneTarget,
                BoneName = boneName,
                SaveCameraPosition = saveCameraPosition,
                CameraPosition = saveCameraPosition ? ToArray(cameraTransform.position) : null,
                CameraRotation = ToArray(NormalizeEuler(cameraTransform.rotation.eulerAngles)),
                CameraOffsetLocal = saveCameraPosition ? ToArray(cameraPositionOffset) : null,
                LookAtPosition = ToArray(lookAt),
                LookAtOffsetLocal = ToArray(lookAtOffsetLocal),
                Fov = _settings.CameraFieldOfView,
                UsePoseOverrides = _settings.SaveCameraPoseOverrides,
                PoseOverrides = new CameraPoseOverride[0]
            };
        }

        private void LoadPreset(SubCameraPreset preset)
        {
            LoadPreset(preset, "manual");
        }

        private void LoadPreset(SubCameraPreset preset, string source)
        {
            if (_actorLease != 0) { ApplyExternalPreset(preset, source); return; }
            if (preset == null)
                return;

            EnsureCameraProbe();
            if (_cameraAnchorObject == null || _subCamera == null)
            {
                SetStatus("サブカメラ未生成");
                return;
            }

            SubCameraPreset resolved = ResolveCameraPresetForCurrentPose(preset);
            _activeCameraPresetName = preset.Name ?? string.Empty;
            _presetNameBuffer = preset.Name ?? string.Empty;
            StartCameraTransition(resolved);
            SetStatus("呼び出し: " + (preset.Name ?? string.Empty));
            NotifyAutoCycleLoadPreset(preset, source);
        }

        private void ApplyNormalPreset(SubCameraPreset preset)
        {
            ClearBoneFollow();
            Transform cameraTransform = _cameraAnchorObject.transform;
            cameraTransform.position = ToVector3(preset.CameraPosition, cameraTransform.position);
            cameraTransform.rotation = Quaternion.Euler(ToVector3(preset.CameraRotation, cameraTransform.rotation.eulerAngles));
        }

        private void ActivateBoneFollow(SubCameraPreset preset)
        {
            _boneFollowActive = true;
            _activeBonePresetName = preset.Name ?? string.Empty;
            _activeBoneTarget = Mathf.Clamp(preset.BoneTarget, 0, BoneTargetLabels.Length - 1);
            _activeBoneName = preset.BoneName ?? string.Empty;
            _activeSaveCameraPosition = HasCameraPosition(preset)
                || (_settings != null && _settings.SaveBoneCameraPosition);
            _activeLookAtOffsetLocal = ToVector3(preset.LookAtOffsetLocal, Vector3.zero);
            _activeCameraOffsetLocal = ToVector3(preset.CameraOffsetLocal, Vector3.zero);
            _activePresetFov = Mathf.Clamp(preset.Fov <= 0f ? _settings.CameraFieldOfView : preset.Fov, 10f, 170f);

            // アクティブなボーンプリセットに合わせて保存モード/対象ボーンボタンを同期
            _settings.SelectedSaveMode = SaveModeBoneLink;
            _settings.SelectedBoneTarget = _activeBoneTarget;

            if (_activeSaveCameraPosition && _activeCameraOffsetLocal.sqrMagnitude <= 0.000001f)
            {
                Transform bone = ResolveActiveBone();
                if (bone != null)
                    _activeCameraOffsetLocal = ToVector3(preset.CameraPosition, _cameraAnchorObject.transform.position) - bone.position;
            }

            UpdateBoneFollow();
        }

        private void UpdateBoneFollow()
        {
            if (!_boneFollowActive || _cameraAnchorObject == null)
                return;

            Transform bone = ResolveActiveBone();
            if (bone == null)
                return;

            Transform cameraTransform = _cameraAnchorObject.transform;
            Vector3 lookAt = bone.TransformPoint(_activeLookAtOffsetLocal);
            if (_activeSaveCameraPosition)
                cameraTransform.position = bone.position + _activeCameraOffsetLocal;

            Vector3 forward = lookAt - cameraTransform.position;
            if (forward.sqrMagnitude > 0.000001f)
                cameraTransform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

            _settings.CameraFieldOfView = _activePresetFov;
        }

        // 位置は呼び出し側（VR掴み/ギズモ掴み）に任せ、回転だけボーン注視に合わせる
        private void ApplyBoneLookAtRotationOnly()
        {
            if (!_boneFollowActive || _cameraAnchorObject == null)
                return;

            Transform bone = ResolveActiveBone();
            if (bone == null)
                return;

            Vector3 lookAt = bone.TransformPoint(_activeLookAtOffsetLocal);
            Vector3 forward = lookAt - _cameraAnchorObject.transform.position;
            if (forward.sqrMagnitude > 0.000001f)
                _cameraAnchorObject.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void SyncActiveFovFromUi()
        {
            _settings.CameraFieldOfView = Mathf.Clamp(_settings.CameraFieldOfView, 10f, 170f);
            if (_actorLease != 0)
            {
                if (_externalPreset != null)
                {
                    _externalPreset.Fov = _settings.CameraFieldOfView;
                    _externalPresetDuration = 0;
                    UpdateExternalPreset();
                    PersistExternalPresetEdits();
                }
                return;
            }
            if (!_boneFollowActive)
                return;

            _activePresetFov = _settings.CameraFieldOfView;
            SaveActiveBonePresetOffsets();
        }

        private void CaptureActiveBoneCameraOffset()
        {
            if (!_boneFollowActive || _cameraAnchorObject == null)
                return;

            bool shouldSaveCameraPosition =
                _activeSaveCameraPosition || (_settings != null && _settings.SaveBoneCameraPosition);

            if (!shouldSaveCameraPosition)
                return;

            Transform bone = ResolveActiveBone();
            if (bone == null)
                return;

            _activeSaveCameraPosition = true;
            _activeCameraOffsetLocal = _cameraAnchorObject.transform.position - bone.position;
        }

        private void SaveActiveBonePresetOffsets()
        {
            // The normal H shot is suspended while the independent actor owns the camera.
            if (_actorLease != 0) return;
            SubCameraPreset preset = FindActiveBonePreset();
            if (preset == null)
                return;

            Transform bone = ResolveActiveBone();

            // 体位対応プリセットで今の体位の override がある場合は、本体ではなく override に書き戻す
            // （追従中のFOV/注視いじりが本体を汚染するのを防ぐ）
            if (preset.UsePoseOverrides && TryGetCurrentPoseInfo(out string poseKey, out _))
            {
                CameraPoseOverride pose = FindCameraPoseOverride(preset, poseKey);
                if (pose != null)
                {
                    pose.LookAtOffsetLocal = ToArray(_activeLookAtOffsetLocal);
                    pose.LookAtPosition = bone != null
                        ? ToArray(bone.TransformPoint(_activeLookAtOffsetLocal))
                        : pose.LookAtPosition;
                    pose.Fov = _settings.CameraFieldOfView;
                    if (_activeSaveCameraPosition)
                    {
                        pose.SaveCameraPosition = true;
                        pose.CameraOffsetLocal = ToArray(_activeCameraOffsetLocal);
                        if (bone != null)
                            pose.CameraPosition = ToArray(bone.position + _activeCameraOffsetLocal);
                    }
                    SaveSettings();
                    return;
                }
            }

            preset.LookAtOffsetLocal = ToArray(_activeLookAtOffsetLocal);
            preset.LookAtPosition = bone != null
                ? ToArray(bone.TransformPoint(_activeLookAtOffsetLocal))
                : preset.LookAtPosition;
            preset.Fov = _settings.CameraFieldOfView;

            if (_activeSaveCameraPosition)
            {
                preset.SaveCameraPosition = true;
                preset.CameraOffsetLocal = ToArray(_activeCameraOffsetLocal);
                if (bone != null)
                    preset.CameraPosition = ToArray(bone.position + _activeCameraOffsetLocal);
            }

            SaveSettings();
        }

        private void SaveMovedCameraPreset()
        {
            if (_actorLease != 0) { _settings.SaveBoneCameraPosition = true; CaptureExternalGrip(); return; }
            if (_cameraAnchorObject == null) return;
            // Moving the camera explicitly requests that its position be retained.
            _settings.SaveBoneCameraPosition = true;
            _activeSaveCameraPosition = true;
            CaptureActiveBoneCameraOffset();
            PersistTransformsToSettings();
            var preset = FindPresetByName(_activeCameraPresetName);
            if (preset != null) OverwritePresetPoseAware(preset);
        }

        private void OverwriteActivePreset()
        {
            if (_actorLease != 0) {
                var existing = FindPresetByName((_presetNameBuffer ?? "").Trim());
                if (existing == null) SetStatus("上書き対象なし"); else SaveExternalCameraPreset(existing);
                return;
            }
            EnsureCameraProbe();
            if (_cameraAnchorObject == null || _subCamera == null)
            {
                SetStatus("サブカメラ未生成");
                return;
            }

            string name = (_presetNameBuffer ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                SetStatus("上書き名なし");
                return;
            }

            SubCameraPreset preset = FindPresetByName(name);
            if (preset == null)
            {
                SetStatus("上書き対象なし");
                return;
            }

            string normalizedName = preset.Name ?? name;
            // トグルON、または既に体位対応プリセットなら、現在の体位として保存する。
            // 既存プリセット（UsePoseOverrides=false）でもトグルONなら旗を立てて体位を貯められるようにする。
            if ((_settings.SaveCameraPoseOverrides || preset.UsePoseOverrides)
                && TryGetCurrentPoseInfo(out string poseKey, out string poseDisplayName))
            {
                preset.UsePoseOverrides = true;
                CapturePoseOverrideToPreset(preset, poseKey, poseDisplayName);
                _activeCameraPresetName = normalizedName;
                SaveSettings();
                SetStatus("体位上書き保存: " + normalizedName + " / " + poseDisplayName);
                return;
            }

            SubCameraPreset next = preset.UseBoneLink
                ? BuildBoneLinkedPreset(normalizedName)
                : BuildNormalPreset(normalizedName);
            if (next == null)
            {
                SetStatus("上書き保存失敗");
                return;
            }

            preset.UseBoneLink = next.UseBoneLink;
            preset.BoneTarget = next.BoneTarget;
            preset.BoneName = next.BoneName;
            preset.SaveCameraPosition = next.SaveCameraPosition;
            preset.CameraPosition = next.CameraPosition;
            preset.CameraRotation = next.CameraRotation;
            preset.CameraOffsetLocal = next.CameraOffsetLocal;
            preset.LookAtPosition = next.LookAtPosition;
            preset.LookAtOffsetLocal = next.LookAtOffsetLocal;
            preset.Fov = next.Fov;
            preset.AutoCycleInclude = next.AutoCycleInclude;

            _activeCameraPresetName = normalizedName;
            SaveSettings();
            SetStatus("上書き保存: " + normalizedName);
        }

        private void OverwritePresetPoseAware(SubCameraPreset preset)
        {
            if (_actorLease != 0) { SaveExternalCameraPreset(preset); return; }
            if (preset == null)
                return;

            if (TryGetCurrentPoseInfo(out string poseKey, out string poseDisplayName))
            {
                preset.UsePoseOverrides = true;
                CapturePoseOverrideToPreset(preset, poseKey, poseDisplayName);
                _activeCameraPresetName = preset.Name ?? string.Empty;
                SaveSettings();
                SetStatus("体位保存: " + (preset.Name ?? string.Empty) + " / " + poseDisplayName);
                return;
            }

            OverwritePreset(preset);
        }

        private void OverwritePreset(SubCameraPreset preset)
        {
            if (_actorLease != 0) { SaveExternalCameraPreset(preset); return; }
            EnsureCameraProbe();
            if (preset == null || _cameraAnchorObject == null || _subCamera == null)
            {
                SetStatus("上書き保存失敗");
                return;
            }

            string normalizedName = preset.Name ?? (_presetNameBuffer ?? string.Empty).Trim();
            SubCameraPreset next = _settings.SelectedSaveMode == SaveModeBoneLink
                ? BuildBoneLinkedPreset(normalizedName)
                : BuildNormalPreset(normalizedName);
            if (next == null)
            {
                SetStatus("上書き保存失敗");
                return;
            }

            preset.UseBoneLink = next.UseBoneLink;
            preset.BoneTarget = next.BoneTarget;
            preset.BoneName = next.BoneName;
            preset.SaveCameraPosition = next.SaveCameraPosition;
            preset.CameraPosition = next.CameraPosition;
            preset.CameraRotation = next.CameraRotation;
            preset.CameraOffsetLocal = next.CameraOffsetLocal;
            preset.LookAtPosition = next.LookAtPosition;
            preset.LookAtOffsetLocal = next.LookAtOffsetLocal;
            preset.Fov = next.Fov;
            preset.UsePoseOverrides = next.UsePoseOverrides || preset.UsePoseOverrides;

            _activeCameraPresetName = normalizedName;
            if (preset.UseBoneLink)
                ActivateBoneFollow(preset);
            else
                ClearBoneFollow();
            SaveSettings();
            SetStatus("上書き保存: " + normalizedName);
        }

        private void RenamePreset(SubCameraPreset preset, string newNameRaw)
        {
            if (preset == null)
                return;

            string newName = (newNameRaw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(newName) || HasPresetNameConflict(newName, preset))
                return;

            string old = preset.Name ?? string.Empty;
            preset.Name = newName;
            if (string.Equals(_activeCameraPresetName, old, StringComparison.Ordinal))
                _activeCameraPresetName = newName;
            if (string.Equals(_activeBonePresetName, old, StringComparison.Ordinal))
                _activeBonePresetName = newName;
            if (string.Equals((_presetNameBuffer ?? string.Empty).Trim(), old, StringComparison.Ordinal))
                _presetNameBuffer = newName;
            SaveSettings();
            SetStatus("改名: " + old + " → " + newName);
        }

        private bool IsSelectedPreset(SubCameraPreset preset)
        {
            if (preset == null)
                return false;

            if (_actorLease != 0)
                return string.Equals(preset.Name ?? string.Empty, _activeCameraPresetName ?? string.Empty, StringComparison.Ordinal);

            if (_boneFollowActive && preset.UseBoneLink)
                return string.Equals(preset.Name ?? string.Empty, _activeBonePresetName ?? string.Empty, StringComparison.Ordinal);

            return string.Equals(preset.Name ?? string.Empty, _activeCameraPresetName ?? string.Empty, StringComparison.Ordinal);
        }

        private void ActivatePresetNormalLive(SubCameraPreset preset)
        {
            if (preset == null)
                return;

            if (_actorLease != 0)
            {
                _settings.SelectedSaveMode = SaveModeNormal;
                SaveExternalCameraPreset(preset);
                return;
            }

            _settings.SelectedSaveMode = SaveModeNormal;
            _presetNameBuffer = preset.Name ?? string.Empty;
            ClearBoneFollow();
            _activeCameraPresetName = preset.Name ?? string.Empty;
            StartCameraTransition(preset);
            SetStatus("通常: " + (preset.Name ?? string.Empty));
        }

        private void ActivatePresetBoneLinkLive(SubCameraPreset preset, int target)
        {
            if (preset == null)
                return;

            if (_actorLease != 0)
            {
                _settings.SelectedSaveMode = SaveModeBoneLink;
                _settings.SelectedBoneTarget = Mathf.Clamp(target, 0, BoneTargetLabels.Length - 1);
                SaveExternalCameraPreset(preset);
                return;
            }

            _settings.SelectedSaveMode = SaveModeBoneLink;
            _settings.SelectedBoneTarget = Mathf.Clamp(target, 0, BoneTargetLabels.Length - 1);
            _presetNameBuffer = preset.Name ?? string.Empty;

            if (!preset.UseBoneLink)
            {
                SubCameraPreset next = BuildBoneLinkedPreset(preset.Name ?? string.Empty);
                if (next != null)
                {
                    preset.UseBoneLink = true;
                    preset.BoneTarget = _settings.SelectedBoneTarget;
                    preset.BoneName = next.BoneName;
                    preset.SaveCameraPosition = next.SaveCameraPosition;
                    preset.CameraPosition = next.CameraPosition;
                    preset.CameraRotation = next.CameraRotation;
                    preset.CameraOffsetLocal = next.CameraOffsetLocal;
                    preset.LookAtPosition = next.LookAtPosition;
                    preset.LookAtOffsetLocal = next.LookAtOffsetLocal;
                    preset.Fov = next.Fov;
                    SaveSettings();
                }
            }

            if (preset.BoneTarget != _settings.SelectedBoneTarget)
            {
                preset.BoneTarget = _settings.SelectedBoneTarget;
                preset.BoneName = string.Empty;
            }
            ActivateBoneFollow(preset);
            SetStatus("ボーン: " + BoneTargetLabels[_settings.SelectedBoneTarget] + " / " + (preset.Name ?? string.Empty));
        }

        private SubCameraPreset ResolveCameraPresetForCurrentPose(SubCameraPreset preset)
        {
            if (preset == null || !preset.UsePoseOverrides)
                return preset;

            if (!TryGetCurrentPoseInfo(out string key, out _))
                return preset;

            CameraPoseOverride pose = FindCameraPoseOverride(preset, key);
            if (pose == null)
                return preset;

            SubCameraPreset resolved = ClonePreset(preset);
            // 体位にボーン情報がある時だけ差し替え（旧データはBoneName無し→本体のボーンを維持＝後方互換）
            if (!pose.UseBoneLink || !string.IsNullOrEmpty(pose.BoneName))
            {
                resolved.UseBoneLink = pose.UseBoneLink;
                resolved.BoneTarget = pose.BoneTarget;
                resolved.BoneName = pose.BoneName;
            }
            resolved.SaveCameraPosition = pose.SaveCameraPosition;
            resolved.CameraPosition = CloneArray(pose.CameraPosition, resolved.CameraPosition);
            resolved.CameraRotation = CloneArray(pose.CameraRotation, resolved.CameraRotation);
            resolved.CameraOffsetLocal = CloneArray(pose.CameraOffsetLocal, resolved.CameraOffsetLocal);
            resolved.LookAtPosition = CloneArray(pose.LookAtPosition, resolved.LookAtPosition);
            resolved.LookAtOffsetLocal = CloneArray(pose.LookAtOffsetLocal, resolved.LookAtOffsetLocal);
            resolved.Fov = pose.Fov > 0f ? pose.Fov : resolved.Fov;
            LogInfo("pose override load preset=" + (preset.Name ?? string.Empty)
                + " key=" + key
                + " display=" + (pose.DisplayName ?? string.Empty)
                + " look=" + FormatArray(resolved.LookAtOffsetLocal)
                + " offset=" + FormatArray(resolved.CameraOffsetLocal)
                + " pos=" + FormatArray(resolved.CameraPosition)
                + " fov=" + resolved.Fov.ToString("0.##"));
            return resolved;
        }

        private void CapturePoseOverrideToPreset(SubCameraPreset preset, string key, string displayName)
        {
            if (preset == null || string.IsNullOrWhiteSpace(key))
                return;

            CameraPoseOverride pose = FindCameraPoseOverride(preset, key);
            if (pose == null)
            {
                List<CameraPoseOverride> list = new List<CameraPoseOverride>(preset.PoseOverrides ?? new CameraPoseOverride[0]);
                pose = new CameraPoseOverride { Key = key };
                list.Add(pose);
                preset.PoseOverrides = list.ToArray();
            }

            SubCameraPreset current = _boneFollowActive
                ? BuildBoneLinkedPreset(preset.Name ?? string.Empty)
                : BuildNormalPreset(preset.Name ?? string.Empty);
            if (current == null)
                return;

            pose.DisplayName = displayName ?? string.Empty;
            pose.UseBoneLink = current.UseBoneLink;
            pose.BoneTarget = current.BoneTarget;
            pose.BoneName = current.BoneName;
            pose.SaveCameraPosition = current.SaveCameraPosition;
            pose.CameraPosition = CloneArray(current.CameraPosition, null);
            pose.CameraRotation = CloneArray(current.CameraRotation, null);
            pose.CameraOffsetLocal = CloneArray(current.CameraOffsetLocal, null);
            pose.LookAtPosition = CloneArray(current.LookAtPosition, null);
            pose.LookAtOffsetLocal = CloneArray(current.LookAtOffsetLocal, null);
            pose.Fov = current.Fov;
            LogInfo("pose override save preset=" + (preset.Name ?? string.Empty)
                + " key=" + key
                + " display=" + pose.DisplayName
                + " bone=" + current.BoneTarget + ":" + (current.BoneName ?? string.Empty)
                + " look=" + FormatArray(pose.LookAtOffsetLocal)
                + " offset=" + FormatArray(pose.CameraOffsetLocal)
                + " pos=" + FormatArray(pose.CameraPosition)
                + " fov=" + pose.Fov.ToString("0.##"));
        }

        private CameraPoseOverride FindCameraPoseOverride(SubCameraPreset preset, string key)
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

        private static SubCameraPreset ClonePreset(SubCameraPreset preset)
        {
            if (preset == null)
                return null;

            return new SubCameraPreset
            {
                Name = preset.Name,
                UseBoneLink = preset.UseBoneLink,
                BoneTarget = preset.BoneTarget,
                BoneName = preset.BoneName,
                SaveCameraPosition = preset.SaveCameraPosition,
                CameraPosition = CloneArray(preset.CameraPosition, null),
                CameraRotation = CloneArray(preset.CameraRotation, null),
                CameraOffsetLocal = CloneArray(preset.CameraOffsetLocal, null),
                LookAtPosition = CloneArray(preset.LookAtPosition, null),
                LookAtOffsetLocal = CloneArray(preset.LookAtOffsetLocal, null),
                Fov = preset.Fov,
                AutoCycleInclude = preset.AutoCycleInclude,
                UsePoseOverrides = preset.UsePoseOverrides,
                PoseOverrides = preset.PoseOverrides,
                ActorCamera = preset.ActorCamera
            };
        }

        private static ActorCameraShot CaptureActorShot(SubCameraPreset source)
        {
            return new ActorCameraShot {
                UseBoneLink = source.UseBoneLink, BoneTarget = source.BoneTarget, BoneName = source.BoneName,
                SaveCameraPosition = source.SaveCameraPosition,
                CameraPosition = CloneArray(source.CameraPosition, null), CameraRotation = CloneArray(source.CameraRotation, null),
                CameraOffsetLocal = CloneArray(source.CameraOffsetLocal, null), LookAtPosition = CloneArray(source.LookAtPosition, null),
                LookAtOffsetLocal = CloneArray(source.LookAtOffsetLocal, null), Fov = source.Fov
            };
        }

        private static SubCameraPreset ResolveActorShot(SubCameraPreset preset)
        {
            SubCameraPreset resolved = ClonePreset(preset);
            ActorCameraShot shot = preset.ActorCamera;
            if (shot == null) return resolved; // Existing presets retain their prior actor view.
            resolved.UseBoneLink = shot.UseBoneLink; resolved.BoneTarget = shot.BoneTarget; resolved.BoneName = shot.BoneName;
            resolved.SaveCameraPosition = shot.SaveCameraPosition;
            resolved.CameraPosition = CloneArray(shot.CameraPosition, null); resolved.CameraRotation = CloneArray(shot.CameraRotation, null);
            resolved.CameraOffsetLocal = CloneArray(shot.CameraOffsetLocal, null); resolved.LookAtPosition = CloneArray(shot.LookAtPosition, null);
            resolved.LookAtOffsetLocal = CloneArray(shot.LookAtOffsetLocal, null); resolved.Fov = shot.Fov;
            return resolved;
        }

        private static float[] CloneArray(float[] values, float[] fallback)
        {
            float[] source = values ?? fallback;
            if (source == null)
                return null;

            float[] clone = new float[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private string MakeUniquePresetName(string baseName, SubCameraPreset self)
        {
            string seed = string.IsNullOrWhiteSpace(baseName) ? "Preset" : baseName.Trim();
            string candidate = seed;
            int suffix = 2;
            while (HasPresetNameConflict(candidate, self))
            {
                candidate = seed + " " + suffix.ToString();
                suffix++;
            }

            return candidate;
        }

        private bool HasPresetNameConflict(string name, SubCameraPreset self)
        {
            SubCameraPreset[] presets = _settings.Presets ?? new SubCameraPreset[0];
            for (int i = 0; i < presets.Length; i++)
            {
                SubCameraPreset preset = presets[i];
                if (preset == null || ReferenceEquals(preset, self))
                    continue;
                if (string.Equals(preset.Name ?? string.Empty, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private SubCameraPreset FindActiveBonePreset()
        {
            if (!_boneFollowActive)
                return null;

            SubCameraPreset[] presets = _settings.Presets ?? new SubCameraPreset[0];
            SubCameraPreset nameMatch = null;
            for (int i = 0; i < presets.Length; i++)
            {
                SubCameraPreset preset = presets[i];
                if (preset == null || !preset.UseBoneLink)
                    continue;

                bool sameName = string.Equals(preset.Name ?? string.Empty, _activeBonePresetName ?? string.Empty, StringComparison.Ordinal);
                if (!sameName)
                    continue;

                if (nameMatch == null)
                    nameMatch = preset;

                bool sameBone =
                    preset.BoneTarget == _activeBoneTarget
                    && string.Equals(preset.BoneName ?? string.Empty, _activeBoneName ?? string.Empty, StringComparison.Ordinal);
                if (sameBone)
                    return preset;
            }

            return nameMatch;
        }

        private bool IsActiveBonePreset(SubCameraPreset preset)
        {
            if (preset == null)
                return false;

            if (_actorLease != 0)
                return string.Equals(preset.Name ?? string.Empty, _activeCameraPresetName ?? string.Empty, StringComparison.Ordinal);

            if (!_boneFollowActive || !preset.UseBoneLink)
                return string.Equals(preset.Name ?? string.Empty, _activeCameraPresetName ?? string.Empty, StringComparison.Ordinal);

            return string.Equals(preset.Name ?? string.Empty, _activeBonePresetName ?? string.Empty, StringComparison.Ordinal)
                && preset.BoneTarget == _activeBoneTarget
                && string.Equals(preset.BoneName ?? string.Empty, _activeBoneName ?? string.Empty, StringComparison.Ordinal);
        }

        // 追従中に対象ボーンを変更したとき、追従先を新しい骨へ切り替える。
        // カメラオフセット/注視オフセットは維持したまま、新しい骨基準でカメラを移動する。
        private void RetargetActiveBone(int newTarget)
        {
            if (_actorLease != 0) return;
            if (!_boneFollowActive)
                return;

            int clamped = Mathf.Clamp(newTarget, 0, BoneTargetLabels.Length - 1);
            ChaControl female = ResolveMainFemale();
            Transform[] boneCache = GetFemaleBoneCache(female);
            Transform bone = ResolveBoneTarget(boneCache, clamped, null);

            _activeBoneTarget = clamped;
            _activeBoneName = bone != null ? bone.name : string.Empty;
            UpdateBoneFollow();
            SetStatus("ボーン変更: " + BoneTargetLabels[clamped]);
        }

        private void ClearBoneFollow()
        {
            if (_actorLease != 0) return;
            _boneFollowActive = false;
            _cachedActiveBone = null;
            _cachedActiveBoneFemale = null;
            _cachedActiveBoneRoot = null;
            _nextActiveBoneResolveTime = 0f;
            _activeBonePresetName = string.Empty;
            _activeBoneTarget = -1;
            _activeBoneName = string.Empty;
            _activeSaveCameraPosition = false;
            _activeLookAtOffsetLocal = Vector3.zero;
            _activeCameraOffsetLocal = Vector3.zero;
            _activePresetFov = _settings != null ? _settings.CameraFieldOfView : 60f;
        }

        private void RemovePresetAt(int index)
        {
            SubCameraPreset[] source = _settings.Presets ?? new SubCameraPreset[0];
            if (index < 0 || index >= source.Length)
                return;

            string removed = source[index]?.Name ?? string.Empty;
            List<SubCameraPreset> next = new List<SubCameraPreset>(source);
            next.RemoveAt(index);
            _settings.Presets = next.ToArray();
            SaveSettings();
            SetStatus("削除: " + removed);
        }

        private void RemovePoseOverrideAt(SubCameraPreset preset, int index)
        {
            if (preset == null || preset.PoseOverrides == null)
                return;
            if (index < 0 || index >= preset.PoseOverrides.Length)
                return;

            CameraPoseOverride target = preset.PoseOverrides[index];
            string removed = target != null
                ? (string.IsNullOrWhiteSpace(target.DisplayName) ? target.Key : target.DisplayName)
                : string.Empty;

            List<CameraPoseOverride> list = new List<CameraPoseOverride>(preset.PoseOverrides);
            list.RemoveAt(index);
            preset.PoseOverrides = list.ToArray();
            SaveSettings();
            SetStatus("体位削除: " + (removed ?? string.Empty));
        }

        private void RemovePoseOverride(SubCameraPreset preset, string key)
        {
            if (preset == null || preset.PoseOverrides == null || string.IsNullOrEmpty(key))
                return;

            List<CameraPoseOverride> list = new List<CameraPoseOverride>(preset.PoseOverrides);
            int removed = list.RemoveAll(o => o != null && string.Equals(o.Key, key, StringComparison.Ordinal));
            if (removed <= 0)
                return;

            preset.PoseOverrides = list.ToArray();
            if (preset.PoseOverrides.Length == 0)
                preset.UsePoseOverrides = false;
            SaveSettings();
            SetStatus("体位削除: " + key);
        }

        private string GetPresetDisplayName(SubCameraPreset preset, int index)
        {
            string name = string.IsNullOrWhiteSpace(preset?.Name) ? "Preset " + (index + 1) : preset.Name;
            if (preset == null)
                return name;

            string flags = string.Empty;
            if (preset.UseBoneLink && HasArray(preset.LookAtOffsetLocal))
                flags += "[注視]";
            if (HasCameraPosition(preset))
                flags += "[位置]";
            if (preset.Fov > 0f)
                flags += "[FOV]";
            if (preset.UsePoseOverrides)
                flags += "[体位]";

            return preset.UseBoneLink
                ? "ボーン(" + BoneTargetLabels[Mathf.Clamp(preset.BoneTarget, 0, BoneTargetLabels.Length - 1)] + ")" + flags + ": " + name
                : flags + name;
        }

        private static bool HasCameraPosition(SubCameraPreset preset)
        {
            return preset != null && preset.SaveCameraPosition && (HasArray(preset.CameraOffsetLocal) || HasArray(preset.CameraPosition));
        }

        private static bool HasPoseCameraPosition(CameraPoseOverride pose)
        {
            return pose != null && pose.SaveCameraPosition && (HasArray(pose.CameraOffsetLocal) || HasArray(pose.CameraPosition));
        }

        private static bool PresetHasStoredOffset(SubCameraPreset preset)
        {
            return preset != null && (HasArray(preset.CameraOffsetLocal) || HasArray(preset.CameraPosition));
        }

        private static bool PoseHasStoredOffset(CameraPoseOverride pose)
        {
            return pose != null && (HasArray(pose.CameraOffsetLocal) || HasArray(pose.CameraPosition));
        }

        private static bool AllUseCameraPositionOn(SubCameraPreset preset, out bool anyData)
        {
            anyData = false;
            bool all = true;
            if (preset != null && preset.UseBoneLink && PresetHasStoredOffset(preset))
            {
                anyData = true;
                if (!preset.SaveCameraPosition)
                    all = false;
            }
            CameraPoseOverride[] overrides = preset?.PoseOverrides ?? new CameraPoseOverride[0];
            for (int i = 0; i < overrides.Length; i++)
            {
                CameraPoseOverride pose = overrides[i];
                if (pose != null && pose.UseBoneLink && PoseHasStoredOffset(pose))
                {
                    anyData = true;
                    if (!pose.SaveCameraPosition)
                        all = false;
                }
            }
            return anyData && all;
        }

        private void SetAllUseCameraPosition(SubCameraPreset preset, bool value)
        {
            bool any = false;
            if (preset != null && preset.UseBoneLink && PresetHasStoredOffset(preset))
            {
                preset.SaveCameraPosition = value;
                any = true;
            }
            CameraPoseOverride[] overrides = preset?.PoseOverrides ?? new CameraPoseOverride[0];
            for (int i = 0; i < overrides.Length; i++)
            {
                CameraPoseOverride pose = overrides[i];
                if (pose != null && pose.UseBoneLink && PoseHasStoredOffset(pose))
                {
                    pose.SaveCameraPosition = value;
                    any = true;
                }
            }
            if (any)
                SaveSettings();
        }

        private bool AllUseCameraPositionOnAllPresets(out bool anyData)
        {
            anyData = false;
            bool all = true;
            SubCameraPreset[] presets = _settings.Presets ?? new SubCameraPreset[0];
            for (int i = 0; i < presets.Length; i++)
            {
                bool sub = AllUseCameraPositionOn(presets[i], out bool subAny);
                if (subAny)
                {
                    anyData = true;
                    if (!sub)
                        all = false;
                }
            }
            return anyData && all;
        }

        private void SetAllUseCameraPositionAllPresets(bool value)
        {
            bool any = false;
            SubCameraPreset[] presets = _settings.Presets ?? new SubCameraPreset[0];
            for (int i = 0; i < presets.Length; i++)
            {
                SubCameraPreset preset = presets[i];
                if (preset == null)
                    continue;
                if (preset.UseBoneLink && PresetHasStoredOffset(preset))
                {
                    preset.SaveCameraPosition = value;
                    any = true;
                }
                CameraPoseOverride[] overrides = preset.PoseOverrides ?? new CameraPoseOverride[0];
                for (int j = 0; j < overrides.Length; j++)
                {
                    CameraPoseOverride pose = overrides[j];
                    if (pose != null && pose.UseBoneLink && PoseHasStoredOffset(pose))
                    {
                        pose.SaveCameraPosition = value;
                        any = true;
                    }
                }
            }
            if (any)
                SaveSettings();
        }

        private static string DescribeModeBone(bool useBone, int boneTarget)
        {
            if (!useBone)
                return "通常";
            string b = boneTarget >= 0 && boneTarget < BoneTargetLabels.Length ? BoneTargetLabels[boneTarget] : "?";
            return "ボーン:" + b;
        }

        private static string DescribeTag(bool look, bool pos)
        {
            string s = string.Empty;
            if (look) s += "[注視]";
            if (pos) s += "[位置]";
            return s;
        }

        private static string DescribePresetBase(SubCameraPreset preset)
        {
            if (preset == null)
                return string.Empty;
            bool look = preset.UseBoneLink && HasArray(preset.LookAtOffsetLocal);
            bool pos = HasCameraPosition(preset);
            return DescribeModeBone(preset.UseBoneLink, preset.BoneTarget)
                + " FOV" + preset.Fov.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                + " " + DescribeTag(look, pos);
        }

        private static string DescribePoseOverride(CameraPoseOverride pose)
        {
            if (pose == null)
                return string.Empty;
            bool look = pose.UseBoneLink && HasArray(pose.LookAtOffsetLocal);
            bool pos = HasPoseCameraPosition(pose);
            return DescribeModeBone(pose.UseBoneLink, pose.BoneTarget)
                + " FOV" + pose.Fov.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                + " " + DescribeTag(look, pos);
        }

        private static bool HasArray(float[] values)
        {
            return values != null && values.Length >= 3;
        }

        private int CountPoseAwareCameraPresets()
        {
            SubCameraPreset[] presets = _settings.Presets ?? new SubCameraPreset[0];
            int count = 0;
            for (int i = 0; i < presets.Length; i++)
            {
                if (presets[i] != null && presets[i].UsePoseOverrides)
                    count++;
            }

            return count;
        }

        private Transform ResolveSelectedBone()
        {
            ChaControl female = ResolveMainFemale();
            Transform[] boneCache = GetFemaleBoneCache(female);
            return ResolveBoneTarget(boneCache, _settings.SelectedBoneTarget, null);
        }

        private Transform ResolveActiveBone()
        {
            ChaControl female = ResolveMainFemale();
            Transform root = female != null
                ? (female.animBody != null ? female.animBody.transform : female.transform)
                : null;
            float now = Time.unscaledTime;
            if (_cachedActiveBone != null && _cachedActiveBoneFemale == female
                && _cachedActiveBoneRoot == root
                && _cachedActiveBoneTarget == _activeBoneTarget
                && string.Equals(_cachedActiveBoneName, _activeBoneName, System.StringComparison.Ordinal)
                && now < _nextActiveBoneResolveTime)
                return _cachedActiveBone;

            _cachedActiveBoneFemale = female;
            _cachedActiveBoneRoot = root;
            _cachedActiveBoneTarget = _activeBoneTarget;
            _cachedActiveBoneName = _activeBoneName;
            _cachedActiveBone = ResolveBoneTarget(GetFemaleBoneCache(female), _activeBoneTarget, _activeBoneName);
            _nextActiveBoneResolveTime = now + 0.5f;
            return _cachedActiveBone;
        }

        private static ChaControl ResolveMainFemale()
        {
            HSceneProc proc = ResolveHSceneProcCached();
            if (proc != null)
            {
                IList listObj = HSceneFemaleListField != null ? HSceneFemaleListField.GetValue(proc) as IList : null;
                if (listObj != null)
                {
                    for (int i = 0; i < listObj.Count; i++)
                    {
                        ChaControl cha = listObj[i] as ChaControl;
                        if (cha != null)
                            return _cachedFallbackFemale = cha;
                    }
                }
            }

            if (_cachedFallbackFemale != null)
                return _cachedFallbackFemale;
            float now = Time.unscaledTime;
            if (now < _nextFallbackFemaleSearchTime)
                return null;
            _nextFallbackFemaleSearchTime = now + 0.5f;
            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha != null && cha.sex == 1)
                    return _cachedFallbackFemale = cha;
            }

            return null;
        }

        private static Transform[] GetFemaleBoneCache(ChaControl female)
        {
            if (female == null)
                return null;

            Transform root = female.animBody != null ? female.animBody.transform : female.transform;
            return root != null ? root.GetComponentsInChildren<Transform>(true) : null;
        }

        private static Transform ResolveBoneTarget(Transform[] boneCache, int boneTarget, string preferredName)
        {
            if (boneCache == null)
                return null;

            if (!string.IsNullOrWhiteSpace(preferredName))
            {
                Transform exact = FindBoneByName(boneCache, preferredName);
                if (exact != null)
                    return exact;
            }

            int normalizedTarget = Mathf.Clamp(boneTarget, 0, BoneTargetNameCandidates.Length - 1);
            string[] candidates = BoneTargetNameCandidates[normalizedTarget];
            for (int i = 0; i < candidates.Length; i++)
            {
                Transform found = FindBoneByName(boneCache, candidates[i]);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Transform FindBoneByName(Transform[] boneCache, string name)
        {
            if (boneCache == null || string.IsNullOrWhiteSpace(name))
                return null;

            for (int i = 0; i < boneCache.Length; i++)
            {
                Transform current = boneCache[i];
                if (current != null && string.Equals(current.name, name, StringComparison.Ordinal))
                    return current;
            }

            return null;
        }

        private void SetStatus(string text)
        {
            _statusMessage = text ?? string.Empty;
            LogInfo("preset: " + _statusMessage);
        }

        private bool TryLoadPresetByNameInternal(string presetName, out string reason)
        {
            reason = string.Empty;
            string normalized = (presetName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                reason = "preset_name_empty";
                return false;
            }

            SubCameraPreset[] presets = _settings.Presets ?? new SubCameraPreset[0];
            for (int i = 0; i < presets.Length; i++)
            {
                SubCameraPreset preset = presets[i];
                if (preset == null || string.IsNullOrWhiteSpace(preset.Name))
                    continue;

                if (string.Equals(preset.Name.Trim(), normalized, StringComparison.Ordinal))
                {
                    LoadPreset(preset, "bridge");
                    reason = "ok";
                    return true;
                }
            }

            reason = "preset_not_found";
            return false;
        }

        public static bool TryLoadPresetByName(string presetName, out string reason)
        {
            reason = string.Empty;
            if (Instance == null)
            {
                reason = "instance_null";
                return false;
            }

            if (Instance._settings != null && !Instance._settings.VoiceFaceEventSubCameraTriggerEnabled)
            {
                reason = "voiceface_subcamera_trigger_disabled";
                Instance.SetStatus("VoiceFaceEventサブカメラトリガー無効: " + (presetName ?? string.Empty));
                return false;
            }

            try
            {
                return Instance.TryLoadPresetByNameInternal(presetName, out reason);
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                Instance.LogWarn("preset load failed: " + reason);
                return false;
            }
        }

        internal static bool TryGetRenderTextureInternalApi(out UnityEngine.RenderTexture renderTexture, out string reason)
        {
            renderTexture = null;
            reason = string.Empty;
            if (Instance == null)
            {
                reason = "instance_null";
                return false;
            }

            if (Instance._renderTexture == null)
            {
                reason = "render_texture_not_ready";
                return false;
            }

            renderTexture = Instance._renderTexture;
            reason = "ok";
            return true;
        }

        internal static bool TryGetPresetNamesInternalApi(out string[] names, out string reason)
        {
            names = new string[0];
            reason = string.Empty;
            if (Instance == null)
            {
                reason = "instance_null";
                return false;
            }

            SubCameraPreset[] presets = Instance._settings != null ? Instance._settings.Presets : null;
            if (presets == null || presets.Length == 0)
            {
                reason = "no_presets";
                return true;
            }

            List<string> result = new List<string>(presets.Length);
            for (int i = 0; i < presets.Length; i++)
            {
                SubCameraPreset preset = presets[i];
                if (preset == null || string.IsNullOrWhiteSpace(preset.Name))
                    continue;
                result.Add(preset.Name.Trim());
            }

            names = result.ToArray();
            reason = "ok";
            return true;
        }

        private static float[] ToArray(Vector3 value)
        {
            return new[] { Round2Static(value.x), Round2Static(value.y), Round2Static(value.z) };
        }

        private static string FormatArray(float[] values)
        {
            if (values == null || values.Length < 3)
                return "(null)";

            return "("
                + values[0].ToString("0.##") + ","
                + values[1].ToString("0.##") + ","
                + values[2].ToString("0.##") + ")";
        }

        private static Vector3 ToVector3(float[] values, Vector3 fallback)
        {
            if (values == null || values.Length < 3)
                return fallback;

            return new Vector3(values[0], values[1], values[2]);
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
        }

        private static float NormalizeAngle(float angle)
        {
            float normalized = angle;
            while (normalized > 180f) normalized -= 360f;
            while (normalized < -180f) normalized += 360f;
            return normalized;
        }

        private static float Round2Static(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}
