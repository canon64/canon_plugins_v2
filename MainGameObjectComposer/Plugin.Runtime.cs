using System;
using System.Collections;
using System.Collections.Generic;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private Animator _resolvedAnimBody;
        private float _nextFbbikResolveTime;
        private float _nextHSceneProcResolveTime;
        private readonly Dictionary<string, List<ManagedObjectData>> _managedChildrenByParent =
            new Dictionary<string, List<ManagedObjectData>>(StringComparer.Ordinal);
        private readonly List<List<ManagedObjectData>> _managedChildListPool = new List<List<ManagedObjectData>>();
        private int _managedChildIndexFrame = -1;

        private bool TryGetManagedChildren(string parentId, out List<ManagedObjectData> children)
        {
            if (_managedChildIndexFrame != Time.frameCount)
            {
                _managedChildIndexFrame = Time.frameCount;
                _managedChildrenByParent.Clear();
                for (int i = 0; i < _managedChildListPool.Count; i++)
                    _managedChildListPool[i].Clear();

                int usedLists = 0;
                for (int i = 0; i < _objects.Count; i++)
                {
                    ManagedObjectData child = _objects[i];
                    if (child == null || !string.Equals(child.parentKind, ParentKindManaged, StringComparison.Ordinal)
                        || string.IsNullOrEmpty(child.parentRefId)) continue;
                    if (!_managedChildrenByParent.TryGetValue(child.parentRefId, out List<ManagedObjectData> list))
                    {
                        if (usedLists == _managedChildListPool.Count)
                            _managedChildListPool.Add(new List<ManagedObjectData>());
                        list = _managedChildListPool[usedLists++];
                        _managedChildrenByParent.Add(child.parentRefId, list);
                    }
                    list.Add(child);
                }
            }

            if (string.IsNullOrEmpty(parentId))
            {
                children = null;
                return false;
            }
            return _managedChildrenByParent.TryGetValue(parentId, out children);
        }

        private bool TryResolveRuntimeRefs()
        {
            if (_runtime.HSceneProc != null && !_runtime.HSceneProc)
            {
                OnHSceneEnded();
            }

            if (_runtime.HSceneProc == null)
            {
                if (Time.unscaledTime < _nextHSceneProcResolveTime)
                    return false;
                _nextHSceneProcResolveTime = Time.unscaledTime + 0.5f;
                _runtime.HSceneProc = FindObjectOfType<HSceneProc>();
                if (_runtime.HSceneProc == null)
                {
                    LogResolveMissing("HSceneProc");
                    return false;
                }

                _runtime.Flags = null;
                _runtime.MainFemale = null;
                _runtime.MainFemaleAnimBody = null;
                _runtime.MainFemaleFbbik = null;
                _runtime.ReadyLogged = false;
            }

            if (_runtime.Flags == null)
            {
                _runtime.Flags = _runtime.HSceneProc.flags;
            }
            if (_runtime.Flags == null)
            {
                LogResolveMissing("HFlag");
                return false;
            }

            if (_runtime.MainFemale != null && !_runtime.MainFemale)
            {
                _runtime.MainFemale = null;
                _runtime.MainFemaleAnimBody = null;
                _runtime.MainFemaleFbbik = null;
            }

            if (_runtime.MainFemale == null)
            {
                _runtime.MainFemale = ResolveMainFemale(_runtime.HSceneProc);
            }
            if (_runtime.MainFemale == null)
            {
                LogResolveMissing("MainFemale");
                return false;
            }

            Animator animBody = _runtime.MainFemale.animBody;
            if (_resolvedAnimBody != animBody)
            {
                _resolvedAnimBody = animBody;
                _runtime.MainFemaleAnimBody = null;
                _runtime.MainFemaleFbbik = null;
                _nextFbbikResolveTime = 0f;
            }
            if (animBody != null && _runtime.MainFemaleAnimBody == null)
                _runtime.MainFemaleAnimBody = animBody;
            if (animBody != null && _runtime.MainFemaleFbbik == null && Time.unscaledTime >= _nextFbbikResolveTime)
            {
                _runtime.MainFemaleFbbik = ResolveFbbik(_runtime.MainFemale);
                _nextFbbikResolveTime = Time.unscaledTime + 1f;
            }

            if (!_runtime.ReadyLogged)
            {
                _runtime.ReadyLogged = true;
                LogInfo("runtime ready female=" + GetFemaleName(_runtime.MainFemale));
            }

            if (_settings.AutoSpawnOnHSceneReady && _runtime.Root == null)
            {
                RebuildAllRuntimeObjects();
            }

            return true;
        }

        private void OnHSceneEnded()
        {
            RestoreFemaleGhost();
            StopUiCapture("HScene終了");
            DetachSelectedGizmo();
            DestroyAllRuntimeObjects();
            _runtime.Clear();
            _resolvedAnimBody = null;
            _nextFbbikResolveTime = 0f;
            _nextHSceneProcResolveTime = 0f;
            ClearExternalParentTargets();
            LogInfo("HScene ended -> runtime cleared");
        }

        private static ChaControl ResolveMainFemale(HSceneProc proc)
        {
            if (proc == null)
            {
                return null;
            }

            if (RuntimeReflection.FiHSceneLstFemale != null)
            {
                IList listObj = RuntimeReflection.FiHSceneLstFemale.GetValue(proc) as IList;
                if (listObj != null)
                {
                    for (int i = 0; i < listObj.Count; i++)
                    {
                        ChaControl cha = listObj[i] as ChaControl;
                        if (cha != null)
                        {
                            return cha;
                        }
                    }
                }
            }

            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha != null && cha.sex == 1)
                {
                    return cha;
                }
            }

            return null;
        }

        private void RefreshExternalParentTargetsIfNeeded(bool force)
        {
            int femaleId = _runtime.MainFemale != null ? _runtime.MainFemale.GetInstanceID() : 0;
            float now = Time.unscaledTime;
            if (!force && femaleId == _lastExternalFemaleInstanceId && now < _nextExternalTargetRefreshTime)
            {
                return;
            }

            _lastExternalFemaleInstanceId = femaleId;
            _nextExternalTargetRefreshTime = now + 1f;
            RebuildExternalParentTargets();
        }

        private void RebuildExternalParentTargets()
        {
            _externalParentTargets.Clear();
            _externalParentTargetMap.Clear();

            ChaControl female = _runtime.MainFemale;
            if (female == null)
            {
                return;
            }

            AddExternalParentTarget("キャラ", "char:root", "キャラ本体", female.transform);

            if (female.animBody != null)
            {
                AddExternalParentTarget("キャラ", "char:animbody", "animBody", female.animBody.transform);
            }

            Animator animator = _runtime.MainFemaleAnimBody;
            if (animator != null)
            {
                AddAnimatorBoneTarget(animator, HumanBodyBones.Hips, "fk:hips", "FK 腰");
                AddAnimatorBoneTarget(animator, HumanBodyBones.Spine, "fk:spine", "FK 背骨");
                AddAnimatorBoneTarget(animator, HumanBodyBones.Chest, "fk:chest", "FK 胸");
                AddAnimatorBoneTarget(animator, HumanBodyBones.UpperChest, "fk:upperchest", "FK 上胸");
                AddAnimatorBoneTarget(animator, HumanBodyBones.Neck, "fk:neck", "FK 首");
                AddAnimatorBoneTarget(animator, HumanBodyBones.Head, "fk:head", "FK 頭");
                AddAnimatorBoneTarget(animator, HumanBodyBones.LeftShoulder, "fk:l_shoulder", "FK 左肩");
                AddAnimatorBoneTarget(animator, HumanBodyBones.RightShoulder, "fk:r_shoulder", "FK 右肩");
                AddAnimatorBoneTarget(animator, HumanBodyBones.LeftHand, "fk:l_hand", "FK 左手");
                AddAnimatorBoneTarget(animator, HumanBodyBones.RightHand, "fk:r_hand", "FK 右手");
                AddAnimatorBoneTarget(animator, HumanBodyBones.LeftFoot, "fk:l_foot", "FK 左足");
                AddAnimatorBoneTarget(animator, HumanBodyBones.RightFoot, "fk:r_foot", "FK 右足");
            }

            FullBodyBipedIK fbbik = _runtime.MainFemaleFbbik;
            if (fbbik != null)
            {
                AddExternalParentTarget("IK", "ik:solver_root", "IK SolverRoot", fbbik.transform);
                AddFbbikEffectorTarget(fbbik, FullBodyBipedEffector.Body, "ik:body", "IK 腰");
                AddFbbikEffectorTarget(fbbik, FullBodyBipedEffector.LeftHand, "ik:left_hand", "IK 左手");
                AddFbbikEffectorTarget(fbbik, FullBodyBipedEffector.RightHand, "ik:right_hand", "IK 右手");
                AddFbbikEffectorTarget(fbbik, FullBodyBipedEffector.LeftFoot, "ik:left_foot", "IK 左足");
                AddFbbikEffectorTarget(fbbik, FullBodyBipedEffector.RightFoot, "ik:right_foot", "IK 右足");
                AddFbbikEffectorTarget(fbbik, FullBodyBipedEffector.LeftShoulder, "ik:left_shoulder", "IK 左肩");
                AddFbbikEffectorTarget(fbbik, FullBodyBipedEffector.RightShoulder, "ik:right_shoulder", "IK 右肩");
                AddFbbikEffectorTarget(fbbik, FullBodyBipedEffector.LeftThigh, "ik:left_thigh", "IK 左腿");
                AddFbbikEffectorTarget(fbbik, FullBodyBipedEffector.RightThigh, "ik:right_thigh", "IK 右腿");
            }
        }

        private void AddAnimatorBoneTarget(Animator animator, HumanBodyBones bone, string key, string label)
        {
            if (animator == null)
            {
                return;
            }

            Transform tf;
            try
            {
                tf = animator.GetBoneTransform(bone);
            }
            catch
            {
                tf = null;
            }

            AddExternalParentTarget("FK", key, label, tf);
        }

        private void AddFbbikEffectorTarget(FullBodyBipedIK fbbik, FullBodyBipedEffector effectorId, string key, string label)
        {
            if (fbbik == null)
            {
                return;
            }

            IKEffector effector;
            try
            {
                effector = fbbik.solver.GetEffector(effectorId);
            }
            catch
            {
                effector = null;
            }

            Transform tf = null;
            if (effector != null)
            {
                tf = effector.target != null ? effector.target : effector.bone;
            }

            AddExternalParentTarget("IK", key, label, tf);
        }

        private void AddExternalParentTarget(string category, string key, string label, Transform transform)
        {
            if (string.IsNullOrEmpty(key) || transform == null)
            {
                return;
            }

            if (_externalParentTargetMap.ContainsKey(key))
            {
                return;
            }

            var target = new ExternalParentTarget
            {
                Category = category ?? string.Empty,
                Key = key,
                Label = label ?? key,
                Transform = transform
            };

            _externalParentTargets.Add(target);
            _externalParentTargetMap[key] = target;
        }

        private void ClearExternalParentTargets()
        {
            _externalParentTargets.Clear();
            _externalParentTargetMap.Clear();
            _lastExternalFemaleInstanceId = 0;
            _nextExternalTargetRefreshTime = 0f;
        }

        private bool TryGetExternalParentTarget(string key, out ExternalParentTarget target)
        {
            if (string.IsNullOrEmpty(key))
            {
                target = null;
                return false;
            }

            return _externalParentTargetMap.TryGetValue(key, out target) && target != null && target.Transform != null;
        }

        private Transform ResolveExternalParentTransform(string key)
        {
            return TryGetExternalParentTarget(key, out ExternalParentTarget target) ? target.Transform : null;
        }

        private bool TryGetExternalParentKeyByTransform(Transform tf, out string key)
        {
            key = null;
            if (tf == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, ExternalParentTarget> kv in _externalParentTargetMap)
            {
                ExternalParentTarget target = kv.Value;
                if (target == null || target.Transform == null)
                {
                    continue;
                }

                if (target.Transform == tf)
                {
                    key = kv.Key;
                    return true;
                }
            }

            return false;
        }

        // 動き系（GetSyncTime / TickMotions / Base 姿勢キャプチャ）は Plugin.Motion.cs を参照

        private string GetFemaleName(ChaControl cha)
        {
            if (cha == null)
            {
                return "<none>";
            }

            try
            {
                if (cha.chaFile != null && cha.chaFile.parameter != null && !string.IsNullOrEmpty(cha.chaFile.parameter.fullname))
                {
                    return cha.chaFile.parameter.fullname;
                }
            }
            catch
            {
                // ignore
            }

            return string.IsNullOrEmpty(cha.name) ? "<unnamed>" : cha.name;
        }

        private string DescribeAnimationInfo(object info)
        {
            if (info == null)
            {
                return "<none>";
            }

            object id = TryGetInstanceField(info, "id");
            object mode = TryGetInstanceField(info, "mode");
            object femaleInit = TryGetInstanceField(info, "isFemaleInitiative");
            object categories = TryGetInstanceField(info, "lstCategory");

            int categoryCount = 0;
            IList list = categories as IList;
            if (list != null)
            {
                categoryCount = list.Count;
            }

            return "id=" + SafeValueText(id)
                + " mode=" + SafeValueText(mode)
                + " femaleInit=" + SafeValueText(femaleInit)
                + " categories=" + categoryCount;
        }

        private static object TryGetInstanceField(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            try
            {
                var fi = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (fi == null)
                {
                    return null;
                }

                return fi.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private static string SafeValueText(object value)
        {
            return value == null ? "<null>" : value.ToString();
        }

        private string GetCurrentAnimatorStateText()
        {
            if (_runtime.MainFemale == null)
            {
                return "<female missing>";
            }

            try
            {
                AnimatorStateInfo info = _runtime.MainFemale.getAnimatorStateInfo(0);
                return "hash=" + info.shortNameHash + " normTime=" + info.normalizedTime.ToString("F3");
            }
            catch (Exception ex)
            {
                return "<anim read failed: " + ex.GetType().Name + ">";
            }
        }

        private string GetNowHPointName()
        {
            if (_runtime.HSceneProc == null || RuntimeReflection.FiHSceneNowHpointData == null)
            {
                return "<none>";
            }

            object value = RuntimeReflection.FiHSceneNowHpointData.GetValue(_runtime.HSceneProc);
            return value == null ? "<null>" : value.ToString();
        }

        private bool TryGetNowHPointPosition(out Vector3 pos)
        {
            pos = Vector3.zero;
            if (_runtime.HSceneProc == null || RuntimeReflection.FiHSceneNowHpointDataPos == null)
            {
                return false;
            }

            object value = RuntimeReflection.FiHSceneNowHpointDataPos.GetValue(_runtime.HSceneProc);
            if (value is Vector3 vector)
            {
                pos = vector;
                return true;
            }

            return false;
        }

        private static string FormatVec3(Vector3 value)
        {
            return "(" + value.x.ToString("F3") + ", " + value.y.ToString("F3") + ", " + value.z.ToString("F3") + ")";
        }

        private static FullBodyBipedIK ResolveFbbik(ChaControl cha)
        {
            if (cha == null || cha.animBody == null)
            {
                return null;
            }

            FullBodyBipedIK direct = cha.animBody.GetComponent<FullBodyBipedIK>();
            if (direct != null)
            {
                return direct;
            }

            return cha.animBody.GetComponentInChildren<FullBodyBipedIK>(true);
        }
    }
}
