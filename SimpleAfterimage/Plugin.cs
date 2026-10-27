using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.XR;
using VRGIN.Core;
using CanonPlugins.Shared;

namespace SimpleAfterimage
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInDependency("com.kks.maingame.unifiedui", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.kks.maingame.rhythmsync", BepInDependency.DependencyFlags.HardDependency)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.simpleafterimage";
        public const string PluginName = "SimpleAfterimage";
        public const string Version = "0.2.0";

        public static Plugin Instance { get; private set; }

        // Config
        private ConfigEntry<bool>   _cfgEnabled;
        private ConfigEntry<bool>   _cfgVerboseLog;
        private ConfigEntry<int>    _cfgFadeFrames;
        private ConfigEntry<int>    _cfgMaxSlots;
        private ConfigEntry<int>    _cfgCaptureInterval;
        private ConfigEntry<bool>   _cfgUseScreenSize;
        private ConfigEntry<int>    _cfgCaptureWidth;
        private ConfigEntry<int>    _cfgCaptureHeight;
        private ConfigEntry<string> _cfgCharaLayer;
        private ConfigEntry<string> _cfgFullSceneExcludeLayer;
        private ConfigEntry<float>  _cfgTintR;
        private ConfigEntry<float>  _cfgTintG;
        private ConfigEntry<float>  _cfgTintB;
        private ConfigEntry<float>  _cfgTintA;
        private ConfigEntry<float>  _cfgAlphaScale;
        private ConfigEntry<string> _cfgFadeCurve;
        private ConfigEntry<bool>   _cfgFrontOfCharacter;
        private ConfigEntry<bool>   _cfgPreferCameraMain;
        private ConfigEntry<string> _cfgCameraNameFilter;
        private ConfigEntry<int>    _cfgCameraFallbackIndex;
        private ConfigEntry<string> _cfgPresetName;
        private ConfigEntry<string> _cfgPresetAction;
        private ConfigEntry<bool>   _cfgBeatSyncFadeEnabled;
        private ConfigEntry<int>    _cfgBeatSyncFadeMin;
        private ConfigEntry<int>    _cfgBeatSyncFadeMax;
        private ConfigEntry<float>  _cfgGlowThreshold;
        private ConfigEntry<float>  _cfgGlowStrength;
        private ConfigEntry<float>  _cfgGlowBlurPercent;

        // Runtime
        private Camera _captureCamera;
        private Coroutine _captureLoop;
        private Camera _pendingCaptureSource;
        private readonly WaitForEndOfFrame _captureEndOfFrameWait = new WaitForEndOfFrame();
        private Camera _lastSourceCamera;
        private OverlayDrawer _overlayDrawer;
        private GameObject _cameraRoot;
        private RenderTexture[] _slots;
        private int[] _life;
        private int[] _slotFadeFrames;
        private float[] _slotAlphaScale;
        private bool[] _slotExternal;
        private int _writeIndex;
        private int _frameCounter;
        private int _runtimeBeatFadeFrames;
        private int _characterMask;
        private int _rtWidth;
        private int _rtHeight;

        private RenderTexture[] _drawSlots;
        private float[] _drawAlpha;
        private int _drawCount;
        private bool _afterimageRuntimeReady;
        private bool _externalGlowPaused;
        private PostProcessLayer _capturePostProcessLayer;
        private PostProcessVolume _capturePostProcessVolume;
        private PostProcessProfile _capturePostProcessProfile;
        private Bloom _captureBloom;
        private GameObject _captureGlowVolumeRoot;
        private bool _captureGlowInitFailed;
        private bool _captureGlowTemplateWarned;

        // HScene速さ連携
        private HSceneProc _hSceneProc;
        private float _nextHSceneScanTime;

        // プリセット
        private string _presetsPath;
        private string _configJsonPath;
        private string _pluginLogPath;
        private string _lastGlowDecision;
        private float _nextGlowHeartbeatTime;
        private Dictionary<string, PresetData> _presets = new Dictionary<string, PresetData>();
        private string _pendingAction;

        private void Awake()
        {
            Instance = this;
            string pluginDir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            _presetsPath = Path.Combine(pluginDir, "presets.json");
            _configJsonPath = Path.Combine(pluginDir, "config.json");
            _pluginLogPath = Path.Combine(pluginDir, "SimpleAfterimage.log");
            LogPlugin("INFO", "=== Awake start ===");
            LoadPresetsFile();
            SetupConfig();
            LoadConfigJson();
            LoadVmdSettings();
            RegisterAfterimageTab();
            ApplyConfig();
            SaveConfigJson();
            Logger.LogInfo($"{PluginName} {Version} loaded.");
            LogPlugin("INFO", $"{PluginName} {Version} loaded");
            LogPlugin("INFO", $"config: threshold={_cfgGlowThreshold.Value:0.##}, strength={_cfgGlowStrength.Value:0.##}, blur%={_cfgGlowBlurPercent.Value:0.##}");
        }

        private void Update()
        {
            RegisterAfterimageTab();
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (_pendingAction == null) return;
            string action = _pendingAction;
            _pendingAction = null;

            string name = _cfgPresetName.Value?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                Logger.LogWarning("プリセット名が空です");
                _cfgPresetAction.Value = "なし";
                return;
            }

            if (action == "保存")
            {
                SavePreset(name);
                Logger.LogInfo($"プリセット保存: {name}");
            }
            else if (action == "読込")
            {
                if (!LoadPreset(name))
                    Logger.LogWarning($"プリセットが見つかりません: {name}");
                else
                    Logger.LogInfo($"プリセット読込: {name}");
            }

            _cfgPresetAction.Value = "なし";
        }

        private bool _gateWasInRoom;

        private void LateUpdate()
        {
            _runtimeBeatFadeFrames = 0;
            RefreshVmdAfterimage();
            // 退室した時点で残像バッファとキャプチャ資源を解放する。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, StopAfterimageRuntime)) return;
            if (!IsEffectivelyEnabled() || !IsHSceneActive())
            {
                StopAfterimageRuntime();
                return;
            }

            if (PauseForExternalGlow())
                return;

            Camera src = ResolveCamera();
            bool vrStereoRenderImagePath = ShouldUseVrStereoRenderImage(src);

            if (!_afterimageRuntimeReady || (!vrStereoRenderImagePath && _slots == null))
                ApplyConfig();

            if (!vrStereoRenderImagePath && _slots == null)
                return;

            ConsumeExternalClearRequest();

            // 画面サイズ変化検知
            if (_cfgUseScreenSize.Value && !vrStereoRenderImagePath)
            {
                bool vrActive = VR.Active && XRSettings.eyeTextureWidth > 0;
                int curW = vrActive ? XRSettings.eyeTextureWidth  : Screen.width;
                int curH = vrActive ? XRSettings.eyeTextureHeight : Screen.height;
                if (curW != _rtWidth || curH != _rtHeight)
                    ApplyConfig();
            }

            // HScene速さ同期
            if (_cfgBeatSyncFadeEnabled.Value && !IsExternalAfterimageActiveNow())
            {
                float intensity = GetHSceneSpeedIntensity();
                if (intensity >= 0f)
                {
                    int fade = Mathf.RoundToInt(Mathf.Lerp(_cfgBeatSyncFadeMin.Value, _cfgBeatSyncFadeMax.Value, intensity));
                    _runtimeBeatFadeFrames = Mathf.Clamp(fade, 1, 300);
                }
            }

            SyncOverlayDrawer(src);

            if (vrStereoRenderImagePath)
            {
                _drawCount = 0;
                return;
            }

            _frameCounter++;
            int interval = Mathf.Max(1, GetEffectiveCaptureInterval());
            bool shouldCapture = src != null && CaptureDue(ref _nextVmdCapture, _frameCounter, interval);
            if (shouldCapture)
                ScheduleCapture(src);

            AgeThenBuildDrawList();
        }

        private void OnDestroy()
        {
            MainGameUnifiedUi.UnifiedUiApi.UnregisterTab("simple-afterimage");
            LogPlugin("INFO", "OnDestroy start");
            StopCaptureLoop();
            if (_overlayDrawer != null) Destroy(_overlayDrawer);
            ReleaseCaptureCameraRuntime();
            ReleaseSlots();
            ReleaseVrStereoSlots();
            if (Instance == this) Instance = null;
            LogPlugin("INFO", "OnDestroy end");
        }
    }
}
