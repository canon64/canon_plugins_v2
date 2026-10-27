using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using CanonPlugins.Shared;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private MotionTraceLog _motionTrace;
        private float _nextComposerTrace;
        private readonly Dictionary<int, float> _cameraTraceAt = new Dictionary<int, float>();
        private readonly Dictionary<int, int> _cameraTraceFrames = new Dictionary<int, int>();
        private readonly Dictionary<string, float> _traceParentTargets = new Dictionary<string, float>();
        private string _traceUpdateStage = "not_started";
        private int _traceUpdateFrame = -1;
        private static readonly PropertyInfo TraceForceRenderingOff = typeof(Renderer).GetProperty("forceRenderingOff");
        private bool MotionTraceEnabled => _motionTrace != null && _motionTrace.Enabled;
        private static string TraceNumber(float value) => value.ToString("F4", CultureInfo.InvariantCulture);
        private static string TraceVector(Vector3 value) => "(" + TraceNumber(value.x) + ","
            + TraceNumber(value.y) + "," + TraceNumber(value.z) + ")";
        private void TraceComposerEvent(string stage, string detail)
        {
            if (MotionTraceEnabled)
                _motionTrace.Write(stage, Time.frameCount, Time.realtimeSinceStartup, detail);
        }

        private void InitMotionDiagnostics()
        {
            _motionTrace = new MotionTraceLog(_pluginDir, GetType().Assembly,
                message => Logger.LogWarning(message));
            if (MotionTraceEnabled) Camera.onPreCull += TraceCameraPreCull;
            if (MotionTraceEnabled) Camera.onPostRender += TraceCameraPostRender;
        }

        private void DisposeMotionDiagnostics()
        {
            Camera.onPreCull -= TraceCameraPreCull;
            Camera.onPostRender -= TraceCameraPostRender;
            TraceComposerEvent("STOP", "plugin_destroyed");
            _motionTrace?.Dispose();
        }

        private void TickComposerDiagnostics()
        {
            if (!MotionTraceEnabled || Time.unscaledTime < _nextComposerTrace) return;
            _nextComposerTrace = Time.unscaledTime + 0.5f;
            try
            {
                bool bodyLinked = HipHijackBridge.TryGet("body", out string bodyId);
                Vector3 axis = Vector3.zero;
                bool axisAvailable = _vibeInsertionAxisReader != null && _vibeInsertionAxisReader(out axis);
                Vector3 direction = Vector3.zero;
                float body = 0f, upper = 0f, slow = 0f, gauge = 0f;
                bool vibeAvailable = _vibeMotionReader != null
                    && _vibeMotionReader(out direction, out body, out upper, out slow, out gauge);
                foreach (var entry in _vibeParentWeights)
                {
                    ManagedObjectData child = FindDataById(entry.Key);
                    if (child != null && Mathf.Abs(entry.Value - slow * child.vibeSlowParentWeight) > 0.001f)
                        _nextComposerTrace = Time.unscaledTime + 0.1f;
                }
                TraceComposerEvent("STATE", "stage=LateUpdate inRoom=" + BlankRoomGate.IsInBlankRoom
                    + " updateFrame=" + _traceUpdateFrame + " lastUpdateStage=" + _traceUpdateStage
                    + " currentKey=" + BuildCurrentPositionKey() + " preset=" + _activePreset?.displayName
                    + " presetKey=" + _activePreset?.key + " loopSplit=" + (_activePreset?.useLoopSplit ?? false)
                    + " authoredObjects=" + _objects.Count + " runtimeObjects=" + _runtimeObjects.Count
                    + " rootExists=" + (_runtime.Root != null) + " femaleExists=" + (_runtime.MainFemale != null)
                    + " flagsMotion=" + TraceNumber(_runtime.Flags != null ? _runtime.Flags.motion : -1f)
                    + " flagsSpeed=" + TraceNumber(_runtime.Flags != null ? _runtime.Flags.speed : -1f)
                    + " voiceIntensityLevel=" + _motionIntensityLevel + " movementWeight=" + TraceNumber(_movementWeight)
                    + " ghostEnabled=" + _femaleGhostEnabled + " ghostTarget=" + (_femaleGhostTarget != null)
                    + " slowReaderBound=" + (_vibeMotionReader != null) + " vibeAvailable=" + vibeAvailable
                    + " slow=" + TraceNumber(slow) + " gauge01=" + TraceNumber(gauge)
                    + " bodyOffset=" + TraceNumber(body) + " upperOffset=" + TraceNumber(upper)
                    + " axisReaderBound=" + (_vibeInsertionAxisReader != null) + " axisAvailable=" + axisAvailable
                    + " insertionAxis=" + TraceVector(axis)
                    + " hipBridge=" + HipHijackBridge.IsAvailable + " bodyLinked=" + bodyLinked + " bodyObject=" + bodyId
                    + " bodyIkRunning=" + HipHijackBridge.IsBodyIkRunning()
                    + " speedHijack=" + HipHijackBridge.IsSpeedHijackEnabled()
                    + " cutAnimSpeed=" + HipHijackBridge.IsCutFemaleAnimSpeedEnabled()
                    + " bodyCtrlLink=" + HipHijackBridge.IsBodyCtrlLinkEnabled());
                foreach (ManagedObjectData data in _objects)
                {
                    if (data == null || !data.isRotationObject) continue;
                    RuntimeObjectRef rr = FindRuntimeById(data.id);
                    if (rr == null) TraceComposerEvent("RING_MISSING", "id=" + data.id + " name=" + data.name
                        + " desiredVisible=" + data.visible);
                    else TraceRing(rr, "RING", null, null);
                }
                RuntimeObjectRef bodyRef = bodyLinked ? FindRuntimeById(bodyId) : null;
                if (bodyRef?.GameObject != null)
                    TraceComposerEvent("IK_TARGET", "id=" + bodyId + " world="
                        + TraceVector(bodyRef.GameObject.transform.position)
                        + " local=" + TraceVector(bodyRef.GameObject.transform.localPosition)
                        + " active=" + bodyRef.GameObject.activeInHierarchy);
                TraceFinalAnimatorAndIk();
            }
            catch (Exception ex) { TraceComposerEvent("DIAGNOSTIC_ERROR", ex.ToString()); }
        }

        private void TraceParentTarget(ManagedObjectData child, float target, float current, float duration)
        {
            if (!MotionTraceEnabled) return;
            bool known = _traceParentTargets.TryGetValue(child.id, out float previous);
            if (known && Mathf.Abs(previous - target) < 0.0001f) return;
            _traceParentTargets[child.id] = target;
            _nextComposerTrace = 0f;
            TraceComposerEvent("SLOW_PARENT_TARGET", "id=" + child.id + " from=" + TraceNumber(current)
                + " target=" + TraceNumber(target) + " seconds=" + TraceNumber(duration));
        }

        private void TraceCameraPreCull(Camera camera)
        {
            if (!MotionTraceEnabled || camera == null) return;
            int id = camera.GetInstanceID();
            if (_cameraTraceAt.TryGetValue(id, out float next) && Time.unscaledTime < next) return;
            if (!_cameraTraceAt.ContainsKey(id) && _cameraTraceAt.Count >= 32)
            {
                _cameraTraceAt.Clear();
                _cameraTraceFrames.Clear();
            }
            _cameraTraceAt[id] = Time.unscaledTime + 1f;
            _cameraTraceFrames[id] = Time.frameCount;
            try
            {
                TraceComposerEvent("CAMERA_PRE_CULL", "id=" + id + " name=" + camera.name
                    + " type=" + camera.cameraType + " enabled=" + camera.enabled
                    + " mask=" + camera.cullingMask + " depth=" + TraceNumber(camera.depth)
                    + " near=" + TraceNumber(camera.nearClipPlane) + " far=" + TraceNumber(camera.farClipPlane)
                    + " occlusion=" + camera.useOcclusionCulling + " targetTexture=" + (camera.targetTexture != null)
                    + " stereo=" + camera.stereoEnabled + " world=" + TraceVector(camera.transform.position));
                TraceFinalAnimatorAndIk();
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                foreach (RuntimeObjectRef rr in _runtimeObjects.Values)
                    if (rr?.Data != null && rr.Data.isRotationObject) TraceRing(rr, "RING_PRE_CULL", camera, planes);
            }
            catch (Exception ex) { TraceComposerEvent("DIAGNOSTIC_ERROR", ex.ToString()); }
        }

        private void TraceCameraPostRender(Camera camera)
        {
            if (!MotionTraceEnabled || camera == null || !_cameraTraceFrames.TryGetValue(camera.GetInstanceID(), out int frame)
                || frame != Time.frameCount) return;
            TraceComposerEvent("CAMERA_POST_RENDER", "id=" + camera.GetInstanceID() + " name=" + camera.name);
        }

        internal void RecordRingRender(string objectId, Camera camera)
        {
            if (!MotionTraceEnabled || camera == null || !_cameraTraceFrames.TryGetValue(camera.GetInstanceID(), out int frame)
                || frame != Time.frameCount) return;
            TraceComposerEvent("RING_RENDER_CALLBACK", "id=" + objectId + " camera=" + camera.GetInstanceID()
                + " name=" + camera.name);
        }

        private void TraceUpdateStage(string stage)
        {
            if (!MotionTraceEnabled) return;
            _traceUpdateStage = stage;
            _traceUpdateFrame = Time.frameCount;
        }

        private void TraceRing(RuntimeObjectRef rr, string stage, Camera camera, Plane[] planes)
        {
            ManagedObjectData data = rr.Data;
            Transform wrapper = rr.GameObject != null ? rr.GameObject.transform : null;
            Transform visual = rr.Visual != null ? rr.Visual.transform : null;
            MeshRenderer renderer = rr.Visual != null ? rr.Visual.GetComponent<MeshRenderer>() : null;
            MeshFilter filter = rr.Visual != null ? rr.Visual.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Material material = renderer != null ? renderer.sharedMaterial : null;
            _vibeParentWeights.TryGetValue(data.id, out float weight);
            GetEffectiveOrbitRadii(data, out float rx, out float rz);
            ManagedObjectData l1 = null, l2 = null;
            _activeL1ById?.TryGetValue(data.id, out l1);
            _activeL2ById?.TryGetValue(data.id, out l2);
            GetMotionMultipliers(_activePreset, data.id, 1, out float m1x, out float m1z);
            GetMotionMultipliers(_activePreset, data.id, 2, out float m2x, out float m2z);
            string cameraState = "";
            if (camera != null && renderer != null)
                cameraState = " camera=" + camera.GetInstanceID()
                    + " layerAllowed=" + ((camera.cullingMask & (1 << renderer.gameObject.layer)) != 0)
                    + " boundsInFrustum=" + GeometryUtility.TestPlanesAABB(planes, renderer.bounds)
                    + " viewport=" + TraceVector(camera.WorldToViewportPoint(renderer.bounds.center));
            TraceComposerEvent(stage, "id=" + data.id + " name=" + data.name + " visibleSetting=" + data.visible
                + " wrapper=" + (wrapper != null) + " visual=" + (visual != null)
                + " active=" + (visual != null && visual.gameObject.activeInHierarchy)
                + " activeSelf=" + (visual != null && visual.gameObject.activeSelf)
                + " wrapperActive=" + (wrapper != null && wrapper.gameObject.activeInHierarchy)
                + " renderer=" + (renderer != null) + " rendererEnabled=" + (renderer != null && renderer.enabled)
                + " rendererVisiblePreviousRender=" + (renderer != null && renderer.isVisible)
                + " forceRenderingOff=" + (renderer != null && TraceForceRenderingOff != null
                    ? TraceForceRenderingOff.GetValue(renderer, null)?.ToString() : "unavailable")
                + " layer=" + (visual != null ? visual.gameObject.layer : -1)
                + " mesh=" + (mesh != null ? mesh.GetInstanceID() : 0) + " vertices=" + (mesh != null ? mesh.vertexCount : 0)
                + " submeshes=" + (mesh != null ? mesh.subMeshCount : 0)
                + " indices0=" + (mesh != null && mesh.subMeshCount > 0 ? mesh.GetIndexCount(0).ToString() : "0")
                + " meshBounds=" + (mesh != null ? TraceVector(mesh.bounds.size) : "missing")
                + " meshMatchesGenerated=" + (mesh != null && mesh == rr.GeneratedMesh)
                + " material=" + (material != null ? material.name : "missing")
                + " materialCount=" + (renderer != null ? renderer.sharedMaterials.Length : 0)
                + " materialMatchesGenerated=" + (material != null && material == rr.GeneratedMaterial)
                + " shader=" + (material != null && material.shader != null ? material.shader.name : "missing")
                + " shaderSupported=" + (material != null && material.shader != null && material.shader.isSupported)
                + " passes=" + (material != null ? material.passCount : 0)
                + " renderQueue=" + (material != null ? material.renderQueue : -999)
                + TraceMaterialProperty(material, "_ZWrite") + TraceMaterialProperty(material, "_ZTest")
                + TraceMaterialProperty(material, "_SrcBlend") + TraceMaterialProperty(material, "_DstBlend")
                + TraceMaterialProperty(material, "_Cull")
                + " alpha=" + (material != null && material.HasProperty("_Color") ? TraceNumber(material.color.a) : "absent")
                + " world=" + (wrapper != null ? TraceVector(wrapper.position) : "missing")
                + " worldEuler=" + (wrapper != null ? TraceVector(wrapper.eulerAngles) : "missing")
                + " local=" + (wrapper != null ? TraceVector(wrapper.localPosition) : "missing")
                + " scale=" + (visual != null ? TraceVector(visual.lossyScale) : "missing")
                + " boundsCenter=" + (renderer != null ? TraceVector(renderer.bounds.center) : "missing")
                + " boundsSize=" + (renderer != null ? TraceVector(renderer.bounds.size) : "missing")
                + " parent=" + (wrapper != null && wrapper.parent != null ? wrapper.parent.name : "none")
                + " parentId=" + data.parentRefId + " rx=" + TraceNumber(rx) + " rz=" + TraceNumber(rz)
                + " l1Rx=" + TraceNumber(l1 != null ? l1.orbitRadiusX : -1f)
                + " l1Rz=" + TraceNumber(l1 != null ? l1.orbitRadiusZ : -1f)
                + " l2Rx=" + TraceNumber(l2 != null ? l2.orbitRadiusX : -1f)
                + " l2Rz=" + TraceNumber(l2 != null ? l2.orbitRadiusZ : -1f)
                + " strengthTarget=" + IsMotionTarget(_activePreset, data.id)
                + " l1Multipliers=" + TraceNumber(m1x) + "," + TraceNumber(m1z)
                + " l2Multipliers=" + TraceNumber(m2x) + "," + TraceNumber(m2z)
                + " tube=" + TraceNumber(data.tubeRadius) + " animSync=" + data.animSync
                + " phase=" + TraceNumber(GetAnimNormalizedPhase())
                + " slowEnabled=" + data.vibeSlowParentEnabled + " slowWeightActual=" + TraceNumber(weight)
                + " slowWeightMax=" + TraceNumber(data.vibeSlowParentWeight)
                + " rise=" + TraceNumber(data.vibeSlowRiseSeconds) + " fall=" + TraceNumber(data.vibeSlowFallSeconds)
                + " insertionAlign=" + data.vibeInsertionAlignEnabled + cameraState);
        }

        private static string TraceMaterialProperty(Material material, string name) => " " + name + "="
            + (material != null && material.HasProperty(name) ? TraceNumber(material.GetFloat(name)) : "absent");

        private void TraceFinalAnimatorAndIk()
        {
            Animator animator = _runtime.MainFemaleAnimBody;
            string motion = "unavailable", speedBody = "unavailable";
            if (animator != null && animator.runtimeAnimatorController != null)
                foreach (var p in animator.parameters)
                {
                    if (p.type != AnimatorControllerParameterType.Float) continue;
                    if (p.name == "motion") motion = TraceNumber(animator.GetFloat(p.nameHash));
                    if (p.name == "speedBody") speedBody = TraceNumber(animator.GetFloat(p.nameHash));
                }
            var solver = _runtime.MainFemaleFbbik != null ? _runtime.MainFemaleFbbik.solver : null;
            var effector = solver != null ? solver.bodyEffector : null;
            TraceComposerEvent("FINAL_ANIM_IK", "animMotion=" + motion + " animSpeedBody=" + speedBody
                + " flagsMotion=" + TraceNumber(_runtime.Flags != null ? _runtime.Flags.motion : -1f)
                + " effector=" + (effector != null)
                + " effectorPosition=" + (effector != null ? TraceVector(effector.position) : "missing")
                + " positionWeight=" + (effector != null ? TraceNumber(effector.positionWeight) : "missing")
                + " actualBone=" + (effector != null && effector.bone != null
                    ? TraceVector(effector.bone.position) : "missing"));
        }
    }

    internal sealed class MotionRingRenderProbe : MonoBehaviour
    {
        internal Plugin Owner;
        internal string ObjectId;
        private void OnWillRenderObject()
        {
            if (Owner != null) Owner.RecordRingRender(ObjectId, Camera.current);
        }
    }
}
