using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        // メインカメラは Camera.transform を直接固定せず、可能なら MainGameCameraControl の CameraData.Pos をずらす。
        // CameraControl_Ver2.LateUpdate は止めない。戻す先そのものをアストラル側へ移す。

        private Camera _trackedMainCamera;
        private Vector3 _mainCameraOrigPos;
        private Quaternion _mainCameraOrigRot;
        private Transform _mainCameraOrigParent;
        private bool _mainCameraStateCaptured;
        private Vector3 _mainCameraAstralOffset;
        private int _mainCameraTeleportSerial;
        private bool _mainGameCameraControlOffsetApplied;
        private Vector3 _mainGameCameraControlAppliedOffset;
        private float _nextMainCameraRecenterLogTime;

        private static bool _cameraControllerMonitorPatched;
        private static readonly Dictionary<string, Vector3> _cameraControllerBeforeLocal = new Dictionary<string, Vector3>();
        private static readonly Dictionary<string, Vector3> _cameraControllerBeforeWorld = new Dictionary<string, Vector3>();
        private static int _cameraControllerWriteHits;

        internal void TeleportMainCameraToAstral(Vector3 offset)
        {
            Camera cam = ResolveMainCamera();
            if (cam == null)
            {
                SimpleFileLogger.Warn("TeleportMainCamera: Camera.main null");
                return;
            }

            // noisy camera-controller write monitor disabled; keep camera movement path unchanged.

            _trackedMainCamera = cam;
            _mainCameraOrigPos = cam.transform.position;
            _mainCameraOrigRot = cam.transform.rotation;
            _mainCameraOrigParent = cam.transform.parent;
            _mainCameraAstralOffset = offset;
            _mainCameraStateCaptured = true;
            _mainCameraTeleportSerial++;
            _nextMainCameraRecenterLogTime = 0f;
            _cameraControllerWriteHits = 0;
            _cameraControllerBeforeLocal.Clear();
            _cameraControllerBeforeWorld.Clear();

            DumpMainCameraControllerObjects(cam);

            if (TryApplyMainGameCameraControlAstralOffset(offset, "enter"))
            {
                _mainGameCameraControlOffsetApplied = true;
                _mainGameCameraControlAppliedOffset = offset;
                SimpleFileLogger.Info($"MainCamera astral offset applied through MainGameCameraControl offset={offset}, cameraBefore={_mainCameraOrigPos}, cameraAfter={cam.transform.position}");
            }
            else
            {
                _mainGameCameraControlOffsetApplied = false;
                _mainGameCameraControlAppliedOffset = Vector3.zero;
                SimpleFileLogger.Warn("MainGameCameraControl astral offset failed; falling back to one-shot Camera.transform move");
                ApplyMainCameraAstralOffsetOnce("fallback-immediate");
                StartCoroutine(ReapplyMainCameraAstralOffsetOnceAfterLateCameraUpdate(_mainCameraTeleportSerial));
            }

            StartCoroutine(DiagnoseMainCameraDrift(_mainCameraTeleportSerial));
        }

        private bool TryApplyMainGameCameraControlAstralOffset(Vector3 offset, string phase)
        {
            try
            {
                Type type = AccessTools.TypeByName("MainGameCameraControl.Plugin");
                if (type == null)
                {
                    SimpleFileLogger.Warn($"MainGameCameraControl offset skipped: phase={phase}, type not found");
                    return false;
                }

                MethodInfo method = AccessTools.Method(type, "TryApplyAstralWorldOffset", new[] { typeof(Vector3) });
                if (method == null)
                {
                    SimpleFileLogger.Warn($"MainGameCameraControl offset skipped: phase={phase}, method not found");
                    return false;
                }

                object result = method.Invoke(null, new object[] { offset });
                bool ok = result is bool && (bool)result;
                SimpleFileLogger.Info($"MainGameCameraControl offset call phase={phase}, offset={offset}, ok={ok}");
                return ok;
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"MainGameCameraControl offset call failed: phase={phase}, offset={offset}, error={ex.Message}");
                return false;
            }
        }

        internal void RecenterMainCameraAstralOffsetFromCurrent(string reason, bool detail)
        {
            if (!_mainCameraStateCaptured) return;

            Camera cam = _trackedMainCamera != null ? _trackedMainCamera : ResolveMainCamera();
            if (cam == null) return;

            Vector3 expected = _mainCameraOrigPos + _mainCameraAstralOffset;
            Vector3 actual = cam.transform.position;
            Vector3 delta = expected - actual;
            float err = delta.magnitude;
            if (err <= 0.05f) return;

            bool ok;
            if (_mainGameCameraControlOffsetApplied)
            {
                ok = TryApplyMainGameCameraControlAstralOffset(delta, "recenter:" + reason);
            }
            else
            {
                cam.transform.position = expected;
                ok = true;
            }

            if (detail || Time.unscaledTime >= _nextMainCameraRecenterLogTime)
            {
                _nextMainCameraRecenterLogTime = Time.unscaledTime + 1.0f;
                SimpleFileLogger.Warn($"MainCamera astral recenter[{reason}]: expected={expected}, before={actual}, delta={delta}, err={err:F3}, after={cam.transform.position}, ok={ok}, mgccOffset={_mainGameCameraControlOffsetApplied}, state={_state}");
            }
        }

        private static void EnsureCameraControllerMonitorPatched()
        {
            // disabled: this monitor generated too much log noise.
            _cameraControllerMonitorPatched = true;
        }

        private static void PatchCameraControllerMonitor(Harmony harmony, string typeName, string methodName)
        {
            // disabled
        }

        private static void CameraControllerMonitor_Prefix(object __instance, MethodBase __originalMethod)
        {
            // disabled
        }

        private static void CameraControllerMonitor_Postfix(object __instance, MethodBase __originalMethod)
        {
            // disabled
        }

        private static string CameraControllerMonitorKey(object instance, MethodBase method)
        {
            int id = 0;
            var mb = instance as MonoBehaviour;
            if (mb != null) id = mb.GetInstanceID();
            return method.DeclaringType.FullName + "." + method.Name + "#" + id;
        }

        private IEnumerator ReapplyMainCameraAstralOffsetOnceAfterLateCameraUpdate(int serial)
        {
            // fallback only. MainGameCameraControl offset path does not re-apply Camera.transform.
            yield return null;

            if (!_mainCameraStateCaptured || serial != _mainCameraTeleportSerial || _mainGameCameraControlOffsetApplied)
                yield break;

            ApplyMainCameraAstralOffsetOnce("fallback-post-lateupdate-once");
        }

        private IEnumerator DiagnoseMainCameraDrift(int serial)
        {
            const float interval = 1.0f;
            const float duration = 3.0f;
            float elapsed = 0f;
            Vector3 expected = _mainCameraOrigPos + _mainCameraAstralOffset;
            int sample = 0;

            while (elapsed <= duration)
            {
                yield return new WaitForSecondsRealtime(interval);
                elapsed += interval;

                if (!_mainCameraStateCaptured || serial != _mainCameraTeleportSerial)
                    yield break;

                Camera cam = _trackedMainCamera != null ? _trackedMainCamera : ResolveMainCamera();
                if (cam == null)
                {
                    SimpleFileLogger.Warn($"MainCamera drift summary: camera=null elapsed={elapsed:F2}");
                    yield break;
                }

                Vector3 pos = cam.transform.position;
                float err = Vector3.Distance(pos, expected);
                if (sample == 0 || err > 1.0f)
                {
                    SimpleFileLogger.Info($"MainCamera drift summary sample={sample}, elapsed={elapsed:F2}s, expected={expected}, actual={pos}, err={err:F3}, state={_state}, mgccOffset={_mainGameCameraControlOffsetApplied}");
                }

                sample++;
            }
        }

        private void ApplyMainCameraAstralOffsetOnce(string phase)
        {
            Camera cam = _trackedMainCamera != null ? _trackedMainCamera : ResolveMainCamera();
            if (cam == null)
            {
                SimpleFileLogger.Warn($"MainCamera offset apply skipped: phase={phase}, camera=null");
                return;
            }

            _trackedMainCamera = cam;
            Vector3 before = cam.transform.position;
            Vector3 target = _mainCameraOrigPos + _mainCameraAstralOffset;
            cam.transform.position = target;
            SimpleFileLogger.Info($"MainCamera teleported by offset={_mainCameraAstralOffset}, phase={phase}, before={before}, target={target}, after={cam.transform.position}, parent={(_mainCameraOrigParent != null ? _mainCameraOrigParent.name : "null")}, localPos={cam.transform.localPosition}");
        }

        private void DumpMainCameraControllerObjects(Camera cam)
        {
            if (cam == null) return;

            Transform t = cam.transform;
            int depth = 0;
            while (t != null && depth < 6)
            {
                SimpleFileLogger.Info($"MainCamera hierarchy[{depth}]: name={t.name}, id={t.gameObject.GetInstanceID()}, active={t.gameObject.activeInHierarchy}, worldPos={t.position}, localPos={t.localPosition}, worldRot={t.rotation.eulerAngles}, localRot={t.localRotation.eulerAngles}, parent={(t.parent != null ? t.parent.name : "null")}");

                var behaviours = t.GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    var b = behaviours[i];
                    if (b == null)
                    {
                        SimpleFileLogger.Info($"MainCamera hierarchy[{depth}] behaviour[{i}]: null");
                        continue;
                    }

                    SimpleFileLogger.Info($"MainCamera hierarchy[{depth}] behaviour[{i}]: type={b.GetType().FullName}, enabled={b.enabled}");
                }

                t = t.parent;
                depth++;
            }
        }

        internal void MaintainMainCameraAstralOffset()
        {
            if (_state == AstralState.Normal || _state == AstralState.ReturnFadeOut) return;
            RecenterMainCameraAstralOffsetFromCurrent("update", detail: false);
        }

        internal void RestoreMainCameraToReal()
        {
            if (!_mainCameraStateCaptured)
            {
                SimpleFileLogger.Info("RestoreMainCamera: no state captured");
                return;
            }

            if (_mainGameCameraControlOffsetApplied)
            {
                Vector3 restoreOffset = -_mainGameCameraControlAppliedOffset;
                bool ok = TryApplyMainGameCameraControlAstralOffset(restoreOffset, "exit");
                SimpleFileLogger.Info($"MainGameCameraControl astral offset restored offset={restoreOffset}, ok={ok}");
            }
            else if (_trackedMainCamera != null)
            {
                _trackedMainCamera.transform.position = _mainCameraOrigPos;
                _trackedMainCamera.transform.rotation = _mainCameraOrigRot;
                SimpleFileLogger.Info($"MainCamera restored to {_mainCameraOrigPos}, parent={(_mainCameraOrigParent != null ? _mainCameraOrigParent.name : "null")}");
            }

            _mainCameraStateCaptured = false;
            _trackedMainCamera = null;
            _mainCameraOrigParent = null;
            _mainCameraAstralOffset = Vector3.zero;
            _mainGameCameraControlOffsetApplied = false;
            _mainGameCameraControlAppliedOffset = Vector3.zero;
            _cameraControllerBeforeLocal.Clear();
            _cameraControllerBeforeWorld.Clear();
        }

        private Camera ResolveMainCamera()
        {
            Camera cam = Camera.main;
            if (cam != null) return cam;

            foreach (var c in Camera.allCameras)
            {
                if (c != null && c.gameObject != null && c.gameObject.CompareTag("MainCamera"))
                    return c;
            }
            foreach (var c in Camera.allCameras)
            {
                if (c != null && c.targetTexture == null) return c;
            }
            return null;
        }
    }
}
