using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private static readonly FieldInfo HActionFixLstProcField = AccessTools.Field(typeof(HSceneProc), "lstProc");

        private static void PatchHActionReturnFix(Harmony harmony)
        {
            // 自動 Harmony patch は撤去。
            // 体位＞モーション＞オートの順序は ProcessWhileWhite_ExitAstral 内で明示的に呼ぶ。
        }

        private static void PatchPluginReturnFix(Harmony harmony, string targetName, string prefixName = null, string postfixName = null)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(Plugin), targetName);
                if (target == null)
                {
                    SimpleFileLogger.Warn($"H_ACTION_RETURN_FIX patch failed: target not found {targetName}");
                    return;
                }

                HarmonyMethod prefix = null;
                HarmonyMethod postfix = null;

                if (!string.IsNullOrWhiteSpace(prefixName))
                {
                    MethodInfo p = AccessTools.Method(typeof(Plugin), prefixName);
                    if (p == null)
                    {
                        SimpleFileLogger.Warn($"H_ACTION_RETURN_FIX patch failed: prefix not found {prefixName}");
                        return;
                    }
                    prefix = new HarmonyMethod(p);
                }

                if (!string.IsNullOrWhiteSpace(postfixName))
                {
                    MethodInfo p = AccessTools.Method(typeof(Plugin), postfixName);
                    if (p == null)
                    {
                        SimpleFileLogger.Warn($"H_ACTION_RETURN_FIX patch failed: postfix not found {postfixName}");
                        return;
                    }
                    postfix = new HarmonyMethod(p);
                }

                harmony.Patch(target, prefix: prefix, postfix: postfix);
                SimpleFileLogger.Info($"Patched H_ACTION_RETURN_FIX: Plugin.{targetName}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"H_ACTION_RETURN_FIX patch failed for Plugin.{targetName}: {ex.Message}");
            }
        }

        private static void RestoreSavedPose_HActionFix_Postfix(Plugin __instance)
        {
            if (__instance == null) return;
            __instance.RestoreSavedHActionState("after-RestoreSavedPose");
        }

        private static void RestoreSavedAnimatorMotionNow_HActionFix_Prefix(Plugin __instance, string phase)
        {
            if (__instance == null) return;
            __instance.RestoreSavedHActionState("before-anim-restore:" + (phase ?? "null"));
        }

        private static void RestoreSavedAnimatorMotionNow_HActionFix_Postfix(Plugin __instance, string phase)
        {
            if (__instance == null) return;
            __instance.RestoreSavedHActionState("after-anim-restore:" + (phase ?? "null"));
        }

        private static void LogCurrentAnimatorMotion_HActionFix_Prefix(Plugin __instance, string phase)
        {
            if (__instance == null) return;
            __instance.RestoreSavedHActionState("before-anim-probe:" + (phase ?? "null"));
        }

        private HActionBase ResolveSavedHAction()
        {
            if (_hSceneProc == null || _hFlag == null || HActionFixLstProcField == null)
                return null;

            try
            {
                IList actions = HActionFixLstProcField.GetValue(_hSceneProc) as IList;
                int modeIndex = (int)_savedPoseMode;
                if (actions == null || modeIndex < 0 || modeIndex >= actions.Count)
                    return null;

                return actions[modeIndex] as HActionBase;
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"H_ACTION_RETURN_FIX ResolveSavedHAction failed: {ex.Message}");
                return null;
            }
        }

        private void RestoreSavedHActionState(string phase)
        {
            if (_hFlag == null || string.IsNullOrWhiteSpace(_savedNowAnimStateName))
                return;

            bool isSonyu =
                _savedPoseMode == HFlag.EMode.sonyu ||
                _savedPoseMode == HFlag.EMode.sonyu3P ||
                _savedPoseMode == HFlag.EMode.sonyu3PMMF;

            bool isLoop =
                _savedNowAnimStateName == "SLoop" ||
                _savedNowAnimStateName == "WLoop" ||
                _savedNowAnimStateName == "A_SLoop" ||
                _savedNowAnimStateName == "A_WLoop";

            if (!isSonyu || !isLoop)
                return;

            try
            {
                HActionBase action = ResolveSavedHAction();
                if (action == null)
                {
                    SimpleFileLogger.Warn($"H_ACTION_RETURN_FIX skipped phase={phase} reason=action-null savedMode={_savedPoseMode}");
                    return;
                }

                bool anal = _savedNowAnimStateName.StartsWith("A_", StringComparison.Ordinal);

                _hFlag.finish = HFlag.FinishKind.none;
                _hFlag.voiceWait = false;
                _hFlag.isAnalPlay = anal;

                if (anal)
                {
                    _hFlag.SetInsertAnal();
                    _hFlag.SetInsertAnalVoiceCondition();
                }
                else
                {
                    _hFlag.SetInsertKokan();
                    _hFlag.SetInsertKokanVoiceCondition();
                }

                if (_hFlag.speedCalc <= 0f)
                    _hFlag.speedCalc = 0.1f;

                _hFlag.speedMaxBody = 1f;

                try
                {
                    AnimationCurve curve = _hFlag.speedSonyuCurve;
                    _hFlag.speed = curve != null ? curve.Evaluate(_hFlag.speedCalc) : Mathf.Max(_hFlag.speed, 1f);
                }
                catch
                {
                    _hFlag.speed = Mathf.Max(_hFlag.speed, 1f);
                }

                if (_hFlag.voice != null)
                    _hFlag.voice.speedMotion = true;

                action.SetPlay(_savedNowAnimStateName);
                // オート復元はここでは行わない。体位＞モーションが落ち着いた後に別ステージで実施。

                SimpleFileLogger.Warn(
                    $"H_ACTION_RETURN_FIX applied phase={phase}"
                    + $" state={_savedNowAnimStateName}"
                    + $" pose=({_savedPoseId},{_savedPoseMode},{_savedPoseName})"
                    + $" speed={_hFlag.speed:F3}"
                    + $" speedCalc={_hFlag.speedCalc:F3}"
                    + $" anal={anal}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"H_ACTION_RETURN_FIX failed phase={phase}: {ex}");
            }
        }
    }
}
