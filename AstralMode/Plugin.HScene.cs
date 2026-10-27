using HarmonyLib;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        // アストラルモード中は本体側のH操作・更新を抑制
        internal static bool IsAstralActive()
        {
            if (Instance == null) return false;
            var s = Instance._state;
            return s != AstralState.Normal;
        }

        // HSprite.Update は必ず通す。止めると速度スライダー・感度ゲージ・体位UIが止まる。
        [HarmonyPatch(typeof(HSceneProc), "Update")]
        [HarmonyPrefix]
        private static void HSceneProc_Update_Prefix()
        {
            Plugin inst = Instance;
            if (inst == null) return;
            if (!IsAstralActive()) return;
            inst.LogHSceneProcUpdateCharaState("prefix");
        }

        [HarmonyPatch(typeof(HSceneProc), "Update")]
        [HarmonyPostfix]
        private static void HSceneProc_Update_Postfix()
        {
            Plugin inst = Instance;
            if (inst == null) return;
            if (!IsAstralActive()) return;
            inst.LogHSceneProcUpdateCharaState("postfix");
        }

        // HSceneProc.LateUpdate を抑制 -> 各 lstProc[mode].LateProc() の連鎖を止める。
        // 旧設計（本体をアストラル座標へテレポート）では、LateProc が本体を元位置へ引き戻すため抑制が必要だった。
        // だが in-place 設計では本体は据え置き（テレポートしない）ので引き戻し問題は無い。
        // 逆に LateProc は正常位等のH体位をIK/座標補正で「維持」しており、止めると体が素のポーズ（立ち/Tポーズ）へ崩れる。
        // → 既定では抑制しない（LateProc を走らせて体位を維持）。config で旧挙動に戻せる。
        [HarmonyPatch(typeof(HSceneProc), "LateUpdate")]
        [HarmonyPrefix]
        private static bool HSceneProc_LateUpdate_Prefix()
        {
            if (!IsAstralActive()) return true;
            Plugin inst = Instance;
            bool suppress = inst != null && inst.SuppressHSceneLateUpdate;
            return !suppress;
        }

        // HScene開始: HSceneProc.Start (IEnumerator) の Postfix で参照確保
        [HarmonyPatch(typeof(HSceneProc), "Start")]
        [HarmonyPostfix]
        private static void HSceneProc_Start_Postfix(HSceneProc __instance)
        {
            if (Instance != null)
            {
                Instance.DiscardVmdAstral();
                CharaFreezeClone.DestroyLeakedCloneRoots();
                Instance.DestroyWhiteRoom();
                Instance.HideAstralLight();
                Instance.DestroyRealWorldSubCamera();
                Instance.ResumeMainGameSubCameraFromAstral("hscene-start-cleanup");
                Instance.RestoreAstralSkybox();
            }

            _hSceneProc = __instance;
            _hFlag = __instance != null ? __instance.flags : null;
            _inHScene = true;
            _simultOrgasmCount = 0;
            SimpleFileLogger.Info($"HScene start detected. hFlag={(_hFlag != null ? "OK" : "null")}");
        }

        // HScene終了
        [HarmonyPatch(typeof(HSceneProc), "OnDestroy")]
        [HarmonyPostfix]
        private static void HSceneProc_OnDestroy_Postfix()
        {
            SimpleFileLogger.Info("HScene end detected");

            if (Instance != null)
            {
                Instance.RestoreMainCameraToReal();
                Instance.EndExternalAstralRuntime();
                Instance.DestroyRealWorldSubCamera();
                Instance.ResumeMainGameSubCameraFromAstral("hscene-destroy");
                Instance.RestoreAstralSkybox();
                Instance.HideAstralLight();
                Instance.DestroyWhiteRoom();
                Instance.RestoreCharasToReal();
                Instance.UnfreezeAllCharacterAnimators();
                Instance.DestroyWarpEffects();
                Instance.DiscardVmdAstral();
                CharaFreezeClone.DestroyLeakedCloneRoots();
                Instance.StopAstralBgm();
                Instance.DestroyAstralBgm();
                Instance.DetachFovPreCullHook();
                Instance.SetAstralCleanVisualsActive(false, "hscene-destroy");
                Instance._state = AstralState.Normal;
            }

            _hSceneProc = null;
            _hFlag = null;
            _inHScene = false;
            _simultOrgasmCount = 0;
        }

        // 同時絶頂(Sonyu) カウント
        [HarmonyPatch(typeof(HFlag), nameof(HFlag.AddSonyuSame))]
        [HarmonyPostfix]
        private static void AddSonyuSame_Postfix()
        {
            OnSimultOrgasm("AddSonyuSame");
        }

        // 同時絶頂(SonyuAnal) カウント
        [HarmonyPatch(typeof(HFlag), nameof(HFlag.AddSonyuAnalSame))]
        [HarmonyPostfix]
        private static void AddSonyuAnalSame_Postfix()
        {
            OnSimultOrgasm("AddSonyuAnalSame");
        }

        // 同時絶頂からの自動突入も、部屋の外では起こさない。
        // (後始末側のパッチは塞がない。塞ぐと界に入ったまま帰れなくなる)
        private static void OnSimultOrgasm(string source)
        {
            if (Instance == null) return;
            if (!CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom) return;
            if (Instance._cfgEnabled == null || !Instance._cfgEnabled.Value) return;
            if (Instance._state != AstralState.Normal) return;

            _simultOrgasmCount++;
            int threshold = Instance._cfgOrgasmCountToTrigger != null ? Instance._cfgOrgasmCountToTrigger.Value : 10;
            SimpleFileLogger.Info($"Simultaneous orgasm count: {_simultOrgasmCount}/{threshold} ({source})");

            if (_simultOrgasmCount >= threshold)
            {
                _simultOrgasmCount = 0;
                SimpleFileLogger.Info("Threshold reached -> trigger astral mode");
                Instance.BeginEntryClimaxVoiceGrace(source);
                Instance.StartCoroutine(Instance.EnterAstralCoroutine());
            }
        }
    }
}
