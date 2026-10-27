using System;
using System.Linq;
using UnityEngine;
namespace MainGameSubCameraDisplayProbe {
 public sealed partial class Plugin {
  public object[] GetPlaybackCameraPresetState() => new object[] { _settings?.Presets != null && _settings.Presets.Any(p => p != null), _settings != null && _settings.AutoCycleEnabled, _activeCameraPresetName ?? "" };
  public bool GetPlaybackCameraGizmo() => _settings != null && _settings.CameraGizmoVisible;
  public bool CanOverwritePlaybackCameraPose() => _cameraAnchorObject != null && FindPresetByName(_activeCameraPresetName) != null
   && (_actorLease != 0 ? ActorLeaseValid(_actorLease) : TryGetCurrentPoseInfo(out _, out _));
  public void OverwritePlaybackCameraPose() {
   if (!CanOverwritePlaybackCameraPose()) return;
   var preset = FindPresetByName(_activeCameraPresetName);
   if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.kks.maingame.cameracontrol", out var info)) { SetStatus("メインカメラ連携が利用できません"); return; }
   var data = info.Instance.GetType().GetMethod("CaptureMainCameraForSubCamera")?.Invoke(info.Instance, null) as object[];
   if (data == null || data.Length < 9) { SetStatus("メインカメラの状態を取得できません"); return; }
   // The source offset belongs to the main camera's target. Rebase the visible
   // world shot onto the bone that this subcamera will actually follow.
   bool boneLinked = (bool)data[3];
   Vector3 cameraWorld = (Vector3)data[0];
   Vector3 lookWorld = (Vector3)data[8];
   Vector3 lookOffset = (Vector3)data[6];
   Vector3 cameraOffset = (Vector3)data[7];
   string boneName = (string)data[5];
   if (boneLinked) {
    Transform bone = null;
    if (_actorLease != 0) {
     var root = _externalActor != null ? _externalActor.objBodyBone : null;
     if (root != null) bone = ResolveBoneTarget(root.GetComponentsInChildren<Transform>(true), (int)data[4], boneName);
    } else {
     bone = ResolveBoneTarget(GetFemaleBoneCache(ResolveMainFemale()), (int)data[4], boneName);
    }
    if (bone == null) { SetStatus("保存失敗: サブカメラの対象ボーンがありません"); return; }
    boneName = bone.name;
    lookOffset = bone.InverseTransformPoint(lookWorld);
    cameraOffset = cameraWorld - bone.position;
    LogInfo("playback camera save rebase target=" + bone.name
     + " sourceWorld=" + cameraWorld.ToString("0.000")
     + " targetBone=" + bone.position.ToString("0.000")
     + " offset=" + cameraOffset.ToString("0.000"));
   }
   if (_actorLease != 0) {
    var shot = new SubCameraPreset {
     UseBoneLink = boneLinked, BoneTarget = (int)data[4], BoneName = boneName,
     SaveCameraPosition = true, CameraPosition = ToArray(cameraWorld),
     CameraRotation = ToArray((Vector3)data[1]), Fov = (float)data[2],
     LookAtOffsetLocal = ToArray(lookOffset),
     CameraOffsetLocal = ToArray(cameraOffset),
     LookAtPosition = ToArray(lookWorld)
    };
    preset.ActorCamera = CaptureActorShot(shot);
    SaveSettings(); ApplyExternalPreset(preset, "save");
    // A manual overwrite takes effect at once, including after the subcamera
    // position sliders changed the previous live actor shot.
    _externalPresetDuration = 0;
    UpdateExternalPreset();
    SetStatus("メインカメラから呼出キャラのカメラ上書き: " + preset.Name);
    return;
   }
   if (!TryGetCurrentPoseInfo(out string key, out string label)) return;
   var pose = FindCameraPoseOverride(preset, key);
   if (pose == null) {
    var items = new System.Collections.Generic.List<CameraPoseOverride>(preset.PoseOverrides ?? new CameraPoseOverride[0]);
    pose = new CameraPoseOverride { Key = key }; items.Add(pose); preset.PoseOverrides = items.ToArray();
   }
   pose.DisplayName = label;
   pose.SaveCameraPosition = true;
   pose.CameraPosition = ToArray(cameraWorld);
   pose.CameraRotation = ToArray((Vector3)data[1]);
   pose.Fov = (float)data[2]; pose.UseBoneLink = boneLinked;
   pose.BoneTarget = (int)data[4]; pose.BoneName = boneName;
   pose.LookAtOffsetLocal = ToArray(lookOffset);
   pose.CameraOffsetLocal = ToArray(cameraOffset);
   pose.LookAtPosition = ToArray(lookWorld);
   preset.UsePoseOverrides = true;
   SaveSettings(); LoadPreset(preset);
   SetStatus("メインカメラから体位上書き: " + preset.Name + " / " + label);
  }
  public void TogglePlaybackCameraGizmo() {
   if (_settings == null) return;
   _settings.CameraGizmoVisible = !_settings.CameraGizmoVisible;
   if (_settings.CameraGizmoVisible) EnsureCameraProbe();
   SyncGizmoState(); SaveSettings();
  }
  public void PlaybackCameraPresetCommand(int direction) {
   if (_settings == null) return;
   if (direction == 0) {
    _settings.AutoCycleEnabled = !_settings.AutoCycleEnabled;
    _autoCycleAwayUntil = 0;
    _autoCycleNextAt = UnityEngine.Time.unscaledTime;
    if (_settings.AutoCycleEnabled) EnsureCameraProbe();
    SaveSettings(); return;
   }
   var presets = _settings.Presets?.Where(p => p != null).ToArray();
   if (presets == null || presets.Length == 0) return;
   int index = Array.FindIndex(presets, p => p.Name == _activeCameraPresetName);
   int next = index < 0 ? (direction < 0 ? presets.Length - 1 : 0) : (index + (direction < 0 ? -1 : 1) + presets.Length) % presets.Length;
   LoadPreset(presets[next]);
  }
 }
}
