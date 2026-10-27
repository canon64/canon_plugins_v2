using System;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using MainGameLogRelay;
using MainGameUnifiedUi;
using UnityEngine;

namespace MainGameVMDPlay
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInDependency("com.kks.maingame.unifiedui")]
    [BepInDependency("com.kks.maingame.voicefaceeventbridge", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("KKABMX.Core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("orange.spork.advikplugin", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("canon.maingame.hscene.poseinjector", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInDependency("KKS_VMDPlayPlugin",       BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("KKS_GirlsClothesChange", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(MainGameLogRelay.Plugin.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid       = "com.kks.maingame.vmdplay";
        public const string PluginName = "MainGameVMDPlay";
        public const string Version    = "0.3.0";

        internal static Plugin Instance { get; private set; }

        private string          _pluginDir;
        private string          _settingsPath;
        private VMDPlaySettings _settings;
        private bool            _available = false;

        // BepInEx Config
        private ConfigEntry<bool>             _cfgEnabled;
        private ConfigEntry<bool>             _cfgLogEnabled;

        // HScene 検出
        private HSceneProc _hSceneProc   = null;
        private bool       _insideHScene = false;
        private float      _nextScanTime = 0f;

        // ── 初期化 ──────────────────────────────────────────────────────────────

        private void Awake()
        {
            Instance     = this;
            _pluginDir   = Path.GetDirectoryName(Info.Location);
            _settingsPath = Path.Combine(_pluginDir, "MainGameVMDPlaySettings.json");
            InitDiagnostics();

            _settings = SettingsStore.Load(_settingsPath);

            BindConfig();
            InitBodyCorrectionSettings();
            InitClothingShortcuts();
            InitLogRelay();

            LogInfo($"=== {PluginName} started ver={Version} logRelay={LogRelayApi.IsAvailable} ===");
            LogInfo($"[Boot] Awake pluginDir={_pluginDir}");
            LogInfo($"[Settings] loaded path={_settingsPath} vmdDir={_settings.VmdDirectory} autoLoadLast={_settings.AutoLoadLast}");
            LogInfo($"[Config] bound Enabled={_cfgEnabled.Value} LogEnabled={_cfgLogEnabled.Value}");

            EnsureVmdDirectory();
            FilesRescan();

            if (!VmdBridgeTryResolveTypes())
            {
                LogError("[Boot] VmdBridge resolve failed — plugin unavailable");
                return;
            }

            // VMDPlayPlugin の Start() は本編では実行されないので自前で初期化する
            InitVmdPlugin();

            InitCameraMotion();
            InitPoseTransitions();
            InitIndependentActor();
            InitGameAudio();
            InitUI();
            InitDanceTrigger();
            _available = true;
            LogInfo("[Boot] init complete available=True");
        }

        private void InitVmdPlugin()
        {
            // 1. Logger.Init
            VmdBridgeInitLogger(Logger);

            // 2. Settings.Init（internal static → NonPublic Reflection）
            string embeddedCfgPath = Path.Combine(_pluginDir, "VMDPlayPlugin_embedded.cfg");
            var embeddedConfig = new BepInEx.Configuration.ConfigFile(embeddedCfgPath, saveOnInit: true);
            LogInfo($"[Bridge] embedded ConfigFile created path={embeddedCfgPath}");
            VmdBridgeInitSettings(embeddedConfig, 1f);

            // 3. Settings.BaseDir = KKS_VMDPlayPlugin.dll のあるディレクトリ
            string vmdDir = _vmdPluginDir;
            if (string.IsNullOrEmpty(vmdDir) && Chainloader.PluginInfos.TryGetValue("KKS_VMDPlayPlugin", out var vmdInfo))
            {
                vmdDir = Path.GetDirectoryName(vmdInfo.Location);
            }
            if (!string.IsNullOrEmpty(vmdDir))
                VmdBridgeSetSettingsBaseDir(vmdDir);
            else
                LogWarn("[Bridge] Settings.BaseDir skipped because KKS_VMDPlayPlugin dir is unknown");

            // 4. VMDPlayPlugin 設定を本プラグインの設定に同期
            VmdBridgeSetSetting("UseIKDataInVMD",   _settings.UseIKDataInVMD);
            VmdBridgeSetSetting("SmoothArmTwist",   _settings.SmoothArmTwist);
            VmdBridgeSetSetting("UseBoneTransform",  _settings.UseBoneTransform);

            // 5. VMDAnimationMgr.Install
            var container = new GameObject("MainGameVMDPlay_Container");
            UnityEngine.Object.DontDestroyOnLoad(container);
            VmdBridgeInstallMgr(container);
        }

        private void BindConfig()
        {
            _cfgEnabled = Config.Bind("General", "Enabled", true, "プラグイン 有効/無効");

            _cfgLogEnabled = Config.Bind("General", "LogEnabled", false, "ログ出力 有効/無効");
            _cfgLogEnabled.SettingChanged += (_, __) =>
            {
                if (LogRelayApi.IsAvailable)
                    LogRelayApi.SetOwnerEnabled(RelayOwner, _cfgLogEnabled.Value);
            };


        }

        // ── 毎フレーム ──────────────────────────────────────────────────────────

        private void Update()
        {
            EnsureClothingShortcuts();
            TickNeckDiagnostics();
            TickActorGizmo();
            if (_astral != null) { ScanHScene(); if (!_cfgEnabled.Value) { AbortAstral(); DisposeIndependentActor(); } return; }
            TickIndependentActor();
            if (!_available) return;
            if (!_cfgEnabled.Value) { if (_vmdOwnsCharacter || _loadRoutine != null) ReleaseCharacter(); return; }
            TickPoseIntegrity();
            TickAdjustmentSave();
            TickModelReload();
            TickFilePicker();
            TickDiagnostics();
            if (!_available)        return;
            if (!_cfgEnabled.Value) { if (_vmdOwnsCharacter || _loadRoutine != null) ReleaseCharacter(); return; }

            ScanHScene();
            if (!_insideHScene) return;

            TickVideoPlayback();
            TickPlayback();
        }

        // ── HScene 検出 ─────────────────────────────────────────────────────────

        private HSceneProc _observedHScene;
        private void ScanHScene()
        {
            float now = Time.unscaledTime;
            if (now < _nextScanTime) return;
            _nextScanTime = now + 1f;

            if (_hSceneProc == null)
                _hSceneProc = FindObjectOfType<HSceneProc>();

            bool inside = _hSceneProc != null;
            if (inside && _insideHScene && !ReferenceEquals(_observedHScene, _hSceneProc)) {
                var nextScene = _hSceneProc;
                OnHSceneEnd();
                _hSceneProc = nextScene; _insideHScene = true;
                OnHSceneStart(); return;
            }
            if (inside == _insideHScene) return;

            _insideHScene = inside;
            if (_insideHScene) OnHSceneStart();
            else               OnHSceneEnd();
        }

        private void OnHSceneStart()
        {
            _observedHScene = _hSceneProc; _target.SceneGeneration++;
            LogInfo($"[HScene] entered hSceneProc={_hSceneProc.GetInstanceID()}");
            SetState(PluginState.HSceneEntered);
            StartCoroutine(WaitForCharaReady());
        }

        private void OnHSceneEnd()
        {
            AbortAstral();
            LogInfo("[HScene] left");
            DisposeIndependentActor();
            PlaybackUnloadAll();
            _hActor = null; _target.Actor = null; _target.Independent = false; _target.Generation++;
            _hSceneProc = null; _observedHScene = null; _target.SceneGeneration++;
        }

        // ── 設定保存 ────────────────────────────────────────────────────────────

        private void SaveSettings(string reason = "")
        {
            InvalidateBarCatalog();
            for (int i = 0; i < 10; i++) _settings.Adjustments.Remove("body." + i);
            bool ok = SettingsStore.Save(_settingsPath, _settings);
            LogDebug($"[Settings] saved reason={reason} path={_settingsPath} ok={ok}");
        }

        // ── 終了処理 ────────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            FinishNeckDiagnostics();
            AbortAstral();
            DisposeIndependentActor();
            _actorHarmony?.UnpatchSelf();
            DisposeDanceTrigger();
            _clothingHarmony?.UnpatchSelf();
            UnregisterMusicReverb();
            UnifiedUiApi.UnregisterTab(UnifiedTabId);
            Application.logMessageReceived -= DiagnosticUnityLog;
            if (_insideHScene) PlaybackUnloadAll();
            VmdBridgeSoundClearClip();
            ReleaseCameraMotion();
            new HarmonyLib.Harmony(Guid + ".camera").UnpatchSelf();
            _poseHarmony?.UnpatchSelf();
            RestoreGameAudio();
            _gameAudioHarmony?.UnpatchSelf();
            SaveSettings("destroy");
            LogInfo("[Boot] OnDestroy");
        }

        private void OnApplicationQuit()
        {
            SaveSettings("quit");
        }
    }
}
