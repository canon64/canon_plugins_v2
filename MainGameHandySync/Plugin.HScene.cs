using HarmonyLib;
using System.Collections;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameHandySync
{
    internal sealed partial class Plugin
    {
        private static HSceneProc _hSceneProc;
        private static HFlag _hFlag;
        private static bool _inHScene;

        internal static HSceneProc HSceneProc => _hSceneProc;
        internal static HFlag HFlag => _hFlag;
        internal static bool InHScene => _inHScene;

        // HScene開始
        [HarmonyPatch(typeof(HSceneProc), "Start")]
        [HarmonyPostfix]
        private static void HSceneProc_Start_Postfix(HSceneProc __instance)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            _hSceneProc = __instance;
            _hFlag = __instance != null ? __instance.flags : null;
            _inHScene = true;

            SimpleFileLogger.Info($"HScene start detected. mode={(_hFlag != null ? _hFlag.mode.ToString() : "null")}");

            // TODO: Start piston sampling + Handy sync if enabled and in sonyu mode
            if (Instance != null && Instance.IsEnabled())
            {
                // Instance.StartHandySyncForCurrentScene();
            }
        }

        // HScene終了
        [HarmonyPatch(typeof(HSceneProc), "OnDestroy")]
        [HarmonyPostfix]
        private static void HSceneProc_OnDestroy_Postfix()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            SimpleFileLogger.Info("HScene end detected");

            // TODO: Stop sync + optional Funscript export
            if (Instance != null)
            {
                // Instance.StopHandySyncAndExportIfNeeded();
            }

            _hSceneProc = null;
            _hFlag = null;
            _inHScene = false;
        }

        // Fallback HScene detection (same pattern as AstralMode)
        private float _nextHSceneCheckTime;

        private void EnsureHSceneRefs()
        {
            if (Time.unscaledTime < _nextHSceneCheckTime) return;
            _nextHSceneCheckTime = Time.unscaledTime + 1f;

            if (!_inHScene)
            {
                var proc = FindObjectOfType<HSceneProc>();
                if (proc != null)
                {
                    _hSceneProc = proc;
                    _hFlag = proc.flags;
                    _inHScene = true;
                    SimpleFileLogger.Info("HScene detected via fallback FindObjectOfType");
                }
            }
            else
            {
                if (_hSceneProc == null || !_hSceneProc)
                {
                    SimpleFileLogger.Info("HScene end detected via fallback");
                    _hSceneProc = null;
                    _hFlag = null;
                    _inHScene = false;
                }
            }
        }
    }
}
