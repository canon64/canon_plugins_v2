using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public enum VmdEndReason { UserStop, PoseChange, LoadFailed, SceneExit }
    public static class VmdCharacterControlApi
    {
        public static void EndForPoseChange() => Plugin.Instance?.EndForPoseChange();
    }

    public sealed partial class Plugin
    {
        private Harmony _poseHarmony;
        private ControlTransition _controlTransition => _target.Transition;
        private int _poseEpoch => _controlTransition.Generation;
        private bool _endingPose => _controlTransition.Releasing;
        private bool _fullBodyOwned;
        private object _ownedSolver;
        private PoseSnapshot _poseSnapshot { get => _target.Snapshot; set => _target.Snapshot = value; }
        private Transform _preparedBoneRoot { get => _target.BoneRoot; set => _target.BoneRoot = value; }
        private int _preparedShape;
        private readonly HashSet<UnityEngine.Object> _ownedVmdObjects = new HashSet<UnityEngine.Object>();
        private readonly HashSet<object> _retiredControllers = new HashSet<object>();
        private readonly List<Component> _retiredComponents = new List<Component>();
        private Behaviour _abmx;
        private int _abmxApplied;
        private float _nextPoseIntegrityCheck;
        private void TickPoseIntegrity()
        {
            if (!_fullBodyOwned || _loadRoutine != null || Time.unscaledTime < _nextPoseIntegrityCheck) return;
            _nextPoseIntegrityCheck = Time.unscaledTime + .25f;
            if (_trialChara == null || _trialChara.objBodyBone == null || !_trialChara.loadEnd) {
                ReleaseCharacter(VmdEndReason.LoadFailed);
                _presetMessage = "モデルが未準備になったためVMDを終了しました";
                return;
            }
            if (_preparedBoneRoot == _trialChara.objBodyBone.transform && _preparedShape == ShapeSignature()) return;
            string path = _settings.LastVmdAbsPath;
            float time = _currentAnimTime;
            bool playing = _state == PluginState.Playing;
            bool switching = _myListSwitching;
            _myListSwitching = true;
            try {
                ReleaseCharacter();
                _modelReloadStarting = true;
                _modelResumeTime = time;
                PlaybackLoadAndPlay(path, playing);
            } finally { _modelReloadStarting = false; _myListSwitching = switching; }
        }

        private sealed class LocalPose
        {
            internal Transform Transform;
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;
            internal LocalPose(Transform t) { Transform = t; Position = t.localPosition; Rotation = t.localRotation; Scale = t.localScale; }
            internal void Restore() { if (Transform != null) { Transform.localPosition = Position; Transform.localRotation = Rotation; Transform.localScale = Scale; } }
        }
        private sealed class PoseSnapshot
        {
            internal ChaControl Chara;
            internal HSceneProc Scene;
            internal GameObject Body;
            internal Animator Animator;
            internal RuntimeAnimatorController Controller;
            internal float Speed;
            internal AnimatorUpdateMode UpdateMode;
            internal int[] States;
            internal float[] Times, Weights;
            internal LocalPose[] Bones;
        }

        private void InitPoseTransitions()
        {
            _poseHarmony = new Harmony(Guid + ".pose-transition");
            InitMainHPause();
            _poseHarmony.Patch(PartnerUpdateVisible, prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforePartnerVisibility)));
            foreach (string name in new[] { "ChangeAnimator", "MovePointByChangeAnim" })
                _poseHarmony.Patch(AccessTools.Method(typeof(HSceneProc), name), prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeHPosture)));
            foreach (var method in typeof(ChaControl).GetMethods().Where(m => m.Name == "SetPosition" || m.Name == "SetRotation"))
                _poseHarmony.Patch(method, prefix: new HarmonyMethod(typeof(Plugin), nameof(AllowCharaPlacement)));
            PatchOptional("RootMotion.FinalIK.IKSolver", "Update", nameof(AllowSolver));
            PatchOptional("RootMotion.FinalIK.IKSolverFullBody", "FixTransforms", nameof(AllowSolver));
            PatchOptional("AdvIKPlugin.Algos.BreathingBoneEffect", "GetEffect", nameof(AllowBreathing));
            var abmx = AccessTools.TypeByName("KKABMX.Core.BoneController");
            if (abmx != null) _poseHarmony.Patch(AccessTools.Method(abmx, "ApplyEffects"), postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterAbmxApply)));
            PatchOptional("MainGameHScenePoseInjector.Plugin", "ApplyPose", nameof(BeforeInjectedPosture));
            PatchOptional("MainGameHScenePoseInjector.Plugin", "RestoreAllSnapshots", nameof(BeforeInjectedPosture));
            PatchOptional("KKVMDPlayPlugin.DestroyListener", "OnDestroy", nameof(AllowControllerDestroyListener));
            for (Type type = _tCtrl; type != null && type != typeof(MonoBehaviour); type = type.BaseType) {
                var destroy = type.GetMethod("OnDestroy", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (destroy != null) _poseHarmony.Patch(destroy, prefix: new HarmonyMethod(typeof(Plugin), nameof(AllowControllerDestroy)));
            }
        }
        private void PatchOptional(string type, string method, string prefix)
        {
            var t = AccessTools.TypeByName(type);
            if (t == null) return;
            var m = AccessTools.Method(t, method);
            if (m == null) throw new MissingMethodException(type, method);
            _poseHarmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), prefix));
        }
        private static void BeforeHPosture(HSceneProc __instance)
        {
            if (Instance != null && Instance._hSceneProc == __instance) Instance.EndForPoseChange();
        }
        private static void BeforeInjectedPosture() => Instance?.EndForPoseChange();
        internal void EndForPoseChange()
        {
            if (_endingPose || IsIndependentTarget) return;
            // Also invalidate speech callbacks while idle: a delayed dance must not undo a new posture.
            _controlTransition.Invalidate();
            _modelReloadAt = -1; _modelResumeTime = -1;
            if (_vmdOwnsCharacter || _loadRoutine != null) ReleaseCharacter(VmdEndReason.PoseChange);
        }
        private static bool AllowCharaPlacement(ChaControl __instance) => Instance == null || (!Instance._fullBodyOwned && !Instance._actorPreparing) || Instance._trialChara != __instance;
        private static bool AllowSolver(object __instance) => Instance == null || ((!Instance._fullBodyOwned || !ReferenceEquals(Instance._ownedSolver, __instance)) && (!Instance._actorPreparing || !ReferenceEquals(Instance._actorPreparationSolver, __instance)));
        private static bool AllowBreathing(object origin)
        {
            var p = Instance;
            if (p == null || origin == null) return true;
            return (!p._fullBodyOwned || !ReferenceEquals(origin, p._abmx)) && (!p._actorPreparing || !ReferenceEquals(origin, p._actorPreparationAbmx));
        }
        private static void AfterAbmxApply(object __instance)
        {
            var p = Instance;
            if (p != null && (ReferenceEquals(p._abmx, __instance) || ReferenceEquals(p._actorPreparationAbmx, __instance))) p._abmxApplied++;
        }
        private static bool AllowControllerDestroy(object __instance) => Instance == null || !Instance._retiredControllers.Contains(__instance);
        private static bool AllowControllerDestroyListener(object __instance)
        {
            var p = Instance;
            return p == null || !p._retiredControllers.Contains(AccessTools.Field(__instance.GetType(), "controller")?.GetValue(__instance));
        }
        private static object ReadProperty(object value, string name) => value == null ? null : AccessTools.Property(value.GetType(), name)?.GetValue(value, null);
        private bool FaceOnlyRequested => _settings.Adjustments.TryGetValue("char.FaceOnly", out float value) && value != 0;
        private bool HasPendingPosture()
        {
            if (IsIndependentTarget) return false;
            if (_hSceneProc == null) return true;
            var flags = AccessTools.Field(typeof(HSceneProc), "flags")?.GetValue(_hSceneProc);
            return flags != null && AccessTools.Field(flags.GetType(), "selectAnimationListInfo")?.GetValue(flags) != null;
        }

        private void CapturePoseSnapshot()
        {
            var c = _trialChara;
            var a = c.animBody;
            var transforms = new HashSet<Transform>(c.objBodyBone.GetComponentsInChildren<Transform>(true));
            transforms.Add(c.transform); transforms.Add(c.objTop.transform); transforms.Add(c.objAnim.transform);
            var s = new PoseSnapshot { Chara = c, Scene = _hSceneProc, Body = c.objBodyBone, Animator = a,
                Controller = a.runtimeAnimatorController, Speed = a.speed, UpdateMode = a.updateMode,
                States = new int[a.layerCount], Times = new float[a.layerCount], Weights = new float[a.layerCount],
                Bones = transforms.Select(t => new LocalPose(t)).ToArray() };
            for (int i = 0; i < s.States.Length; i++) { var state = a.GetCurrentAnimatorStateInfo(i); s.States[i] = state.fullPathHash; s.Times[i] = state.normalizedTime; s.Weights[i] = a.GetLayerWeight(i); }
            _poseSnapshot = s;
        }
        private void RestorePoseSnapshot(VmdEndReason reason)
        {
            var s = _poseSnapshot; _poseSnapshot = null;
            if (s == null || s.Chara == null || s.Chara != _trialChara || s.Body != s.Chara.objBodyBone) return;
            if (reason == VmdEndReason.SceneExit || s.Scene == null || s.Scene != _hSceneProc) return;
            foreach (var pose in s.Bones) pose.Restore();
            if (s.Animator != null && s.Animator == s.Chara.animBody) {
                s.Animator.runtimeAnimatorController = s.Controller;
                s.Animator.speed = s.Speed; s.Animator.updateMode = s.UpdateMode;
                for (int i = 0; i < s.States.Length && i < s.Animator.layerCount; i++) {
                    s.Animator.SetLayerWeight(i, s.Weights[i]);
                    if (s.States[i] != 0 && s.Animator.HasState(i, s.States[i])) s.Animator.Play(s.States[i], i, s.Times[i]);
                }
            }
        }
        private int ShapeSignature()
        {
            unchecked {
                int hash = _trialChara.objBodyBone.GetInstanceID();
                if (IsIndependentTarget) hash = hash * 31 + _actorBodyRevision;
                foreach (float v in _trialChara.fileBody.shapeValueBody) hash = hash * 31 + v.GetHashCode();
                return hash;
            }
        }
        private static string BonePath(Transform t, Transform root)
        {
            string path = t.name;
            while (t.parent != null && t != root) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
        private void SetNativeRestPose()
        {
            var c = _trialChara;
            // Same skeleton asset used by ChaControl.LoadAsync, including male characters.
            var prefab = CommonLib.LoadAsset<GameObject>("chara/oo_base.unity3d", c.hiPoly ? "p_cf_body_bone" : "p_cf_body_bone_low", false, Manager.Character.mainManifestName);
            if (prefab == null) throw new InvalidOperationException("本体の基準骨プレハブが読み込めません");
            var originals = prefab.GetComponentsInChildren<Transform>(true).ToDictionary(t => BonePath(t, prefab.transform));
            int matched = 0;
            var required = new HashSet<string>(new[] { "cf_j_hips", "cf_j_spine01", "cf_j_head", "cf_j_arm00_L", "cf_j_arm00_R", "cf_j_forearm01_L", "cf_j_forearm01_R", "cf_j_thigh00_L", "cf_j_thigh00_R", "cf_j_leg01_L", "cf_j_leg01_R", "cf_j_foot_L", "cf_j_foot_R" });
            foreach (var t in c.objAnim.GetComponentsInChildren<Transform>(true)) {
                if (!t.name.StartsWith("cf_j_", StringComparison.Ordinal)) continue;
                string key = BonePath(t, c.objAnim.transform);
                // Unity's cloned root name can have a suffix; compare the path below it.
                int slash = key.IndexOf('/');
                key = prefab.name + (slash >= 0 ? key.Substring(slash) : "");
                if (!originals.TryGetValue(key, out var rest)) continue; // Accessory/cloth bones are not the native skeleton.
                t.localPosition = rest.localPosition; t.localRotation = rest.localRotation;
                required.Remove(t.name);
                matched++;
            }
            if (matched < 20 || required.Count != 0) throw new InvalidOperationException("基準骨の対応が不足しています: " + string.Join(",", required));
            var top = c.objTop.transform;
            Vector3 forward = Vector3.ProjectOnPlane(top.forward, Vector3.up);
            if (forward.sqrMagnitude < .0001f) forward = Vector3.ProjectOnPlane(c.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
            Quaternion upright = Quaternion.LookRotation(forward.normalized, Vector3.up);
            top.rotation = upright;
            c.objAnim.transform.localPosition = prefab.transform.localPosition;
            c.objAnim.transform.localRotation = prefab.transform.localRotation;
            c.updateShapeBody = true; c.updateShapeFace = true;
            c.LateUpdateForce();
        }
        private void RetireController()
        {
            if (!(_controller is Component ctrl) || ctrl == null) { _controller = null; return; }
            VmdBridgeDeleteAnim(ctrl);
            _retiredControllers.Add(ctrl);
            var listenerType = _tCtrl.Assembly.GetType("KKVMDPlayPlugin.DestroyListener");
            if (listenerType != null && _trialChara != null)
                foreach (var listener in _trialChara.GetComponentsInChildren(listenerType, true))
                    if (ReferenceEquals(AccessTools.Field(listenerType, "controller").GetValue(listener), ctrl)) RetireObject(listener);
            var manager = _piMgrInstance.GetValue(null, null);
            AccessTools.Method(manager.GetType(), "RemoveController")?.Invoke(manager, new object[] { ctrl });
            foreach (string field in new[] { "animeOverride", "faceController", "vmdAnimationGo", "armCorrectors" }) {
                object value = AccessTools.Field(ctrl.GetType(), field)?.GetValue(ctrl);
                if (value is Array array) { foreach (var entry in array) RetireObject(entry); }
                else RetireObject(value);
            }
            foreach (var owned in _ownedVmdObjects.ToArray()) RetireObject(owned);
            _ownedVmdObjects.Clear();
            _controller = null;
        }
        private void RetireObject(object value)
        {
            if (value is Transform) return;
            if (value is UnityEngine.Object obj && !_ownedVmdObjects.Remove(obj)) return;
            if (value is Behaviour b && b != null) b.enabled = false;
            if (value is Component c && c != null) { _retiredComponents.Add(c); Destroy(c); }
            else if (value is GameObject go && go != null) { go.SetActive(false); Destroy(go); }
        }
        private IEnumerator PrepareUprightPose(int epoch)
        {
            if (FaceOnlyRequested) {
                if ((_controller as UnityEngine.Object) == null) {
                    _controller = VmdBridgeInstallController(_trialChara);
                    foreach (var pose in _poseSnapshot.Bones) pose.Restore();
                }
                yield break;
            }
            _fullBodyOwned = true;
            _ownedSolver = ReadProperty(_originalIk, "solver") ?? (_originalIk == null ? null : AccessTools.Field(_originalIk.GetType(), "solver")?.GetValue(_originalIk));
            if (_originalAnimator != null) _originalAnimator.enabled = false;
            if (_originalIk != null) _originalIk.enabled = false;
            var abmxType = AccessTools.TypeByName("KKABMX.Core.BoneController");
            _abmx = abmxType == null ? null : _trialChara.GetComponent(abmxType) as Behaviour;
            if (_abmx != null && !_abmx.enabled) throw new InvalidOperationException("ABMXが無効のため基準姿勢を準備できません");
            int shape = ShapeSignature();
            bool rebuild = _preparedBoneRoot != _trialChara.objBodyBone.transform || _preparedShape != shape || (_controller as UnityEngine.Object) == null;
            if (rebuild) { RetireController(); yield return null; _retiredComponents.Clear(); _retiredControllers.Clear(); }
            if (epoch != _poseEpoch || !_vmdOwnsCharacter) yield break;
            SetNativeRestPose();
            if (_abmx != null) {
                float deadline = Time.realtimeSinceStartup + 15f;
                int applied = _abmxApplied;
                while (_abmxApplied == applied || ReadProperty(_abmx, "NeedsFullRefresh") is bool refresh && refresh ||
                       ReadProperty(_abmx, "NeedsBaselineUpdate") is bool baseline && baseline ||
                       AccessTools.Field(abmxType, "_previousAnimSpeed").GetValue(_abmx) != null) {
                    if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("ABMXの補正完了待ちが時間切れです");
                    yield return null;
                    if (epoch != _poseEpoch || !_vmdOwnsCharacter) yield break;
                }
                // Keep the existing card baseline; do not collect a second baseline from an H/VMD pose.
            }
            if (rebuild) {
                _controller = VmdBridgeInstallController(_trialChara);
                if ((_controller as UnityEngine.Object) == null) throw new InvalidOperationException("VMDコントローラを作成できません");
                foreach (string field in new[] { "sequenceSet", "vmdAnimationGo", "t_dummy_root", "faceController", "armCorrectors" })
                    if (AccessTools.Field(_controller.GetType(), field)?.GetValue(_controller) == null)
                        throw new InvalidOperationException("VMD基準構築が未完了です: " + field);
                _preparedBoneRoot = _trialChara.objBodyBone.transform; _preparedShape = shape;
            }
            ApplySavedAdjustments(true);
            AccessTools.Method(_controller.GetType(), "InitializeBasePositions").Invoke(_controller, null);
            _trialChara.resetDynamicBoneAll = true;
        }
    }
}
