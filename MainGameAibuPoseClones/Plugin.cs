using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MainGameAibuPoseClones
{
    [BepInPlugin("com.kks.maingame.aibuposeclones", "MainGameAibuPoseClones", "0.1.0")]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private static readonly FieldInfo UseListField = AccessTools.Field(typeof(HSceneProc), "lstUseAnimInfo");
        private static readonly FieldInfo FemaleListField = AccessTools.Field(typeof(HSceneProc), "lstFemale");
        private static readonly FieldInfo MaleField = AccessTools.Field(typeof(HSceneProc), "male");
        private static readonly FieldInfo ActionFlagsField = AccessTools.Field(typeof(HActionBase), "flags");
        private static readonly FieldInfo ItemDictionaryField = AccessTools.Field(typeof(HandCtrl), "dicItem");
        private static readonly MethodInfo ShallowCopyMethod = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static readonly Dictionary<int, string> TargetNames = new Dictionary<int, string>
        {
            { 0, "立ち愛撫" },
            { 1, "仰向け愛撫" },
            { 7, "座り愛撫" }
        };

        private static Plugin _instance;
        private Harmony _harmony;
        private HSceneProc _observedScene;
        private string _lastMotionSignature;
        private float _nextMotionSample;
        private float _nextSceneFind;
        private HandCtrl _hiddenHandOwner;
        private readonly Dictionary<Renderer, bool> _hiddenHandRenderers = new Dictionary<Renderer, bool>();
        private bool _wasClone;
        private float _orgasmAfterStarted = -1f;
        private bool _loggedNoHandMeshes;
        private static HSceneProc.AnimationListInfo _changingToPose;

        private void Awake()
        {
            _instance = this;
            _harmony = new Harmony("com.kks.maingame.aibuposeclones");
            _harmony.PatchAll(typeof(Plugin));
        }

        private void OnDestroy()
        {
            RestoreHandRenderers();
            _harmony?.UnpatchSelf();
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        [HarmonyPatch(typeof(HSceneProc), "ChangeAnimator")]
        [HarmonyPrefix]
        private static void TrackSelectedPose(HSceneProc.AnimationListInfo _nextAinmInfo)
        {
            // MotionChange calls SetPlay before nowAnimationInfo is updated.
            _changingToPose = _nextAinmInfo;
        }

        [HarmonyPatch(typeof(HSceneProc), "ChangeAnimator")]
        [HarmonyPostfix]
        private static void FinishSelectedPose(HSceneProc __instance)
        {
            try
            {
                var pose = __instance.flags?.nowAnimationInfo;
                if (!IsClone(pose) || pose.id != 1) return;
                var actions = AccessTools.Field(typeof(HSceneProc), "lstProc")?.GetValue(__instance)
                    as List<HActionBase>;
                int mode = (int)HFlag.EMode.aibu;
                if (actions == null || actions.Count <= mode || !(actions[mode] is HAibu)) return;
                actions[mode].SetPlay("A_Idle");
                _instance?.Logger.LogInfo("[GroinPose] ChangeAnimator completed; forced A_Idle for selected clone");
            }
            finally
            {
                _changingToPose = null;
            }
        }


        [HarmonyPatch(typeof(HActionBase), "SetPlay", new[] { typeof(string), typeof(bool) })]
        [HarmonyPrefix]
        private static void KeepSupineCloneGroinMotion(HActionBase __instance, ref string _nextAnimation)
        {
            if (!(__instance is HAibu)) return;
            var flags = ActionFlagsField?.GetValue(__instance) as HFlag;
            var pose = _changingToPose ?? flags?.nowAnimationInfo;
            if (pose == null || pose.id != 1 || !IsClone(pose)) return;
            string current = flags.nowAnimStateName ?? string.Empty;
            if (current.StartsWith("Orgasm", StringComparison.Ordinal) ||
                current.StartsWith("After", StringComparison.Ordinal)) return;
            switch (_nextAnimation)
            {
                case "Idle":
                case "A_Touch":
                case "K_Touch":
                case "M_Touch":
                case "S_Touch":
                case "K_Loop":
                case "M_Idle":
                case "S_Idle":
                    _nextAnimation = "A_Idle";
                    break;
            }
        }

        [HarmonyPatch(typeof(HAibu), "Proc")]
        [HarmonyPostfix]
        private static void RestoreGroinPoseAfterOrgasm(HAibu __instance)
        {
            var plugin = _instance;
            if (plugin == null) return;
            var flags = ActionFlagsField?.GetValue(__instance) as HFlag;
            if (!IsClone(flags?.nowAnimationInfo) || flags.nowAnimationInfo.id != 1)
            {
                plugin._orgasmAfterStarted = -1f;
                return;
            }
            if (flags.nowAnimStateName != "Orgasm_A")
            {
                plugin._orgasmAfterStarted = -1f;
                return;
            }
            if (plugin._orgasmAfterStarted < 0f) plugin._orgasmAfterStarted = Time.unscaledTime;
            if (Time.unscaledTime - plugin._orgasmAfterStarted < 0.5f ||
                flags.voice.playVoices[0] != -1 || Manager.Voice.IsPlay(flags.transVoiceMouth[0])) return;
            var scene = plugin._observedScene;
            var female = scene != null ? FemaleListField?.GetValue(scene) as List<ChaControl> : null;
            if (female == null || female.Count == 0 || female[0] == null ||
                !female[0].getAnimatorStateInfo(0).IsName("Orgasm_A") ||
                female[0].getAnimatorStateInfo(0).normalizedTime < 1f) return;
            __instance.SetPlay("A_Idle");
            plugin.Logger.LogInfo("[GroinPose] orgasm finished; restored A_Idle");
            plugin._orgasmAfterStarted = -1f;
        }

        [HarmonyPatch(typeof(HActionBase), "SetPlay", new[] { typeof(string), typeof(bool) })]
        [HarmonyPostfix]
        private static void LogMotionPlay(HActionBase __instance, string _nextAnimation)
        {
            var plugin = _instance;
            if (plugin == null) return;
            var flags = ActionFlagsField?.GetValue(__instance) as HFlag;
            var pose = flags?.nowAnimationInfo;
            if (pose == null) return;
            plugin.Logger.LogInfo("[HMotionPlay] mode=" + pose.mode + " id=" + pose.id
                + " pose=" + pose.nameAnimation + " state=" + _nextAnimation);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextMotionSample) return;
            _nextMotionSample = Time.unscaledTime + 0.1f;

            if (_observedScene == null || !_observedScene)
            {
                if (Time.unscaledTime < _nextSceneFind) return;
                _nextSceneFind = Time.unscaledTime + 0.5f;
                _observedScene = FindObjectOfType<HSceneProc>();
                _lastMotionSignature = null;
            }
            if (_observedScene == null || _observedScene.flags == null
                || _observedScene.flags.nowAnimationInfo == null)
            {
                RestoreHandRenderers();
                _wasClone = false;
                return;
            }

            bool clone = IsClone(_observedScene.flags.nowAnimationInfo)
                && _observedScene.flags.nowAnimationInfo.id == 1;
            if (!clone)
            {
                if (_wasClone) Logger.LogInfo("[GroinPose] clone exited; hand renderers restored");
                RestoreHandRenderers();
            }
            else
            {
                HideCloneHandRenderers(_observedScene.hand);
                if (!_wasClone) Logger.LogInfo("[GroinPose] clone active; A_Idle fixed, hand hidden");
            }
            _wasClone = clone;

            try
            {
                HFlag flags = _observedScene.flags;
                HSceneProc.AnimationListInfo pose = flags.nowAnimationInfo;
                var females = FemaleListField?.GetValue(_observedScene) as List<ChaControl>;
                var male = MaleField?.GetValue(_observedScene) as ChaControl;
                string femaleClips = females != null && females.Count > 0 ? DescribeAnimator(females[0]) : "<none>";
                string maleClips = DescribeAnimator(male);
                string state = flags.nowAnimStateName ?? "<none>";
                string signature = pose.mode + "/" + pose.id + "/" + pose.nameAnimation
                    + "/" + state + "/" + femaleClips + "/" + maleClips;
                if (signature == _lastMotionSignature) return;
                _lastMotionSignature = signature;
                Logger.LogInfo("[HMotion] mode=" + pose.mode + " id=" + pose.id
                    + " pose=" + pose.nameAnimation + " state=" + state
                    + " click=" + flags.click + " speed=" + flags.speed.ToString("F3")
                    + " motion=" + flags.motion.ToString("F3")
                    + " female=" + femaleClips + " male=" + maleClips);
            }
            catch (Exception ex)
            {
                // A transient animator teardown should not interrupt H gameplay.
                Logger.LogWarning("[HMotion] sample failed: " + ex.GetType().Name + ": " + ex.Message);
                _nextMotionSample = Time.unscaledTime + 2f;
            }
        }

        private void HideCloneHandRenderers(HandCtrl owner)
        {
            if (owner == null) return;
            if (_hiddenHandOwner != null && _hiddenHandOwner != owner) RestoreHandRenderers();
            _hiddenHandOwner = owner;
            var items = ItemDictionaryField?.GetValue(owner) as Dictionary<int, HandCtrl.AibuItem>;
            if (items == null) return;
            int matched = 0;
            foreach (var pair in items)
            {
                var item = pair.Value;
                // isMaterialGet marks the game-owned hand mesh; leave other love items visible.
                if (item == null || item.mHand == null) continue;
                matched++;
                HideRenderer(item.renderBody);
                HideRenderer(item.meshRenderBody);
                HideRenderer(item.renderSilhouette);
                HideRenderer(item.meshRenderSilhouette);
            }
            if (matched == 0 && !_loggedNoHandMeshes)
            {
                Logger.LogWarning("[GroinPose] no hand meshes identified in HandCtrl");
                _loggedNoHandMeshes = true;
            }
        }

        private void HideRenderer(Renderer renderer)
        {
            if (renderer == null) return;
            if (!_hiddenHandRenderers.ContainsKey(renderer))
            {
                _hiddenHandRenderers.Add(renderer, renderer.enabled);
                Logger.LogInfo("[GroinPose] hide hand renderer " + renderer.name);
            }
            renderer.enabled = false;
        }

        private void RestoreHandRenderers()
        {
            foreach (var pair in _hiddenHandRenderers)
            {
                if (pair.Key != null)
                {
                    pair.Key.enabled = pair.Value;
                    Logger.LogInfo("[GroinPose] restore hand renderer " + pair.Key.name);
                }
            }
            _hiddenHandRenderers.Clear();
            _hiddenHandOwner = null;
            _loggedNoHandMeshes = false;
        }

        private static string DescribeAnimator(ChaControl character)
        {
            Animator animator = character != null ? character.animBody : null;
            if (animator == null) return "<none>";
            var sb = new StringBuilder();
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                AnimatorClipInfo[] current = animator.GetCurrentAnimatorClipInfo(layer);
                AnimatorClipInfo[] next = animator.IsInTransition(layer)
                    ? animator.GetNextAnimatorClipInfo(layer) : null;
                if ((current == null || current.Length == 0) && (next == null || next.Length == 0)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append('L').Append(layer).Append('=');
                AppendClips(sb, current);
                if (next != null && next.Length > 0)
                {
                    sb.Append("->");
                    AppendClips(sb, next);
                }
            }
            return sb.Length == 0 ? "<no-clips>" : sb.ToString();
        }

        private static void AppendClips(StringBuilder sb, AnimatorClipInfo[] clips)
        {
            if (clips == null || clips.Length == 0) { sb.Append("<none>"); return; }
            for (int i = 0; i < clips.Length; i++)
            {
                if (i > 0) sb.Append('+');
                sb.Append(clips[i].clip != null ? clips[i].clip.name : "<null>");
            }
        }

        [HarmonyPatch(typeof(HSceneProc), "CreateListAnimationFileName")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void AddCloneEntries(HSceneProc __instance)
        {
            var lists = UseListField?.GetValue(__instance) as List<HSceneProc.AnimationListInfo>[];
            if (lists == null || lists.Length <= (int)HFlag.EMode.aibu) return;
            var aibu = lists[(int)HFlag.EMode.aibu];
            if (aibu == null) return;

            // Only duplicate entries already available in this H scene. This keeps
            // the game's original map and progress restrictions intact.
            for (int i = aibu.Count - 1; i >= 0; i--)
            {
                var original = aibu[i];
                if (original == null || !TargetNames.TryGetValue(original.id, out string expectedName)
                    || !string.Equals(original.nameAnimation, expectedName, StringComparison.Ordinal)) continue;
                string cloneName = expectedName + "【クローン】";
                if (aibu.Exists(x => x != null && x.id == original.id && x.nameAnimation == cloneName)) continue;
                var clone = ShallowCopyMethod?.Invoke(original, null) as HSceneProc.AnimationListInfo;
                if (clone == null) continue;
                clone.nameAnimation = cloneName;
                aibu.Insert(i + 1, clone);
            }
        }

        private static bool IsClone(HSceneProc.AnimationListInfo info)
        {
            return info != null && info.mode == HFlag.EMode.aibu
                && TargetNames.TryGetValue(info.id, out string name)
                && string.Equals(info.nameAnimation, name + "【クローン】", StringComparison.Ordinal);
        }

        [HarmonyPatch(typeof(HSprite), "OnChangePlaySelect")]
        [HarmonyPrefix]
        private static bool SelectSameAnimationVariant(HSprite __instance, GameObject objClick)
        {
            if (objClick == null || __instance?.flags?.nowAnimationInfo == null) return true;
            var toggle = objClick.GetComponent<Toggle>();
            if (toggle != null && !toggle.interactable) return true;
            var component = objClick.GetComponent<HSprite.AnimationInfoComponent>();
            var selected = component?.info;
            var current = __instance.flags.nowAnimationInfo;
            if (selected == null || selected.mode != current.mode || selected.id != current.id
                || ReferenceEquals(selected, current) || (!IsClone(selected) && !IsClone(current))) return true;

            // The stock click handler returns early when mode/id are equal.
            // Queue the normal position-change action so switching between the
            // original entry and its clone still selects the clicked entry.
            __instance.flags.selectAnimationListInfo = selected;
            __instance.flags.voiceWait = true;
            __instance.flags.click = HFlag.ClickKind.actionChange;
            return false;
        }
    }
}
