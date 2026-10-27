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
        private static bool _poseChangeTriggersPatched;
        private static readonly HashSet<Type> _defCamCtrlLateUpdatePatchedTypes = new HashSet<Type>();
        private static readonly HashSet<Type> _cameraControlLateUpdatePatchedTypes = new HashSet<Type>();
        private static readonly HashSet<Type> _cameraControlSetDataPatchedTypes = new HashSet<Type>();
        private static readonly Dictionary<int, int> _defCamCtrlLateUpdateLogCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _cameraControlLateUpdateLogCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _cameraControlSetDataLogCounts = new Dictionary<int, int>();
        private int _poseChangeTriggerHits;
        private float _poseChangeReapplyUntil;
        private string _poseChangeReapplyReason;
        private int _poseChangeHoldLogCount;

        private static void PatchPoseChangeTriggers(Harmony harmony)
        {
            if (_poseChangeTriggersPatched) return;
            _poseChangeTriggersPatched = true;

            var target = AccessTools.Method(typeof(HSceneProc), "ChangeAnimator");
            var prefix = AccessTools.Method(typeof(Plugin), nameof(HSceneProc_ChangeAnimator_Prefix));
            var postfix = AccessTools.Method(typeof(Plugin), nameof(HSceneProc_ChangeAnimator_Postfix));
            if (target == null || prefix == null || postfix == null)
            {
                SimpleFileLogger.Warn($"PoseChange trigger patch failed: HSceneProc.ChangeAnimator target={(target != null ? "OK" : "null")}, prefix={(prefix != null ? "OK" : "null")}, postfix={(postfix != null ? "OK" : "null")}");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
            SimpleFileLogger.Info("Patched pose-change trigger: HSceneProc.ChangeAnimator prefix/postfix");
        }

        private static void HSceneProc_ChangeAnimator_Prefix(HSceneProc __instance, MethodBase __originalMethod)
        {
            Plugin inst = Instance;
            if (inst == null) return;
            if (inst._state == AstralState.Normal || inst._state == AstralState.ReturnFadeOut) return;

            string methodName = __originalMethod != null ? __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name : "HSceneProc.ChangeAnimator";
            string reason = "pose-change:" + methodName;
            // verbose pose-change diagnostics disabled
        }

        private static void HSceneProc_ChangeAnimator_Postfix(HSceneProc __instance, MethodBase __originalMethod)
        {
            Plugin inst = Instance;
            if (inst == null) return;
            if (inst._state == AstralState.Normal || inst._state == AstralState.ReturnFadeOut) return;
            inst.OnPoseChangeTriggered(__originalMethod);
        }

        private void OnPoseChangeTriggered(MethodBase method)
        {
            _poseChangeTriggerHits++;
            string methodName = method != null ? method.DeclaringType.FullName + "." + method.Name : "HSceneProc.ChangeAnimator";
            string reason = "pose-change:" + methodName;
            _poseChangeReapplyUntil = Time.unscaledTime + 2.0f;
            _poseChangeReapplyReason = reason;
            _poseChangeHoldLogCount = 0;
            _defCamCtrlLateUpdateLogCounts.Clear();
            _cameraControlLateUpdateLogCounts.Clear();
            _cameraControlSetDataLogCounts.Clear();

            SimpleFileLogger.Warn($"POSE_CHANGE_TRIGGER hit={_poseChangeTriggerHits}, method={methodName}, state={_state}, holdUntil={_poseChangeReapplyUntil:F3}");
            ReapplyAstralCharaOffsetFromCurrentPose(reason + ":immediate", detail: false);
        }

        private IEnumerator PoseChangeCameraDiagnosticsCoroutine(string reason, int serial)
        {
            yield return new WaitForEndOfFrame();
            if (serial == _mainCameraTeleportSerial)
            {
                DumpPoseChangeCharaDiagnostics(reason, "same-frame-end-of-frame");
                DumpFrozenClonePosture(reason, "same-frame-end-of-frame");
                DumpPoseChangeCameraDiagnostics(reason, "same-frame-end-of-frame");
            }

            yield return null;
            if (serial == _mainCameraTeleportSerial)
            {
                DumpPoseChangeCharaDiagnostics(reason, "after-yield-null");
                DumpFrozenClonePosture(reason, "after-yield-null");
                DumpPoseChangeCameraDiagnostics(reason, "after-yield-null");
            }

            yield return new WaitForEndOfFrame();
            if (serial == _mainCameraTeleportSerial)
            {
                DumpPoseChangeCharaDiagnostics(reason, "after-end-of-frame");
                DumpFrozenClonePosture(reason, "after-end-of-frame");
                DumpPoseChangeCameraDiagnostics(reason, "after-end-of-frame");
            }

            yield return new WaitForSecondsRealtime(0.2f);
            if (serial == _mainCameraTeleportSerial)
            {
                DumpPoseChangeCharaDiagnostics(reason, "after-0.2s");
                DumpFrozenClonePosture(reason, "after-0.2s");
                DumpPoseChangeCameraDiagnostics(reason, "after-0.2s");
            }

            yield return new WaitForSecondsRealtime(0.5f);
            if (serial == _mainCameraTeleportSerial)
            {
                DumpPoseChangeCharaDiagnostics(reason, "after-0.7s");
                DumpFrozenClonePosture(reason, "after-0.7s");
                DumpPoseChangeCameraDiagnostics(reason, "after-0.7s");
            }
        }

        private void DumpPoseChangeCharaDiagnostics(string reason, string phase)
        {
            try
            {
                if (_charaSnapshots.Count == 0)
                {
                    SimpleFileLogger.Warn("POSE_CHARA_DIAG reason=" + reason + " phase=" + phase + " state=" + _state + " snapshots=0");
                    return;
                }

                for (int i = 0; i < _charaSnapshots.Count; i++)
                {
                    TransformSnapshot snap = _charaSnapshots[i];
                    if (snap == null || snap.Target == null)
                    {
                        SimpleFileLogger.Warn("POSE_CHARA_DIAG reason=" + reason + " phase=" + phase + " index=" + i + " target=null");
                        continue;
                    }

                    Vector3 actual = snap.Target.position;
                    Vector3 expected = snap.WorldPos + _astralCharaOffset;
                    float err = Vector3.Distance(actual, expected);
                    Transform parent = snap.Target.parent;
                    SimpleFileLogger.Warn(
                        "POSE_CHARA_DIAG"
                        + " reason=" + reason
                        + " phase=" + phase
                        + " index=" + i
                        + " name=" + snap.Target.name
                        + " state=" + _state
                        + " actual=" + FormatDiagVector(actual)
                        + " savedReal=" + FormatDiagVector(snap.WorldPos)
                        + " expected=" + FormatDiagVector(expected)
                        + " offset=" + FormatDiagVector(_astralCharaOffset)
                        + " err=" + err.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                        + " parent=" + (parent != null ? parent.name : "null"));
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn("POSE_CHARA_DIAG failed reason=" + reason + " phase=" + phase + " error=" + ex.Message);
            }
        }

        // 偽物(凍結クローン)の実形状が体位変更で動いているかを計測する。
        // クローンは静的ベイクのはずなので、renderer.bounds が phase 間で変化したら
        // 「何かがクローンの transform / メッシュを動かしている」動かぬ証拠になる。
        // 特に足(最下部パーツ)を名指しで追う。
        private void DumpFrozenClonePosture(string reason, string phase)
        {
            try
            {
                if (_frozenClones.Count == 0)
                {
                    SimpleFileLogger.Warn($"CLONE_POSTURE reason={reason} phase={phase} state={_state} clones=0");
                    return;
                }

                for (int i = 0; i < _frozenClones.Count; i++)
                {
                    var c = _frozenClones[i];
                    if (c == null || c.Root == null)
                    {
                        SimpleFileLogger.Warn($"CLONE_POSTURE reason={reason} phase={phase} index={i} root=null");
                        continue;
                    }

                    MeshRenderer[] renderers = c.Root.GetComponentsInChildren<MeshRenderer>(true);
                    Bounds total = default(Bounds);
                    bool hasBounds = false;
                    Vector3 signature = Vector3.zero; // 全パーツ bounds.center の総和: どこか動けば変わる
                    var sorted = new List<MeshRenderer>();
                    foreach (var r in renderers)
                    {
                        if (r == null) continue;
                        Bounds b = r.bounds;
                        signature += b.center;
                        if (!hasBounds) { total = b; hasBounds = true; }
                        else total.Encapsulate(b);
                        sorted.Add(r);
                    }

                    // 最下部(足)から順に
                    sorted.Sort((a, b) => a.bounds.min.y.CompareTo(b.bounds.min.y));

                    SimpleFileLogger.Warn(
                        "CLONE_POSTURE"
                        + " reason=" + reason
                        + " phase=" + phase
                        + " state=" + _state
                        + " index=" + i
                        + " root=" + c.Root.name
                        + " rootPos=" + c.Root.transform.position.ToString("F4")
                        + " rootRot=" + c.Root.transform.rotation.eulerAngles.ToString("F3")
                        + " savedWorld=" + c.WorldPos.ToString("F4")
                        + " parts=" + renderers.Length
                        + " boundsCenter=" + (hasBounds ? total.center.ToString("F4") : "none")
                        + " boundsSize=" + (hasBounds ? total.size.ToString("F4") : "none")
                        + " sig=" + signature.ToString("F4"));

                    int dumpCount = Math.Min(5, sorted.Count);
                    for (int k = 0; k < dumpCount; k++)
                    {
                        MeshRenderer r = sorted[k];
                        Bounds b = r.bounds;
                        SimpleFileLogger.Warn(
                            "CLONE_POSTURE_PART"
                            + " reason=" + reason
                            + " phase=" + phase
                            + " index=" + i
                            + " rank=" + k
                            + " name=" + r.name
                            + " tPos=" + r.transform.position.ToString("F4")
                            + " bCenter=" + b.center.ToString("F4")
                            + " bMin=" + b.min.ToString("F4")
                            + " bSize=" + b.size.ToString("F4"));
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"CLONE_POSTURE failed reason={reason} phase={phase} error={ex.Message}");
            }
        }

        private void DumpPoseChangeCameraDiagnostics(string reason, string phase)
        {
            try
            {
                Camera cam = _trackedMainCamera != null ? _trackedMainCamera : ResolveMainCamera();
                Transform camT = cam != null ? cam.transform : null;
                Transform baseT = camT != null ? camT.parent : null;
                object ctrl = cam != null ? cam.GetComponent("CameraControl_Ver2") : null;
                object def = cam != null ? cam.GetComponent("ksPlug.HSession.DefCamCtrl") : null;
                object[] behaviours = cam != null ? cam.GetComponents<MonoBehaviour>() : new object[0];
                EnsureCameraControlLateUpdatePatched(ctrl);
                EnsureCameraControlSetDataPatched(ctrl);
                EnsureDefCamCtrlLateUpdatePatched(def);

                Vector3 dataPos = Vector3.zero;
                Vector3 dataDir = Vector3.zero;
                Vector3 dataRot = Vector3.zero;
                float dataFov = -1f;
                bool dataOk = TryReadCameraControlData(ctrl, out dataPos, out dataDir, out dataRot, out dataFov);
                Transform transBase = TryReadTransformMember(ctrl, "transBase");
                bool defEnabled = IsBehaviourEnabled(def);
                string ctrlState = FormatCameraControlFields(ctrl);
                string defState = FormatDefCamCtrlFields(def);

                SimpleFileLogger.Warn(
                    "POSE_CAMERA_DIAG"
                    + " reason=" + reason
                    + " phase=" + phase
                    + " state=" + _state
                    + " astralOffset=" + FormatDiagVector(_mainCameraAstralOffset)
                    + " mgccOffset=" + _mainGameCameraControlOffsetApplied
                    + " camWorld=" + FormatDiagVector(camT != null ? camT.position : Vector3.zero)
                    + " camLocal=" + FormatDiagVector(camT != null ? camT.localPosition : Vector3.zero)
                    + " camParent=" + (baseT != null ? baseT.name : "null")
                    + " baseWorld=" + FormatDiagVector(baseT != null ? baseT.position : Vector3.zero)
                    + " baseLocal=" + FormatDiagVector(baseT != null ? baseT.localPosition : Vector3.zero)
                    + " transBaseWorld=" + FormatDiagVector(transBase != null ? transBase.position : Vector3.zero)
                    + " dataOk=" + dataOk
                    + " dataPos=" + FormatDiagVector(dataPos)
                    + " dataDir=" + FormatDiagVector(dataDir)
                    + " dataRot=" + FormatDiagVector(dataRot)
                    + " dataFov=" + dataFov.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    + " defCamCtrl=" + (def != null ? "found" : "null")
                    + " defEnabled=" + defEnabled
                    + " ctrlFields=" + ctrlState
                    + " defFields=" + defState
                    + " camBehaviours=" + FormatCameraBehaviours(behaviours));
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn("POSE_CAMERA_DIAG failed reason=" + reason + " phase=" + phase + " error=" + ex.Message);
            }
        }

        private static void EnsureDefCamCtrlLateUpdatePatched(object def)
        {
            if (def == null) return;

            Type type = def.GetType();
            if (type == null) return;
            if (_defCamCtrlLateUpdatePatchedTypes.Contains(type)) return;

            try
            {
                MethodInfo target = type.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo prefix = AccessTools.Method(typeof(Plugin), nameof(DefCamCtrl_LateUpdate_Prefix));
                MethodInfo postfix = AccessTools.Method(typeof(Plugin), nameof(DefCamCtrl_LateUpdate_Postfix));
                if (target == null || prefix == null || postfix == null)
                {
                    SimpleFileLogger.Warn(
                        "DEF_CAM_CTRL_DIAG patch failed"
                        + " type=" + type.FullName
                        + " target=" + (target != null ? "OK" : "null")
                        + " prefix=" + (prefix != null ? "OK" : "null")
                        + " postfix=" + (postfix != null ? "OK" : "null"));
                    _defCamCtrlLateUpdatePatchedTypes.Add(type);
                    return;
                }

                var harmony = new Harmony(Guid + ".defcamctrl.diag");
                harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                _defCamCtrlLateUpdatePatchedTypes.Add(type);
                SimpleFileLogger.Warn("DEF_CAM_CTRL_DIAG patched type=" + type.FullName + " method=LateUpdate");
            }
            catch (Exception ex)
            {
                _defCamCtrlLateUpdatePatchedTypes.Add(type);
                SimpleFileLogger.Warn("DEF_CAM_CTRL_DIAG patch exception type=" + type.FullName + " error=" + ex.Message);
            }
        }

        private static void EnsureCameraControlLateUpdatePatched(object ctrl)
        {
            if (ctrl == null) return;

            Type type = ctrl.GetType();
            if (type == null) return;
            if (_cameraControlLateUpdatePatchedTypes.Contains(type)) return;

            try
            {
                MethodInfo target = type.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo prefix = AccessTools.Method(typeof(Plugin), nameof(CameraControl_LateUpdate_Prefix));
                MethodInfo postfix = AccessTools.Method(typeof(Plugin), nameof(CameraControl_LateUpdate_Postfix));
                if (target == null || prefix == null || postfix == null)
                {
                    SimpleFileLogger.Warn(
                        "CAMERA_CONTROL_DIAG patch failed"
                        + " type=" + type.FullName
                        + " target=" + (target != null ? "OK" : "null")
                        + " prefix=" + (prefix != null ? "OK" : "null")
                        + " postfix=" + (postfix != null ? "OK" : "null"));
                    _cameraControlLateUpdatePatchedTypes.Add(type);
                    return;
                }

                var harmony = new Harmony(Guid + ".cameracontrol.diag");
                harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                _cameraControlLateUpdatePatchedTypes.Add(type);
                SimpleFileLogger.Warn("CAMERA_CONTROL_DIAG patched type=" + type.FullName + " method=LateUpdate");
            }
            catch (Exception ex)
            {
                _cameraControlLateUpdatePatchedTypes.Add(type);
                SimpleFileLogger.Warn("CAMERA_CONTROL_DIAG patch exception type=" + type.FullName + " error=" + ex.Message);
            }
        }

        private static void EnsureCameraControlSetDataPatched(object ctrl)
        {
            if (ctrl == null) return;

            Type type = ctrl.GetType();
            if (type == null) return;
            if (_cameraControlSetDataPatchedTypes.Contains(type)) return;

            try
            {
                MethodInfo target = null;
                MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || method.Name != "SetCameraData") continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1)
                    {
                        target = method;
                        break;
                    }
                }

                MethodInfo prefix = AccessTools.Method(typeof(Plugin), nameof(CameraControl_SetCameraData_Prefix));
                MethodInfo postfix = AccessTools.Method(typeof(Plugin), nameof(CameraControl_SetCameraData_Postfix));
                if (target == null || prefix == null || postfix == null)
                {
                    SimpleFileLogger.Warn(
                        "CAMERA_SETDATA_DIAG patch failed"
                        + " type=" + type.FullName
                        + " target=" + (target != null ? "OK" : "null")
                        + " prefix=" + (prefix != null ? "OK" : "null")
                        + " postfix=" + (postfix != null ? "OK" : "null"));
                    _cameraControlSetDataPatchedTypes.Add(type);
                    return;
                }

                var harmony = new Harmony(Guid + ".camerasetdata.diag");
                harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                _cameraControlSetDataPatchedTypes.Add(type);
                SimpleFileLogger.Warn("CAMERA_SETDATA_DIAG patched type=" + type.FullName + " method=" + target);
            }
            catch (Exception ex)
            {
                _cameraControlSetDataPatchedTypes.Add(type);
                SimpleFileLogger.Warn("CAMERA_SETDATA_DIAG patch exception type=" + type.FullName + " error=" + ex.Message);
            }
        }

        private static void CameraControl_SetCameraData_Prefix(object __instance, object __0)
        {
            Plugin inst = Instance;
            if (inst == null) return;
            inst.LogCameraControlSetCameraData(__instance, __0, "before");
        }

        private static void CameraControl_SetCameraData_Postfix(object __instance, object __0)
        {
            Plugin inst = Instance;
            if (inst == null) return;
            inst.LogCameraControlSetCameraData(__instance, __0, "after");
        }

        private void LogCameraControlSetCameraData(object ctrl, object data, string phase)
        {
            try
            {
                if (_state == AstralState.Normal || _state == AstralState.ReturnFadeOut) return;
                if (Time.unscaledTime > _poseChangeReapplyUntil + 0.25f) return;

                MonoBehaviour behaviour = ctrl as MonoBehaviour;
                int key = behaviour != null ? behaviour.GetInstanceID() : 0;
                int count;
                _cameraControlSetDataLogCounts.TryGetValue(key, out count);
                if (count >= 2) return;
                _cameraControlSetDataLogCounts[key] = count + 1;

                string reason = _poseChangeReapplyReason ?? "pose-change";
                Vector3 dataPos = Vector3.zero;
                Vector3 dataDir = Vector3.zero;
                Vector3 dataRot = Vector3.zero;
                float dataFov = -1f;
                if (data != null)
                {
                    Type dataType = data.GetType();
                    dataPos = ReadVector3Member(data, dataType, "Pos");
                    dataDir = ReadVector3Member(data, dataType, "Dir");
                    dataRot = ReadVector3Member(data, dataType, "Rot");
                    dataFov = ReadFloatMember(data, dataType, "Fov", -1f);
                }

                SimpleFileLogger.Warn(
                    "CAMERA_SETDATA_DIAG"
                    + " reason=" + reason
                    + " phase=" + phase
                    + " count=" + count
                    + " dataType=" + (data != null ? data.GetType().FullName : "null")
                    + " dataPos=" + FormatDiagVector(dataPos)
                    + " dataDir=" + FormatDiagVector(dataDir)
                    + " dataRot=" + FormatDiagVector(dataRot)
                    + " dataFov=" + dataFov.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    + " stack=" + FormatStackTrace(4, 12));

                DumpPoseChangeCameraDiagnostics(reason, "setcameradata-" + phase + "-" + count);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn("CAMERA_SETDATA_DIAG log failed phase=" + phase + " error=" + ex.Message);
            }
        }

        private static void CameraControl_LateUpdate_Prefix(object __instance)
        {
            Plugin inst = Instance;
            if (inst == null) return;
            inst.LogCameraControlLateUpdate(__instance, "before");
        }

        private static void CameraControl_LateUpdate_Postfix(object __instance)
        {
            Plugin inst = Instance;
            if (inst == null) return;
            inst.LogCameraControlLateUpdate(__instance, "after");
        }

        private void LogCameraControlLateUpdate(object ctrl, string phase)
        {
            try
            {
                if (_state == AstralState.Normal || _state == AstralState.ReturnFadeOut) return;
                if (Time.unscaledTime > _poseChangeReapplyUntil + 0.25f) return;

                MonoBehaviour behaviour = ctrl as MonoBehaviour;
                int key = behaviour != null ? behaviour.GetInstanceID() : 0;
                int count;
                _cameraControlLateUpdateLogCounts.TryGetValue(key, out count);
                if (count >= 2) return;
                _cameraControlLateUpdateLogCounts[key] = count + 1;

                string reason = _poseChangeReapplyReason ?? "pose-change";
                DumpPoseChangeCharaDiagnostics(reason, "cameracontrol-lateupdate-" + phase + "-" + count);
                DumpPoseChangeCameraDiagnostics(reason, "cameracontrol-lateupdate-" + phase + "-" + count);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn("CAMERA_CONTROL_DIAG log failed phase=" + phase + " error=" + ex.Message);
            }
        }

        private static void DefCamCtrl_LateUpdate_Prefix(object __instance)
        {
            Plugin inst = Instance;
            if (inst == null) return;
            inst.LogDefCamCtrlLateUpdate(__instance, "before");
        }

        private static void DefCamCtrl_LateUpdate_Postfix(object __instance)
        {
            Plugin inst = Instance;
            if (inst == null) return;
            inst.LogDefCamCtrlLateUpdate(__instance, "after");
        }

        private void LogDefCamCtrlLateUpdate(object def, string phase)
        {
            try
            {
                if (_state == AstralState.Normal || _state == AstralState.ReturnFadeOut) return;
                if (Time.unscaledTime > _poseChangeReapplyUntil + 0.25f) return;

                MonoBehaviour behaviour = def as MonoBehaviour;
                int key = behaviour != null ? behaviour.GetInstanceID() : 0;
                int count;
                _defCamCtrlLateUpdateLogCounts.TryGetValue(key, out count);
                if (count >= 2) return;
                _defCamCtrlLateUpdateLogCounts[key] = count + 1;

                string reason = _poseChangeReapplyReason ?? "pose-change";
                DumpPoseChangeCharaDiagnostics(reason, "defcamctrl-lateupdate-" + phase + "-" + count);
                DumpPoseChangeCameraDiagnostics(reason, "defcamctrl-lateupdate-" + phase + "-" + count);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn("DEF_CAM_CTRL_DIAG log failed phase=" + phase + " error=" + ex.Message);
            }
        }

        private static bool TryReadCameraControlData(object ctrl, out Vector3 pos, out Vector3 dir, out Vector3 rot, out float fov)
        {
            pos = Vector3.zero;
            dir = Vector3.zero;
            rot = Vector3.zero;
            fov = -1f;
            if (ctrl == null) return false;

            MethodInfo getData = ctrl.GetType().GetMethod("GetCameraData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getData == null) return false;

            object data = getData.Invoke(ctrl, null);
            if (data == null) return false;

            Type type = data.GetType();
            pos = ReadVector3Member(data, type, "Pos");
            dir = ReadVector3Member(data, type, "Dir");
            rot = ReadVector3Member(data, type, "Rot");
            fov = ReadFloatMember(data, type, "Fov", -1f);
            return true;
        }

        private static Vector3 ReadVector3Member(object owner, Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.GetValue(owner) is Vector3 fv) return fv;
            PropertyInfo prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.GetValue(owner, null) is Vector3 pv) return pv;
            return Vector3.zero;
        }

        private static float ReadFloatMember(object owner, Type type, string name, float fallback)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                object value = field.GetValue(owner);
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
            }

            PropertyInfo prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                object value = prop.GetValue(owner, null);
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
            }

            return fallback;
        }

        private static Transform TryReadTransformMember(object owner, string name)
        {
            if (owner == null) return null;
            Type type = owner.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(owner) as Transform;
            PropertyInfo prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null) return prop.GetValue(owner, null) as Transform;
            return null;
        }

        private static bool IsBehaviourEnabled(object value)
        {
            Behaviour behaviour = value as Behaviour;
            return behaviour != null && behaviour.enabled;
        }

        private static string FormatCameraControlFields(object ctrl)
        {
            if (ctrl == null) return "none";
            try
            {
                Type type = ctrl.GetType();
                Transform transBase = TryReadTransformMember(ctrl, "transBase");
                return "type=" + type.FullName
                    + ",CameraFov=" + ReadFloatMember(ctrl, type, "CameraFov", -1f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    + ",CameraInitFov=" + ReadFloatMember(ctrl, type, "CameraInitFov", -1f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    + ",transBase=" + FormatDiagVector(transBase != null ? transBase.position : Vector3.zero);
            }
            catch (Exception ex)
            {
                return "error:" + ex.Message;
            }
        }

        private static string FormatDefCamCtrlFields(object def)
        {
            if (def == null) return "none";
            try
            {
                Type type = def.GetType();
                return "type=" + type.FullName
                    + ",enabled=" + IsBehaviourEnabled(def)
                    + ",methods=" + FormatDeclaredMethodNames(type);
            }
            catch (Exception ex)
            {
                return "error:" + ex.Message;
            }
        }

        private static string FormatDeclaredMethodNames(Type type)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || method.IsSpecialName) continue;
                names.Add(method.Name + "/" + method.GetParameters().Length);
            }
            names.Sort(StringComparer.Ordinal);
            return string.Join("|", names.ToArray());
        }

        private static string FormatCameraBehaviours(object[] behaviours)
        {
            if (behaviours == null || behaviours.Length == 0) return "none";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour b = behaviours[i] as MonoBehaviour;
                if (b == null) continue;
                if (sb.Length > 0) sb.Append("|");
                sb.Append(b.GetType().FullName).Append(":").Append(b.enabled);
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        private static string FormatStackTrace(int skipFrames, int maxFrames)
        {
            try
            {
                var trace = new System.Diagnostics.StackTrace(skipFrames, false);
                System.Text.StringBuilder sb = new System.Text.StringBuilder(512);
                int frames = Math.Min(maxFrames, trace.FrameCount);
                for (int i = 0; i < frames; i++)
                {
                    MethodBase method = trace.GetFrame(i).GetMethod();
                    if (method == null) continue;
                    if (sb.Length > 0) sb.Append(" <- ");
                    Type declaringType = method.DeclaringType;
                    sb.Append(declaringType != null ? declaringType.FullName : "unknown");
                    sb.Append(".").Append(method.Name);
                }
                return sb.Length > 0 ? sb.ToString() : "empty";
            }
            catch (Exception ex)
            {
                return "error:" + ex.Message;
            }
        }

        private static string FormatDiagVector(Vector3 value)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.###},{1:0.###},{2:0.###})", value.x, value.y, value.z);
        }

        private void MaintainPoseChangeAstralReapply()
        {
            if (_state == AstralState.Normal || _state == AstralState.ReturnFadeOut) return;
            if (Time.unscaledTime > _poseChangeReapplyUntil) return;

            bool detail = _poseChangeHoldLogCount == 0;
            _poseChangeHoldLogCount++;
            ReapplyAstralCharaOffsetFromCurrentPose((_poseChangeReapplyReason ?? "pose-change") + ":hold", detail);
        }
    }
}
