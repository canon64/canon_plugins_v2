using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using CanonPlugins.Shared;
using HarmonyLib;
using UnityEngine;

namespace MainGameObjectComposer
{
    // Game asset orders: Lookat_dan=6000, FullBodyBipedIK=9999,
    // HSceneProc (including HipHijack's postfix)=12101, DynamicBone=12250.
    [DefaultExecutionOrder(12150)]
    public sealed class BetterPenetrationLateFollow : MonoBehaviour
    {
        internal Plugin Owner;
        private void LateUpdate() { if (Owner != null) Owner.FlushBetterPenetrationFollow(); }
        private void OnDisable() { if (Owner != null) Owner.ClearBetterPenetrationPending(); }
    }

    public sealed partial class Plugin
    {
        private readonly DeferredTargetQueue<PenetrationFollowContext> _penetrationCalls =
            new DeferredTargetQueue<PenetrationFollowContext>();
        private Harmony _penetrationHarmony;
        private BetterPenetrationLateFollow _penetrationLateFollow;
        private MethodInfo _setDanTarget;
        private FieldInfo _bpFemale, _bpMale, _bpReferenceTarget;
        private FieldInfo _bpDanAgents, _bpCollisionAgents, _bpNewTargets;
        private FieldInfo _bpScene, _bpInScene, _bpLoadingCharacter;
        private bool _penetrationFollowReady;
        private readonly object[] _penetrationInvokeArgs = new object[2];

        private void InitializeBetterPenetrationFollow()
        {
            if (!Chainloader.PluginInfos.TryGetValue(BetterPenetrationGuid, out var info)
                || info.Instance == null) return;
            try
            {
                Type pluginType = info.Instance.GetType();
                Assembly assembly = pluginType.Assembly;
                Type danType = assembly.GetType("Core_BetterPenetration.DanAgent", true);
                Type collisionType = assembly.GetType("Core_BetterPenetration.CollisionAgent", true);
                Type coreType = assembly.GetType("Core_BetterPenetration.CoreGame", true);
                _setDanTarget = AccessTools.DeclaredMethod(danType, "SetDanTarget", new[] { collisionType, typeof(bool) });
                if (_setDanTarget == null || _setDanTarget.IsStatic || _setDanTarget.ReturnType != typeof(void))
                    throw new MissingMethodException(danType.FullName, "SetDanTarget(CollisionAgent, bool)");
                _bpFemale = RequirePenetrationField(collisionType, "m_collisionCharacter", typeof(ChaControl), false);
                _bpMale = RequirePenetrationField(danType, "m_danCharacter", typeof(ChaControl), false);
                _bpReferenceTarget = RequirePenetrationField(danType, "m_referenceTarget", typeof(Transform), false);
                _bpDanAgents = RequirePenetrationField(coreType, "danAgents", typeof(IList), true);
                _bpCollisionAgents = RequirePenetrationField(coreType, "collisionAgents", typeof(IList), true);
                _bpNewTargets = RequirePenetrationField(coreType, "danHasNewTarget", typeof(IList), true);
                _bpScene = RequirePenetrationField(pluginType, "hSceneProc", typeof(MonoBehaviour), true);
                _bpInScene = RequirePenetrationField(pluginType, "inHScene", typeof(bool), true);
                _bpLoadingCharacter = RequirePenetrationField(pluginType, "loadingCharacter", typeof(bool), true);

                // Install the dispatcher first. Never suppress BP without a live receiver.
                _penetrationLateFollow = gameObject.AddComponent<BetterPenetrationLateFollow>();
                _penetrationLateFollow.Owner = this;
                _penetrationHarmony = new Harmony(Guid + ".betterpenetration.follow");
                _penetrationHarmony.Patch(_setDanTarget,
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(BetterPenetrationSetTargetPrefix)));
                _penetrationFollowReady = true;
                LogInfo("Better Penetration follow ready: Composer腰連動中はSetDanTargetをLateUpdate(12150)へ移動");
            }
            catch (Exception ex)
            {
                DisposeBetterPenetrationFollow();
                LogWarn("Better Penetration follow unavailable; 通常処理を維持: " + ex.Message);
            }
        }

        private static FieldInfo RequirePenetrationField(Type type, string name, Type expected, bool isStatic)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null || field.IsStatic != isStatic || !expected.IsAssignableFrom(field.FieldType))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        // Use argument indices so this also tolerates parameter-name changes in BP.
        private static bool BetterPenetrationSetTargetPrefix(object __instance, object __0, bool __1)
        {
            Plugin owner = _instance;
            if (owner == null || !owner._penetrationFollowReady) return true;
            try
            {
                bool defer = owner.TryGetPenetrationFollowContext(__instance, __0, out var context);
                return owner._penetrationCalls.ShouldRunOriginal(Time.frameCount, __instance, __0, __1, context, defer);
            }
            catch (Exception ex)
            {
                owner.DisablePenetrationFollow(ex);
                return true;
            }
        }

        private bool TryGetPenetrationFollowContext(object agent, object target, out PenetrationFollowContext context)
        {
            context = default;
            if (!_penetrationFollowReady || !isActiveAndEnabled || _penetrationLateFollow == null
                || !_penetrationLateFollow.isActiveAndEnabled || !BlankRoomGate.IsInBlankRoom
                || agent == null || target == null || _runtime.HSceneProc == null
                || !_runtime.HSceneProc.isActiveAndEnabled || _runtime.MainFemale == null
                || _runtime.MainFemaleFbbik == null || !_runtime.MainFemaleFbbik.isActiveAndEnabled)
                return false;
            if (!(bool)_bpInScene.GetValue(null) || (bool)_bpLoadingCharacter.GetValue(null)
                || !ReferenceEquals(_bpScene.GetValue(null), _runtime.HSceneProc)) return false;

            var agents = _bpDanAgents.GetValue(null) as IList;
            var targets = _bpCollisionAgents.GetValue(null) as IList;
            int agentIndex = IndexOfIdentity(agents, agent);
            if (agentIndex < 0 || IndexOfIdentity(targets, target) < 0) return false;
            var needsTarget = _bpNewTargets.GetValue(null) as IList;
            if (needsTarget == null || agentIndex >= needsTarget.Count || (bool)needsTarget[agentIndex]) return false;

            ChaControl female = _bpFemale.GetValue(target) as ChaControl;
            ChaControl male = _bpMale.GetValue(agent) as ChaControl;
            if (female == null || male == null || female != _runtime.MainFemale
                || female.animBody == null || male.animBody == null) return false;
            // HipHijack and Composer both select the first live woman in lstFemale.
            // Read the actual list again so a cached Composer reference cannot cross a swap.
            var females = RuntimeReflection.FiHSceneLstFemale?.GetValue(_runtime.HSceneProc) as IList;
            ChaControl currentFemale = null;
            if (females != null)
                foreach (object item in females)
                    if (item is ChaControl character && character != null) { currentFemale = character; break; }
            if (female != currentFemale) return false;

            if (!HipHijackBridge.TryGet("body", out string id) || !HipHijackBridge.IsBodyIkRunning()
                || !TryGetManagedObjectTransform(id, out Transform composerTarget)) return false;
            context = new PenetrationFollowContext
            {
                Scene = _runtime.HSceneProc,
                Female = female,
                Male = male,
                FemaleBody = female.animBody,
                MaleBody = male.animBody,
                ReferenceTarget = _bpReferenceTarget.GetValue(agent) as Transform,
                ComposerTarget = composerTarget,
                Animation = _runtime.HSceneProc.flags?.nowAnimationInfo,
                Preset = _activePreset
            };
            return true;
        }

        private static int IndexOfIdentity(IList values, object value)
        {
            if (values == null) return -1;
            for (int i = 0; i < values.Count; i++)
                if (ReferenceEquals(values[i], value)) return i;
            return -1;
        }

        internal void FlushBetterPenetrationFollow()
        {
            if (!_penetrationFollowReady) return;
            try
            {
                _penetrationCalls.Drain(Time.frameCount, IsPenetrationCallCurrent, ReplayPenetrationCall);
            }
            catch (Exception ex) { DisablePenetrationFollow(ex); }
        }

        private bool IsPenetrationCallCurrent(DeferredTargetQueue<PenetrationFollowContext>.Call call)
        {
            return TryGetPenetrationFollowContext(call.Agent, call.Target, out var current)
                && call.Context.Matches(current);
        }

        private void ReplayPenetrationCall(DeferredTargetQueue<PenetrationFollowContext>.Call call)
        {
            _penetrationInvokeArgs[0] = call.Target;
            _penetrationInvokeArgs[1] = call.TwoDans;
            try { _setDanTarget.Invoke(call.Agent, _penetrationInvokeArgs); }
            finally { _penetrationInvokeArgs[0] = null; }
        }

        internal void ClearBetterPenetrationPending() { _penetrationCalls.Clear(); }

        private void DisablePenetrationFollow(Exception ex)
        {
            _penetrationFollowReady = false;
            ClearBetterPenetrationPending();
            // Do not retry a partially executed BP calculation this frame.
            LogWarn("Better Penetration follow disabled; 次回から通常処理へ復帰: "
                + (ex.InnerException ?? ex).Message);
        }

        private void DisposeBetterPenetrationFollow()
        {
            _penetrationFollowReady = false;
            ClearBetterPenetrationPending();
            try
            {
                if (_penetrationHarmony != null && _setDanTarget != null)
                    _penetrationHarmony.Unpatch(_setDanTarget, HarmonyPatchType.Prefix, _penetrationHarmony.Id);
            }
            catch (Exception ex) { LogWarn("Better Penetration follow unpatch: " + ex.Message); }
            _penetrationHarmony = null;
            if (_penetrationLateFollow != null)
            {
                _penetrationLateFollow.Owner = null;
                Destroy(_penetrationLateFollow);
                _penetrationLateFollow = null;
            }
        }
    }
}
