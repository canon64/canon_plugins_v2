using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private long _actorCameraLease;
        private Type _actorCameraApi;
        private MethodInfo _actorCameraAcquire, _actorCameraFollow, _actorCameraPose, _actorCameraRelease, _actorCameraStatusMethod;
        private float _nextActorCameraTry;
        private string _actorCameraStatus;
        private bool _actorCameraRequested;
        public static bool GetActorCameraRequested() => Instance != null && Instance._actorCameraRequested;
        private void ToggleActorCameraFromBar() {
            if (_actorCameraRequested) { _actorCameraRequested = false; ReleaseActorCamera(); _actorCameraStatus = ""; return; }
            if (ReadyIndependentActor() == null) { _presetMessage = "呼出キャラが準備されていません"; return; }
            _actorCameraRequested = true; _nextActorCameraTry = 0; TickActorCamera();
            _presetMessage = _actorCameraStatus;
        }
        private void TickActorCamera() {
            if (!_actorCameraRequested) return;
            var actor = ReadyIndependentActor();
            if (actor == null) return;
            if (_actorCameraLease != 0) return;
            if (Time.unscaledTime < _nextActorCameraTry) return;
            _nextActorCameraTry = Time.unscaledTime + 1;
            if (_actorCameraApi == null) {
                _actorCameraApi = AccessTools.TypeByName("MainGameSubCameraDisplayProbe.ExternalActorCameraApi");
                if (_actorCameraApi == null) { _actorCameraStatus = "サブカメラ連携が利用できません"; return; }
                _actorCameraAcquire = AccessTools.Method(_actorCameraApi, "Acquire"); _actorCameraFollow = AccessTools.Method(_actorCameraApi, "Follow");
                _actorCameraPose = AccessTools.Method(_actorCameraApi, "SetPose"); _actorCameraRelease = AccessTools.Method(_actorCameraApi, "Release"); _actorCameraStatusMethod = AccessTools.Method(_actorCameraApi, "Status");
            }
            try { _actorCameraLease = (long)_actorCameraAcquire.Invoke(null, new object[] { actor }); _actorCameraStatus = _actorCameraLease != 0 ? "サブカメラ追従中" : (string)_actorCameraStatusMethod.Invoke(null, null); }
            catch (Exception ex) { _actorCameraStatus = ex.GetBaseException().Message; }
        }
        private void ReleaseActorCamera() {
            long token = _actorCameraLease; _actorCameraLease = 0;
            if (token != 0) try { _actorCameraRelease?.Invoke(null, new object[] { token }); } catch (Exception ex) { LogWarn("[ActorCamera] release: " + ex.GetBaseException().Message); }
        }
        private void ActorCameraFollow() {
            if (_actorCameraLease != 0 && !(bool)_actorCameraFollow.Invoke(null, new object[] { _actorCameraLease })) _actorCameraLease = 0;
        }
        private void ActorCameraPose(Vector3 position, Quaternion rotation, float fov) {
            if (_actorCameraLease == 0 || _actorStage == null) return;
            if (!(bool)_actorCameraPose.Invoke(null, new object[] { _actorCameraLease, _actorStage.transform.TransformPoint(position), _actorStage.transform.rotation * rotation, fov })) _actorCameraLease = 0;
        }
    }
}
