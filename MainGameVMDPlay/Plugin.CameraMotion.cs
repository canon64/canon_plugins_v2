using System;
using System.IO;
using HarmonyLib;
using MMD4U;
using MMD4U.VMD;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private object _cameraManager;
        private MTSequenceSet _cameraSequence;
        private MMDCamera _cameraData;
        private float _cameraBaseScale;
        private bool _cameraEnabled;
        private Camera _drivenCamera;
        private Vector3 _cameraBeforePosition;
        private Quaternion _cameraBeforeRotation;
        private float _cameraBeforeFov;
        private bool _pickerCamera;
        private string _cameraError = "";

        private void InitCameraMotion()
        {
            _cameraManager = CameraManager;
            if (_cameraManager == null) { _cameraError = "カメラ管理の初期化に失敗しました"; return; }
            _cameraSequence = (MTSequenceSet)ReadMember(_cameraManager, "sequenceSet");
            _cameraData = (MMDCamera)ReadMember(_cameraManager, "mmdCamera");
            _cameraBaseScale = (float)ReadMember(_cameraManager, "_scaleBase");
            new Harmony(Guid + ".camera").Patch(AccessTools.Method(_cameraManager.GetType(), "UpdateCameraPos"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipStudioCamera)));
            var driver = gameObject.AddComponent<MainGameCameraMotionDriver>();
            driver.Owner = this;
        }
        private static bool SkipStudioCamera(object __instance) => Instance == null || !ReferenceEquals(Instance._cameraManager, __instance);
        private void BeginPickCamera() { if (_pickerBusy) return; BeginPickFile(false); _pickerCamera = true; }
        private void LoadCameraMotion(string path)
        {
            if (_cameraManager == null) { _cameraError = "カメラ管理が未初期化です"; return; }
            if (!File.Exists(path)) { _cameraError = "カメラVMDが見つかりません"; return; }
            ReleaseCameraMotion();
            _cameraSequence.KillAll();
            bool ok;
            try { ok = (bool)AccessTools.Method(_cameraManager.GetType(), "SetCameraAnimation").Invoke(_cameraManager, new object[] { path }); }
            catch (Exception ex) { _cameraError = ex.GetBaseException().Message; return; }
            if (!ok || !_cameraSequence.HasSequences() || _cameraSequence.Length <= 0) { _cameraSequence.KillAll(); _cameraError = "有効なカメラモーションがありません（読込失敗またはカメラキーなし）"; return; }
            _settings.CameraVmdPath = path; _cameraError = ""; SaveSettings("camera-vmd");
            ApplySavedAdjustments(false);
            if (_state == PluginState.Playing) PlayCameraMotion();
        }
        private void PlayCameraMotion()
        {
            if (!_cameraEnabled || !_vmdOwnsCharacter) return;
            if (_cameraSequence == null || !_cameraSequence.HasSequences()) { _cameraError = "先にカメラVMDを読み込んでください"; return; }
            if (IsIndependentTarget) {
                _cameraSequence.SetTime(VmdBridgeGetAnimTime(_controller)); _cameraSequence.Play(); return;
            }
            if (_drivenCamera == null) {
                _drivenCamera = Camera.main;
                if (_drivenCamera == null) { _cameraError = "本編カメラが見つかりません"; return; }
                _cameraBeforePosition = _drivenCamera.transform.position;
                _cameraBeforeRotation = _drivenCamera.transform.rotation;
                _cameraBeforeFov = _drivenCamera.fieldOfView;
            }
            _cameraSequence.SetTime(VmdBridgeGetAnimTime(_controller));
            _cameraSequence.Play();
        }
        private void ReleaseCameraMotion()
        {
            _cameraSequence?.StopSequences(false);
            ActorCameraFollow();
            if (_drivenCamera != null) {
                _drivenCamera.transform.SetPositionAndRotation(_cameraBeforePosition, _cameraBeforeRotation);
                _drivenCamera.fieldOfView = _cameraBeforeFov;
            }
            _drivenCamera = null;
        }
        internal void TickCameraMotion()
        {
            if (AstralBlocked) return;
            if (IsIndependentTarget && (!_cameraEnabled || !_vmdOwnsCharacter || _cameraSequence == null || !_cameraSequence.HasSequences())) { ActorCameraFollow(); return; }
            if (!_cameraEnabled || !_vmdOwnsCharacter || (!IsIndependentTarget && _drivenCamera == null) || _cameraSequence == null) return;
            _cameraSequence.UpdateInternal();
            float scale = (float)ReadMember(_cameraManager, "modelScale") * _cameraBaseScale;
            var settings = _piSettingsInstance.GetValue(null, null);
            scale *= (float)ReadMember(ReadMember(settings, "GlobalScale"), "Value");
            Quaternion y = Quaternion.Euler(0, (float)ReadMember(_cameraManager, "cameraRotAdjustY"), 0);
            Vector3 pivot = y * (_cameraData.location * scale) + (Vector3)ReadMember(_cameraManager, "cameraPosAdjust");
            Quaternion rotation = y * Quaternion.Euler(-_cameraData.rotation) * Quaternion.Euler(0, 180, 0);
            Vector3 distance = new Vector3(0, 0, (_cameraData.length - (float)ReadMember(_cameraManager, "cameraDistanceAdjust")) * scale);
            if (IsIndependentTarget) { ActorCameraPose(pivot + rotation * distance, rotation, _cameraData.view_angle); return; }
            _drivenCamera.transform.SetPositionAndRotation(pivot + rotation * distance, rotation);
            _drivenCamera.fieldOfView = _cameraData.view_angle;
        }
        private void DrawCameraMotion()
        {
            GUILayout.Label("カメラVMD: " + Path.GetFileName(_settings.CameraVmdPath ?? ""));
            GUILayout.Label(_cameraSequence != null && _cameraSequence.HasSequences() ? "カメラ読込済み: " + _cameraSequence.Length.ToString("0.00") + "秒 / " + (_cameraEnabled ? "有効" : "無効（VMDカメラを使用をON）") : "カメラ未読込");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("カメラVMDを選択")) BeginPickCamera();
            if (GUILayout.Button("カメラVMDを解除")) { ReleaseCameraMotion(); _cameraSequence?.KillAll(); _settings.CameraVmdPath = ""; SaveSettings("camera-clear"); }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_cameraError)) GUILayout.Label(_cameraError);
        }
    }
    [DefaultExecutionOrder(31000)]
    public sealed class MainGameCameraMotionDriver : MonoBehaviour
    {
        internal Plugin Owner;
        private void LateUpdate() { if (Owner != null) Owner.TickCameraMotion(); }
    }
}
