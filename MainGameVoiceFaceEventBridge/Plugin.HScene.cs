using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameVoiceFaceEventBridge
{
    // Plugin.HScene.cs
    //
    // 責務: HSceneProc を取得/解決するユーティリティ、HSceneProc 起動時/破棄時の
    //       Harmony patch、毎フレームの遅延アクション消化 (ProcessDelayedActions)。
    //       コマンド本体や設定とは独立した「Hシーン状態への配線」を担う。
    internal sealed partial class Plugin
    {
        private Action<Exception> _delayedActionErrorHandler;

        private void LogDelayedActionError(Exception ex)
        {
            LogWarn("[delayed] 実行失敗: " + ex.Message);
        }
        // ----------------------------------------------------------------
        // 遅延アクション処理
        // ----------------------------------------------------------------

        private void ProcessDelayedActions()
        {
            float now = Time.unscaledTime;
            if (_delayedActionErrorHandler == null)
                _delayedActionErrorHandler = LogDelayedActionError;
            _actionScheduler.DrainDue(now, _delayedActionErrorHandler);
        }



        private HSceneProc FindCurrentProc()
        {
            if (CurrentProc != null)
            {
                return CurrentProc;
            }

            float now = Time.unscaledTime;
            if (now < _nextProcProbeTime)
            {
                return null;
            }

            _nextProcProbeTime = now + 1f;
            CurrentProc = UnityEngine.Object.FindObjectOfType<HSceneProc>();
            return CurrentProc;
        }

        private static int ClampMainIndex(HSceneProc proc, int index)
        {
            if (proc == null || proc.flags == null || proc.flags.lstHeroine == null || proc.flags.lstHeroine.Count <= 0)
            {
                return index < 0 ? 0 : index;
            }

            if (index < 0)
            {
                return 0;
            }

            if (index >= proc.flags.lstHeroine.Count)
            {
                return proc.flags.lstHeroine.Count - 1;
            }

            return index;
        }

        private static ChaControl ResolveFemale(HSceneProc proc, int main)
        {
            if (proc == null || LstFemaleField == null)
            {
                return null;
            }

            try
            {
                IList females = LstFemaleField.GetValue(proc) as IList;
                if (females == null || females.Count <= 0)
                {
                    return null;
                }

                int index = main;
                if (index < 0)
                {
                    index = 0;
                }
                if (index >= females.Count)
                {
                    index = females.Count - 1;
                }

                return females[index] as ChaControl;
            }
            catch (Exception ex)
            {
                LogWarn("resolve female failed: " + ex.Message);
                return null;
            }
        }

        private static int ResolveVoiceNo(HSceneProc proc, int main)
        {
            if (proc == null || proc.flags == null || proc.flags.lstHeroine == null)
            {
                return -1;
            }

            if (main < 0 || main >= proc.flags.lstHeroine.Count || proc.flags.lstHeroine[main] == null)
            {
                return -1;
            }

            return proc.flags.lstHeroine[main].voiceNo;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneProc), "CreateListAnimationFileName")]
        private static void CreateListAnimationFileNamePostfix(HSceneProc __instance)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            CurrentProc = __instance;
            if (Instance != null)
            {
                Instance.EnsurePoseClassificationFilesFromProc(__instance);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneProc), "OnDestroy")]
        private static void HSceneOnDestroyPostfix(HSceneProc __instance)
        {
            if (CurrentProc == __instance)
            {
                if (Instance != null)
                {
                    Instance._externalVoicePlayer?.Stop("HScene destroyed");
                    Instance.ResetNativeVoiceAdmission();
                    Instance._voiceGuardState.ClearBlock();
                    Instance.RestoreVoiceProcStopIfNeeded();
                    Instance._facePresetProbe = null;
                }
                CurrentProc = null;
                Log("released HSceneProc at OnDestroy");
            }
        }
    }
}
