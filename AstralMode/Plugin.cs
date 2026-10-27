using Bindings = CanonPlugins.Shared.PluginInput;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace AstralMode
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.astralmode";
        public const string PluginName = "AstralMode";
        public const string Version = "0.1.0";

        public static Plugin Instance { get; private set; }
        internal static bool DiagnosticsEnabled => Instance?._cfgVerboseLog?.Value ?? false;

        private static HFlag _hFlag;
        private static HSceneProc _hSceneProc;
        private static bool _inHScene;
        private static int _simultOrgasmCount;

        /// <summary>
        /// D キーの位置ダンプを動かすかどうか。既定は false で無効。
        /// 素の文字キーを直書きしているのでゲーム側の入力と衝突する。調査が必要になったら
        /// ここを true にしてビルドし直す。const にすると本体が到達不能扱いになるので readonly。
        /// </summary>
        private static readonly bool DebugDumpKeyEnabled = false;

        internal enum AstralState
        {
            Normal,
            Warping,
            WhiteHold,
            FadeIn,
            Astral,
            ReturnWarping,
            ReturnHold,
            ReturnFadeOut
        }
        private AstralState _state = AstralState.Normal;
        internal AstralState State => _state;

        private void Awake()
        {
            Instance = this;

            string pluginDir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            SimpleFileLogger.Init(Path.Combine(pluginDir, "AstralMode.log"));
            SimpleFileLogger.Info($"=== {PluginName} {Version} Awake ===");

            SetupConfig();
            SetupAstralSteadyEffectsConfig();
            InitWhiteRoomConfig();
            InitAstralSkyboxMaterialConfig();
            InitWarpSoundConfig();
            InitAstralBgmConfig();
            InitAstralSkybox(pluginDir);
            InitAstralHill(pluginDir);
            InitAstralLight();

            ApplyHarmonyPatchesIndividually();
        }

        private float _nextHSceneCheckTime;

        // BlankMapAdd の追加マップ以外では一切発火させない。
        private bool _gateWasInRoom;

        private void Update()
        {
            // 霧の見張りは何より先に置く。この下の門で早期 return する経路があるため、
            // ここより後ろに置くと部屋を出た瞬間に見張りが届かなくなる。
            TickAstralFogSafety();

            // 界に入ったまま部屋を出た場合は、緊急脱出を1回だけ走らせて現実を汚さずに畳む。
            if (!CanonPlugins.Shared.BlankRoomGate.Tick(ref _gateWasInRoom, OnBlankRoomLeft)) return;

            UpdateEdgeFadeDisplay();
if (_cfgEnabled == null || !_cfgEnabled.Value) return;

            UpdateAstralCursorState();
            TickSceneryRebuild();      // 設定スライダーの手が止まってから景色を組み直す
            EnsureBenchPoseToggles();  // 体位ごとのベンチ許可チェックボックスを揃える
            UpdateFurnitureFollow();   // 挿入体位のあいだベッドをキャラの下へ寄せる
            LogAstralCharaPositionDriftOnly("update");
            MaintainPoseChangeAstralReapply();
            UpdateRealWorldSubCameraDynamic();

            if (Time.unscaledTime >= _nextHSceneCheckTime)
            {
                _nextHSceneCheckTime = Time.unscaledTime + 1f;
                if (!_inHScene)
                {
                    var proc = FindObjectOfType<HSceneProc>();
                    if (proc != null)
                    {
                        _hSceneProc = proc;
                        _hFlag = proc.flags;
                        _inHScene = true;
                        _simultOrgasmCount = 0;
                        SimpleFileLogger.Info($"HScene detected via fallback (FindObjectOfType). hFlag={(_hFlag != null ? "OK" : "null")}");
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
                        _simultOrgasmCount = 0;
                    }
                }
            }

            if (DebugDumpKeyEnabled && Bindings.Down("AstralMode.dump", "D"))
            {
                DumpAllAstralPositions("D-key");
                MonitorAstralCharaState("D-key", force: true);
            }

            // トグルキーは設定 (Astral Mode Toggle Key) から読む。
            // 以前はここに KeyCode.F を直書きしていたため、設定を何に変えても効かなかった。

            if (IsAstralToggleDown())
            {
                string keySource = AstralToggleKeyName();
                SimpleFileLogger.Info($"Astral toggle pressed ({keySource}). state={_state}, inHScene={_inHScene}");

                if (_state == AstralState.Normal && _inHScene)
                {
                    SimpleFileLogger.Info($"{keySource}: enter astral mode");
                    StartCoroutine(EnterAstralCoroutine());
                }
                else if (_state == AstralState.Astral)
                {
                    SimpleFileLogger.Info($"{keySource}: exit astral mode (normal path)");
                    StartCoroutine(ExitAstralCoroutine());
                }
                else if (_state == AstralState.WhiteHold
                         || _state == AstralState.FadeIn
                         || _state == AstralState.Warping)
                {
                    SimpleFileLogger.Warn($"{keySource}: force exit from intermediate state {_state}");
                    StopAllCoroutines();
                    StartCoroutine(ForceExitCoroutine());
                }
                else
                {
                    SimpleFileLogger.Debug($"{keySource} ignored (already in transition): state={_state}");
                }
            }

            // 残像＋波紋は「白い画面(WhiteHold)」の時点から駆動して、フェードでシーンが出る頃には
            // 既に適用済みにする。Astral まで滞在中ずっと維持。
            // 帰還時(ReturnWarping)も白飛びするまで維持し、白で覆われてから一括解除する。
            if (_state == AstralState.WhiteHold || _state == AstralState.FadeIn
                || _state == AstralState.Astral || _state == AstralState.ReturnWarping)
                DriveAstralSteadyEffects();
            else if (_astralSteadyApplied)
                ClearAstralSteadyEffects();

            // 突入直後の窓の間は、漏れるゲーム音声を毎フレーム強制Stopして確実に潰す。
            if (_astralHardStopUntil > Time.unscaledTime)
                HardStopHSceneVoice();

            // アストラル界 滞在中は voice(喘ぎ/セリフ)を止め、呼吸は残す（設定ON時）。毎フレーム維持。
            MaintainAstralVoiceSuppression();
            MaintainKsPlugTimePassesSuppression();
            MaintainAstralCleanVisualState();
        }

        private void OnBlankRoomLeft()
        {
            if (_state == AstralState.Normal) return;

            SimpleFileLogger.Warn($"blank room left during state={_state} -> force exit");
            StopAllCoroutines();
            StartCoroutine(ForceExitCoroutine());
        }

        private void LateUpdate()
        {
            if (!CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom) return;

            // Main camera is managed by MainGameCameraControl. AstralMode only sends enter/exit offsets.

            // BGM音量をコンフィグとリアルタイム同期
            SyncAstralBgmVolume();
        }

        private void ApplyHarmonyPatchesIndividually()
        {
            var harmony = new Harmony(Guid);
            TryPatch(harmony, typeof(HSceneProc), "Start", nameof(HSceneProc_Start_Postfix), isPostfix: true);
            TryPatch(harmony, typeof(HSceneProc), "OnDestroy", nameof(HSceneProc_OnDestroy_Postfix), isPostfix: true);
            TryPatch(harmony, typeof(HSceneProc), "LateUpdate", nameof(HSceneProc_LateUpdate_Prefix), isPostfix: false);
            TryPatch(harmony, typeof(HVoiceCtrl), "VoiceProc", nameof(HVoiceCtrl_VoiceProc_Prefix), isPostfix: false);
            TryPatch(harmony, typeof(HFlag), nameof(HFlag.AddSonyuSame), nameof(AddSonyuSame_Postfix), isPostfix: true);
            TryPatch(harmony, typeof(HFlag), nameof(HFlag.AddSonyuAnalSame), nameof(AddSonyuAnalSame_Postfix), isPostfix: true);
            // HSprite.Update is intentionally not patched: speed slider / gauges / pose UI must keep native KKS updates.
            PatchPoseChangeTriggers(harmony);
            PatchHInternalTrace(harmony);
            PatchHActionReturnFix(harmony);
            SimpleFileLogger.Info("Harmony patches done");
        }

        private void TryPatch(Harmony harmony, System.Type targetType, string targetMethod, string ourMethod, bool isPostfix)
        {
            try
            {
                var target = AccessTools.Method(targetType, targetMethod);
                if (target == null)
                {
                    SimpleFileLogger.Warn($"Target method not found: {targetType.Name}.{targetMethod}");
                    return;
                }
                var ours = AccessTools.Method(typeof(Plugin), ourMethod);
                if (ours == null)
                {
                    SimpleFileLogger.Warn($"Our patch method not found: {ourMethod}");
                    return;
                }
                if (isPostfix)
                    harmony.Patch(target, postfix: new HarmonyMethod(ours));
                else
                    harmony.Patch(target, prefix: new HarmonyMethod(ours));

                SimpleFileLogger.Info($"Patched: {targetType.Name}.{targetMethod} ({(isPostfix ? "postfix" : "prefix")})");
            }
            catch (System.Exception ex)
            {
                SimpleFileLogger.Warn($"Patch failed for {targetType.Name}.{targetMethod}: {ex.Message}");
            }
        }

        private System.Collections.IEnumerator ForceExitCoroutine()
        {
            DiscardVmdAstral();
            SimpleFileLogger.Info("ForceExit start");
            _state = AstralState.ReturnHold;

            RestoreMainCameraToReal();
            EndExternalAstralRuntime();
            DestroyRealWorldSubCamera();
            ResumeMainGameSubCameraFromAstral("force-exit");
            RestoreAstralSkybox();
            HideAstralHill();          // 霧の解除もここに含まれる。現実に持ち帰らないための必須処理
            HideAstralLight();
            DestroyWhiteRoom();
            RestoreCharasToReal();

            // 汚染防止のため最低限の見た目復元だけは行う。
            // 体位/HFlag の復元は重いのでスキップ（緊急脱出の軽量パス）。
            // 状態クリアは RestoreOriginalCoordinate の finally に集約済み。
            try { RestoreOriginalCoordinate(); }
            catch (System.Exception ex) { SimpleFileLogger.Warn($"ForceExit: RestoreOriginalCoordinate failed: {ex.Message}"); }

            SetAstralCleanVisualsActive(false, "force-exit-before-final-restore");

            try { RestoreSavedCharaVisualsFinal("force-exit"); }
            catch (System.Exception ex) { SimpleFileLogger.Warn($"ForceExit: RestoreSavedCharaVisualsFinal failed: {ex.Message}"); }

            UnfreezeAllCharacterAnimators();
            DestroyWarpEffects();
            CharaFreezeClone.DestroyLeakedCloneRoots();
            StopAstralBgm();
            SetKsPlugTimePassesSuppressed(false, "force-exit");
            SuppressVoiceDuringWarp(false, "force-exit"); // 音声停止を解除

            _state = AstralState.Normal;
            SimpleFileLogger.Info("ForceExit done -> Normal");
            yield break;
        }
    }
}
