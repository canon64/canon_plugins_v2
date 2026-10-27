using System;
using UnityEngine;
namespace MainGameSubCameraDisplayProbe
{
    public static class ExternalActorCameraApi
    {
        public static string Status() => Plugin.Instance == null ? "サブカメラ未起動" : Plugin.Instance.ActorCameraAvailability();
        public static long Acquire(ChaControl actor) => Plugin.Instance == null ? 0 : Plugin.Instance.AcquireActor(actor);
        public static bool SetPose(long token, Vector3 position, Quaternion rotation, float fov) => Plugin.Instance != null && Plugin.Instance.SetActorPose(token, position, rotation, fov);
        public static bool Follow(long token) => Plugin.Instance != null && Plugin.Instance.FollowActor(token);
        public static void Release(long token) { if (Plugin.Instance != null && Plugin.Instance._actorLease == token) Plugin.Instance.ReleaseActor(); }
    }
    public sealed partial class Plugin
    {
        internal long _actorLease;
        private long _actorLeaseCounter;
        private float _externalAstralStarted;
        private ChaControl _externalActor;
        private Camera _externalCamera;
        private Vector3 _externalBeforePosition;
        private Quaternion _externalBeforeRotation;
        private float _externalBeforeFov;
        private Vector3 _externalBeforeAnchorPosition, _externalBeforeCameraLocalPosition;
        private Quaternion _externalBeforeAnchorRotation, _externalBeforeCameraLocalRotation;
        private bool _externalPose;
        private bool _externalGripPose;
        private Transform _externalBone, _externalBoneRoot;
        private int _externalBoneChoice = -1;
        private Vector3 _externalLookOffset;
        private bool ExternalLookEditable => ActorLeaseValid(_actorLease) && (_externalPreset != null ? _externalPreset.UseBoneLink : !_externalPose);
        private Vector3 _externalOffset = new Vector3(0, .3f, 2.5f);
        private Vector3 _externalSettingPosition;
        private float _externalAcquiredAt, _externalSettingFov;
        private Vector3 _externalOriginalSettingPosition, _externalOriginalSettingRotation;
        private int _externalBeforeBoneChoice;
        private SubCameraPreset _externalPreset;
        private string _externalBeforePresetName;
        private string _externalBeforePresetBuffer;
        private Vector3 _externalPresetStartPosition;
        private Quaternion _externalPresetStartRotation;
        private float _externalPresetStartFov, _externalPresetStarted, _externalPresetDuration;
        private float _externalBeforeCycleNext, _externalBeforeCycleAway;
        private string _externalBeforeCycleName;
        internal string ActorCameraAvailability() {
            if (!CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom) return "BlankMap外のためサブカメラを使用できません";
            if (!_settings.Enabled) return "サブカメラが無効です";
            if (_suspendedForAstralMode) return "Astralがサブカメラを使用中です";
            if (_cameraGizmo != null && _cameraGizmo.IsDragging) return "カメラ操作の終了を待っています";
            if (_actorLease != 0) return "サブカメラは別の所有者が使用中です";
            return "サブカメラの準備を待っています";
        }
        internal long AcquireActor(ChaControl actor) {
            if (actor == null || !_settings.Enabled || _suspendedForAstralMode || !CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom) return 0;
            if (_cameraGizmo != null && _cameraGizmo.IsDragging) return 0;
            if (IsHoldingCameraAnchor()) StopGrip();
            if (_actorLease != 0) return _externalActor == actor ? _actorLease : 0;
            EnsureCameraProbe();
            if (_subCamera == null) return 0;
            RestoreExternalRenderScope();
            _externalAcquiredAt = Time.unscaledTime; _externalBeforeBoneChoice = _settings.SelectedBoneTarget;
            _externalBeforePresetName = _activeCameraPresetName;
            _externalBeforePresetBuffer = _presetNameBuffer;
            _externalBeforeCycleNext = _autoCycleNextAt; _externalBeforeCycleAway = _autoCycleAwayUntil; _externalBeforeCycleName = _autoCycleLastPresetName;
            _externalPreset = null;
            _settings.SelectedBoneTarget = 1;
            _externalActor = actor; _externalCamera = _subCamera;
            _externalBeforeAnchorPosition = _cameraAnchorObject.transform.position;
            _externalBeforeAnchorRotation = _cameraAnchorObject.transform.rotation;
            _externalBeforeCameraLocalPosition = _subCamera.transform.localPosition;
            _externalBeforeCameraLocalRotation = _subCamera.transform.localRotation;
            _externalBeforePosition = _subCamera.transform.position; _externalBeforeRotation = _subCamera.transform.rotation; _externalBeforeFov = _subCamera.fieldOfView;
            _externalSettingPosition = new Vector3(_settings.CameraPosX, _settings.CameraPosY, _settings.CameraPosZ);
            _externalOriginalSettingRotation = new Vector3(_settings.CameraRotX, _settings.CameraRotY, _settings.CameraRotZ);
            _externalOriginalSettingPosition = _externalSettingPosition; _externalSettingFov = _settings.CameraFieldOfView;
            _externalOffset = new Vector3(0, .3f, 2.5f); _externalLookOffset = Vector3.zero; _externalPose = false;
            _externalGripPose = false;
            if (_settings.ExternalGripOffset != null && _settings.ExternalGripOffset.Length == 3)
                _externalOffset = ToVector3(_settings.ExternalGripOffset, _externalOffset);
            _externalBone = null; _externalBoneChoice = -1;
            _charactersOnlyKeepObjects.Clear(); _charactersOnlyCharacterNextRescan = 0;
            _actorLease = ++_actorLeaseCounter;
            return _actorLease;
        }
        internal bool SetActorPose(long token, Vector3 position, Quaternion rotation, float fov) {
            if (!ActorLeaseValid(token)) return false;
            if (IsHoldingCameraAnchor() || (_cameraGizmo != null && _cameraGizmo.IsDragging) || _externalGripPose) return true;
            if (_externalPreset != null) return true; // An explicitly chosen preset owns the shot until follow is re-enabled.
            _externalPose = true;
            SetExternalCameraWorldPose(position, rotation); _externalCamera.fieldOfView = fov;
            return true;
        }
        internal bool FollowActor(long token) { if (!ActorLeaseValid(token)) return false; _externalPose = false; _externalGripPose = false; return true; }
        private bool ActorLeaseValid(long token) => token != 0 && token == _actorLease && _externalActor != null && _externalCamera != null && _externalCamera == _subCamera && _settings.Enabled && !_suspendedForAstralMode && CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom;
        internal ChaControl GetKaleidoscopeCalledActor() => ActorLeaseValid(_actorLease) ? _externalActor : null;
        internal void ReleaseActor() {
            if (_actorLease == 0) return;
            if (IsHoldingCameraAnchor()) StopGrip();
            RestoreExternalRenderScope();
            float elapsed = Time.unscaledTime - _externalAcquiredAt;
            if (_transitionActive) _transitionStartTime += elapsed;
            _autoCycleNextAt = _externalBeforeCycleNext > 0 ? _externalBeforeCycleNext + elapsed : 0;
            _autoCycleAwayUntil = _externalBeforeCycleAway > 0 ? _externalBeforeCycleAway + elapsed : 0;
            _autoCycleLastPresetName = _externalBeforeCycleName; _activeCameraPresetName = _externalBeforePresetName;
            _presetNameBuffer = _externalBeforePresetBuffer;
            _settings.SelectedBoneTarget = _externalBeforeBoneChoice;
            _settings.CameraPosX = _externalOriginalSettingPosition.x; _settings.CameraPosY = _externalOriginalSettingPosition.y; _settings.CameraPosZ = _externalOriginalSettingPosition.z;
            _settings.CameraFieldOfView = _externalSettingFov;
            _settings.CameraRotX = _externalOriginalSettingRotation.x; _settings.CameraRotY = _externalOriginalSettingRotation.y; _settings.CameraRotZ = _externalOriginalSettingRotation.z;
            if (_externalCamera != null && _externalCamera == _subCamera) {
                if (_cameraAnchorObject != null) _cameraAnchorObject.transform.SetPositionAndRotation(_externalBeforeAnchorPosition, _externalBeforeAnchorRotation);
                _externalCamera.transform.localPosition = _externalBeforeCameraLocalPosition;
                _externalCamera.transform.localRotation = _externalBeforeCameraLocalRotation;
                _externalCamera.fieldOfView = _externalBeforeFov;
            }
            _actorLease = 0; _externalActor = null; _externalCamera = null; _externalBone = null;
            _externalPreset = null;
            _charactersOnlyKeepObjects.Clear(); _charactersOnlyCharacterNextRescan = 0;
        }
        private void LateUpdate() {
            if (_suspendedForAstralMode) return;
            if (_actorLease == 0) return;
            if (!ActorLeaseValid(_actorLease)) { ReleaseActor(); return; }
            if (IsHoldingCameraAnchor() || (_cameraGizmo != null && _cameraGizmo.IsDragging)) return;
            if (_externalPreset != null) { UpdateExternalPreset(); return; }
            if (_externalPose) return;
            var root = _externalActor.objBodyBone != null ? _externalActor.objBodyBone.transform : null;
            if (root == null) return;
            if (_externalBone == null || root != _externalBoneRoot || _externalBoneChoice != _settings.SelectedBoneTarget) {
                _externalBoneRoot = root; _externalBoneChoice = _settings.SelectedBoneTarget;
                _externalBone = ResolveBoneTarget(root.GetComponentsInChildren<Transform>(true), _externalBoneChoice, null);
                if (_externalBone == null) _externalBone = root;
            }
            Quaternion heading = _externalActor.transform.parent != null ? _externalActor.transform.parent.rotation : Quaternion.identity;
            Vector3 position = _externalBone.position + heading * _externalOffset;
            Vector3 look = _externalBone.TransformPoint(_externalLookOffset) - position;
            if (look.sqrMagnitude > .0001f) SetExternalCameraWorldPose(position, Quaternion.LookRotation(look));
            _externalCamera.fieldOfView = _settings.CameraFieldOfView;
            SyncExternalCameraPositionDisplay();
        }
        private void SetExternalCameraWorldPose(Vector3 position, Quaternion rotation) {
            if (_externalCamera == null || _cameraAnchorObject == null) return;
            // Normal controls move the anchor. Keep the actual Camera at its origin
            // so releasing external follow cannot leave a second hidden offset.
            _cameraAnchorObject.transform.SetPositionAndRotation(position, rotation);
            _externalCamera.transform.localPosition = Vector3.zero;
            _externalCamera.transform.localRotation = Quaternion.identity;
        }
        private void CaptureExternalGrip() {
            if (_externalCamera == null) return;
            SyncExternalCameraPositionDisplay();
            Vector3 position = _externalCamera.transform.position;
            if (_externalPreset != null) {
                _externalPreset.SaveCameraPosition = true;
                _externalPreset.CameraPosition = ToArray(position);
                _externalPreset.CameraRotation = ToArray(_externalCamera.transform.eulerAngles);
                if (_externalPreset.UseBoneLink && _externalBone != null)
                    _externalPreset.CameraOffsetLocal = ToArray(position - _externalBone.position);
                _externalPresetDuration = 0;
                var saved = FindPresetByName(_activeCameraPresetName);
                if (saved != null) SaveExternalCameraPreset(saved);
            } else if (!_externalPose && _externalBone != null) {
                Quaternion heading = _externalActor.transform.parent != null ? _externalActor.transform.parent.rotation : Quaternion.identity;
                _externalOffset = Quaternion.Inverse(heading) * (position - _externalBone.position);
                _settings.ExternalGripOffset = ToArray(_externalOffset);
            } else {
                // Explicit hand positioning takes ownership from camera VMD.
                _externalGripPose = true;
            }
        }
        private void SyncExternalCameraPositionDisplay() {
            if (_externalCamera == null || _rootObject == null) return;
            Vector3 local = _rootObject.transform.InverseTransformPoint(_externalCamera.transform.position);
            _settings.CameraPosX = local.x; _settings.CameraPosY = local.y; _settings.CameraPosZ = local.z;
        }
        private void SyncExternalCameraSliders(bool positionChanged, bool rotationChanged) {
            if (!ActorLeaseValid(_actorLease) || _rootObject == null) return;
            Vector3 desired = _rootObject.transform.TransformPoint(new Vector3(_settings.CameraPosX, _settings.CameraPosY, _settings.CameraPosZ));
            if (_externalPreset != null) {
                if (positionChanged) {
                    _settings.SaveBoneCameraPosition = true;
                    _externalPreset.SaveCameraPosition = true;
                    _externalPreset.CameraPosition = ToArray(desired);
                    if (_externalPreset.UseBoneLink && _externalBone != null)
                        _externalPreset.CameraOffsetLocal = ToArray(desired - _externalBone.position);
                }
                if (rotationChanged && !_externalPreset.UseBoneLink)
                    _externalPreset.CameraRotation = ToArray((_rootObject.transform.rotation * Quaternion.Euler(_settings.CameraRotX, _settings.CameraRotY, _settings.CameraRotZ)).eulerAngles);
                _externalPresetDuration = 0;
                UpdateExternalPreset(); PersistExternalPresetEdits();
            } else if (!_externalPose && _externalBone != null) {
                if (positionChanged) {
                    _settings.SaveBoneCameraPosition = true;
                    Quaternion heading = _externalActor.transform.parent != null ? _externalActor.transform.parent.rotation : Quaternion.identity;
                    _externalOffset = Quaternion.Inverse(heading) * (desired - _externalBone.position);
                }
                LateUpdate();
            } else {
                SetStatus("カメラVMD制御中です。位置調整は骨追従またはプリセットで行ってください");
                return;
            }
            SetStatus("呼出キャラのカメラ座標を更新");
        }
        private void SetExternalLookOffset(Vector3 offset) {
            if (!ExternalLookEditable) return;
            _externalLookOffset = offset;
            if (_externalPreset != null) {
                _externalPreset.LookAtOffsetLocal = ToArray(offset);
                if (_externalBone != null) _externalPreset.LookAtPosition = ToArray(_externalBone.TransformPoint(offset));
                // An explicit adjustment is immediate, even during preset transition.
                _externalPresetDuration = 0;
                UpdateExternalPreset();
                PersistExternalPresetEdits();
            } else LateUpdate();
            SetStatus("呼出キャラの注視点オフセット更新");
        }
        private void PersistExternalPresetEdits() {
            if (_externalPreset == null) return;
            SubCameraPreset registered = FindPresetByName(_activeCameraPresetName);
            if (registered == null) return;
            registered.ActorCamera = CaptureActorShot(_externalPreset);
            SaveSettings();
        }
        private void SaveExternalCameraPreset(SubCameraPreset existing) {
            if (!ActorLeaseValid(_actorLease)) { SetStatus("呼出キャラのサブカメラ未準備"); return; }
            string name = existing != null ? existing.Name : MakeUniquePresetName(
                string.IsNullOrWhiteSpace(_presetNameBuffer) ? "Preset " + ((_settings.Presets?.Length ?? 0) + 1) : _presetNameBuffer.Trim(), null);
            bool linked = _settings.SelectedSaveMode == SaveModeBoneLink;
            var camera = _externalCamera.transform;
            Transform bone = null;
            if (linked) {
                var root = _externalActor.objBodyBone;
                if (root != null) bone = ResolveBoneTarget(root.GetComponentsInChildren<Transform>(true), _settings.SelectedBoneTarget, null);
                if (bone == null) { SetStatus("保存失敗: 呼出キャラの対象ボーンがありません"); return; }
            }
            var preset = existing ?? new SubCameraPreset();
            var shot = new SubCameraPreset();
            shot.UseBoneLink = linked;
            shot.SaveCameraPosition = !linked || _settings.SaveBoneCameraPosition;
            shot.CameraPosition = shot.SaveCameraPosition ? ToArray(camera.position) : null;
            shot.CameraRotation = ToArray(NormalizeEuler(camera.eulerAngles));
            shot.Fov = _externalCamera.fieldOfView;
            shot.BoneTarget = _settings.SelectedBoneTarget;
            shot.BoneName = bone != null ? bone.name : "";
            shot.CameraOffsetLocal = bone != null && shot.SaveCameraPosition ? ToArray(camera.position - bone.position) : null;
            Vector3 offset = _externalLookOffset;
            shot.LookAtOffsetLocal = linked ? ToArray(offset) : null;
            shot.LookAtPosition = bone != null ? ToArray(bone.TransformPoint(offset)) : null;
            preset.Name = name;
            preset.ActorCamera = CaptureActorShot(shot);
            if (existing == null) {
                // A newly created actor preset still has a usable base view if
                // it is selected later without an H pose variant.
                preset.UseBoneLink = shot.UseBoneLink; preset.SaveCameraPosition = shot.SaveCameraPosition;
                preset.CameraPosition = shot.CameraPosition; preset.CameraRotation = shot.CameraRotation;
                preset.Fov = shot.Fov; preset.BoneTarget = shot.BoneTarget; preset.BoneName = shot.BoneName;
                preset.CameraOffsetLocal = shot.CameraOffsetLocal; preset.LookAtOffsetLocal = shot.LookAtOffsetLocal;
                preset.LookAtPosition = shot.LookAtPosition;
                preset.UsePoseOverrides = false; preset.PoseOverrides = new CameraPoseOverride[0];
                var presets = new System.Collections.Generic.List<SubCameraPreset>(_settings.Presets ?? new SubCameraPreset[0]);
                presets.Add(preset); _settings.Presets = presets.ToArray();
            }
            // Existing H base and pose variants stay separate from the actor shot.
            ApplyExternalPreset(preset, "save");
            _externalPresetDuration = 0;
            SaveSettings();
            SetStatus("呼出キャラのカメラ保存: " + name);
        }
        private void ApplyExternalPreset(SubCameraPreset preset, string source) {
            if (preset == null || !ActorLeaseValid(_actorLease)) return;
            // H pose overrides do not describe an independent dancer.
            // Live sliders must not mutate the registered shot. Only explicit save
            // writes back to _settings.Presets; reloading starts from saved values.
            SubCameraPreset actorPreset = ResolveActorShot(preset);
            _externalPreset = actorPreset;
            _externalLookOffset = ToVector3(actorPreset.LookAtOffsetLocal, Vector3.zero);
            _externalPresetStartPosition = _externalCamera.transform.position;
            _externalPresetStartRotation = _externalCamera.transform.rotation;
            _externalPresetStartFov = _externalCamera.fieldOfView;
            _externalPresetStarted = Time.unscaledTime;
            _externalPresetDuration = Mathf.Clamp(_settings.TransitionSeconds, 0, 3);
            _externalBone = null; _externalBoneChoice = -1;
            _activeCameraPresetName = preset.Name ?? ""; _presetNameBuffer = _activeCameraPresetName;
            _settings.SelectedBoneTarget = actorPreset.BoneTarget;
            _settings.SelectedSaveMode = actorPreset.UseBoneLink ? SaveModeBoneLink : SaveModeNormal;
            _settings.SaveBoneCameraPosition = actorPreset.SaveCameraPosition;
            _settings.CameraFieldOfView = Mathf.Clamp(actorPreset.Fov > 0 ? actorPreset.Fov : _externalCamera.fieldOfView, 10, 170);
            NotifyAutoCycleLoadPreset(preset, source);
            UpdateExternalPreset();
            SetStatus("呼出キャラのプリセット: " + preset.Name);
        }
        private void UpdateExternalPreset() {
            var preset = _externalPreset;
            Vector3 position = ToVector3(preset.CameraPosition, _externalPresetStartPosition);
            Quaternion rotation = Quaternion.Euler(ToVector3(preset.CameraRotation, _externalPresetStartRotation.eulerAngles));
            if (preset.UseBoneLink) {
                var root = _externalActor.objBodyBone != null ? _externalActor.objBodyBone.transform : null;
                if (root == null) return;
                if (_externalBone == null || _externalBoneRoot != root || _externalBoneChoice != preset.BoneTarget) {
                    _externalBoneRoot = root; _externalBoneChoice = preset.BoneTarget;
                    _externalBone = ResolveBoneTarget(root.GetComponentsInChildren<Transform>(true), preset.BoneTarget, preset.BoneName);
                }
                if (_externalBone == null) return;
                // Existing presets store a world-space offset despite the historical field name.
                if (preset.SaveCameraPosition && preset.CameraOffsetLocal != null && preset.CameraOffsetLocal.Length >= 3)
                    position = _externalBone.position + ToVector3(preset.CameraOffsetLocal, Vector3.zero);
                else if (!HasCameraPosition(preset))
                    position = _externalPresetStartPosition;
                Vector3 look = _externalBone.TransformPoint(_externalLookOffset) - position;
                if (look.sqrMagnitude > .000001f) rotation = Quaternion.LookRotation(look, Vector3.up);
            }
            float t = _externalPresetDuration <= 0 ? 1 : Mathf.Clamp01((Time.unscaledTime - _externalPresetStarted) / _externalPresetDuration);
            t = t * t * (3 - 2 * t);
            SetExternalCameraWorldPose(Vector3.Lerp(_externalPresetStartPosition, position, t), Quaternion.Slerp(_externalPresetStartRotation, rotation, t));
            _externalCamera.fieldOfView = Mathf.Lerp(_externalPresetStartFov, _settings.CameraFieldOfView, t);
            SyncExternalCameraPositionDisplay();
        }
        private Camera _renderScopeCamera;
        private void RestoreExternalRenderScope() {
            if (_charactersOnlyApplying && _renderScopeCamera != null) _renderScopeCamera.cullingMask = _charactersOnlySavedCullingMask;
            RestoreCharactersOnlySwapped(); _charactersOnlyApplying = false; _renderScopeCamera = null;
        }
    }
}
