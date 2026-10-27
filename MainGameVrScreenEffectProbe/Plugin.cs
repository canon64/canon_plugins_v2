using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using CanonPlugins.Shared;
using UnityEngine;
using UnityEngine.XR;
using VRGIN.Core;

namespace MainGameVrScreenEffectProbe
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.vrscreeneffectprobe";
        public const string PluginName = "MainGameVrScreenEffectProbe";
        public const string Version = "0.1.0";

        public static Plugin Instance { get; private set; }

        private const int WindowId = 0x56524658; // 'VRFX'
        private readonly HashSet<string> _cameraSummaryLogged = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _renderSignatureLogged = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<VrEyeScreenEffectLayer> _attachedLayers = new List<VrEyeScreenEffectLayer>();

        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgUiVisible;
        private ConfigEntry<bool> _cfgOnlyVrActive;
        private ConfigEntry<float> _cfgScanIntervalSec;
        private ConfigEntry<string> _cfgCameraNameFilter;
        private ConfigEntry<bool> _cfgAttachToAllCandidates;
        private ConfigEntry<bool> _cfgAttachToMainFallback;

        private ConfigEntry<bool> _cfgAfterimageEnabled;
        private ConfigEntry<float> _cfgAfterimageAlpha;
        private ConfigEntry<float> _cfgAfterimageFeedback;
        private ConfigEntry<float> _cfgAfterimageTintR;
        private ConfigEntry<float> _cfgAfterimageTintG;
        private ConfigEntry<float> _cfgAfterimageTintB;

        private ConfigEntry<bool> _cfgWaveEnabled;
        private ConfigEntry<float> _cfgWaveAlpha;
        private ConfigEntry<int> _cfgWaveRingCount;
        private ConfigEntry<float> _cfgWaveSpeed;
        private ConfigEntry<int> _cfgWaveSegments;
        private ConfigEntry<float> _cfgWaveCenterX;
        private ConfigEntry<float> _cfgWaveCenterY;

        private ConfigEntry<bool> _cfgLogEnabled;
        private ConfigEntry<ProbeLogLevel> _cfgLogLevel;
        private ConfigEntry<float> _cfgRenderSummaryIntervalSec;

        private ProbeFileLogger _fileLogger;
        private Rect _windowRect = new Rect(40f, 60f, 520f, 360f);
        private Vector2 _scroll;
        private float _nextScanTime;
        private float _nextRenderSummaryTime;
        private bool _lastVrActive;
        private string _lastCameraSummary = string.Empty;
        private int _lastCandidateCount;
        private int _lastAttachedCount;
        private int _renderLeft;
        private int _renderRight;
        private int _renderMono;
        private string _lastRenderInfo = "none";

        private void Awake()
        {
            _onBlankRoomLeft = () => DetachAllLayers("blank room left");
            Instance = this;
            SetupConfig();

            string pluginDir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            string logPath = Path.Combine(pluginDir, PluginName + ".txt");
            _fileLogger = new ProbeFileLogger(logPath, Logger);
            ResetSessionStartState();
            Log(ProbeLogLevel.Info, "=== session start ===");
            Log(ProbeLogLevel.Info, PluginName + " v" + Version + " awake");
            Log(ProbeLogLevel.Info, "pluginDir=" + pluginDir);
            Log(ProbeLogLevel.Info, "configPath=" + Config.ConfigFilePath);
            Log(ProbeLogLevel.Info, "logPath=" + logPath);
            LogVrState("awake");
        }

        private void OnDestroy()
        {
            DetachAllLayers("destroy");
            Log(ProbeLogLevel.Info, "destroyed");
            if (Instance == this)
                Instance = null;
        }

        // BlankMapAdd の追加マップ以外では一切発火させない。
        private bool _gateWasInRoom;
        private Action _onBlankRoomLeft;

        private void Update()
        {
            // 部屋を出たら、カメラに差し込んだ層を全部剥がす。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, _onBlankRoomLeft)) return;

            if (Time.unscaledTime >= _nextScanTime)
            {
                _nextScanTime = Time.unscaledTime + Mathf.Max(0.05f, _cfgScanIntervalSec.Value);
                ScanAndAttach("interval");
            }
        }

        private void OnGUI()
        {
            if (!BlankRoomGate.IsInBlankRoom)
                return;

            if (_cfgUiVisible == null || !_cfgUiVisible.Value)
                return;

            _windowRect.width = Mathf.Clamp(_windowRect.width, 360f, Mathf.Max(360f, Screen.width - 8f));
            _windowRect.height = Mathf.Clamp(_windowRect.height, 240f, Mathf.Max(240f, Screen.height - 8f));
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - _windowRect.width));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - _windowRect.height));
            _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, PluginName);
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            bool enabled = GUILayout.Toggle(_cfgEnabled.Value, "Enable effects");
            if (enabled != _cfgEnabled.Value)
                SetEnabledInternal(enabled, "ui");

            bool logEnabled = GUILayout.Toggle(_cfgLogEnabled.Value, "Log enabled");
            if (logEnabled != _cfgLogEnabled.Value)
            {
                _cfgLogEnabled.Value = logEnabled;
                Config.Save();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Log level", GUILayout.Width(80f));
            ProbeLogLevel nextLevel = (ProbeLogLevel)GUILayout.SelectionGrid(
                Mathf.Clamp((int)_cfgLogLevel.Value - 1, 0, 4),
                new[] { "Error", "Warn", "Info", "Debug", "Trace" },
                5);
            nextLevel = (ProbeLogLevel)((int)nextLevel + 1);
            if (nextLevel != _cfgLogLevel.Value)
            {
                _cfgLogLevel.Value = nextLevel;
                Config.Save();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("VR.Active=" + SafeVrActive()
                + " XR=" + XRSettings.enabled
                + " eyeTex=" + XRSettings.eyeTextureWidth + "x" + XRSettings.eyeTextureHeight);
            GUILayout.Label("Candidates=" + _lastCandidateCount + " attached=" + _lastAttachedCount);
            GUILayout.Label("Renders L/R/M=" + _renderLeft + "/" + _renderRight + "/" + _renderMono);
            GUILayout.Label("Last=" + _lastRenderInfo);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan", GUILayout.Width(100f)))
                ScanAndAttach("ui-rescan");
            if (GUILayout.Button("Reset buffers", GUILayout.Width(120f)))
                ResetAllLayerBuffers("ui-reset");
            if (GUILayout.Button("Close", GUILayout.Width(80f)))
                SetUiVisibleInternal(false, "ui-close");
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(120f));
            GUILayout.Label(_lastCameraSummary);
            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 24f));
            GUILayout.EndVertical();
        }

        private void SetupConfig()
        {
            const string general = "00.General";
            const string camera = "10.Camera Scan";
            const string afterimage = "20.Afterimage Layer";
            const string wave = "30.Wave Layer";
            const string logging = "90.Logging";

            _cfgEnabled = Config.Bind(general, "Enabled", true, "VR左右画面への試験エフェクトを有効化する");
            _cfgUiVisible = Config.Bind(general, "UiVisible", false, "試験UIを表示する");
            _cfgOnlyVrActive = Config.Bind(general, "OnlyVrActive", true, "VR.Active=true の時だけ描画レイヤーを付ける");

            _cfgScanIntervalSec = Config.Bind(camera, "ScanIntervalSec", 1.0f, new ConfigDescription("VRカメラ候補の再スキャン間隔", new AcceptableValueRange<float>(0.05f, 10f)));
            _cfgCameraNameFilter = Config.Bind(camera, "CameraNameFilter", "", "空なら自動。入れるとカメラ名の部分一致だけ候補にする");
            _cfgAttachToAllCandidates = Config.Bind(camera, "AttachToAllCandidates", true, "候補カメラ全てにレイヤーを付ける。falseなら最有力1つのみ");
            _cfgAttachToMainFallback = Config.Bind(camera, "AttachToMainFallback", false, "VRカメラが見つからない時にCamera.mainへ試験レイヤーを付ける");

            _cfgAfterimageEnabled = Config.Bind(afterimage, "Enabled", true, "前フレーム履歴を薄く上乗せする");
            _cfgAfterimageAlpha = Config.Bind(afterimage, "Alpha", 0.22f, new ConfigDescription("残像の合成濃度", new AcceptableValueRange<float>(0f, 1f)));
            _cfgAfterimageFeedback = Config.Bind(afterimage, "Feedback", 0.82f, new ConfigDescription("履歴に戻す濃度。高いほど尾を引く", new AcceptableValueRange<float>(0f, 1f)));
            _cfgAfterimageTintR = Config.Bind(afterimage, "TintR", 0.85f, new ConfigDescription("残像色R", new AcceptableValueRange<float>(0f, 1f)));
            _cfgAfterimageTintG = Config.Bind(afterimage, "TintG", 0.95f, new ConfigDescription("残像色G", new AcceptableValueRange<float>(0f, 1f)));
            _cfgAfterimageTintB = Config.Bind(afterimage, "TintB", 1.0f, new ConfigDescription("残像色B", new AcceptableValueRange<float>(0f, 1f)));

            _cfgWaveEnabled = Config.Bind(wave, "Enabled", true, "透明リングの波レイヤーを上乗せする");
            _cfgWaveAlpha = Config.Bind(wave, "Alpha", 0.22f, new ConfigDescription("波レイヤー濃度", new AcceptableValueRange<float>(0f, 1f)));
            _cfgWaveRingCount = Config.Bind(wave, "RingCount", 4, new ConfigDescription("波リング数", new AcceptableValueRange<int>(1, 16)));
            _cfgWaveSpeed = Config.Bind(wave, "Speed", 0.25f, new ConfigDescription("波の進行速度", new AcceptableValueRange<float>(0f, 5f)));
            _cfgWaveSegments = Config.Bind(wave, "Segments", 96, new ConfigDescription("リング分割数", new AcceptableValueRange<int>(16, 256)));
            _cfgWaveCenterX = Config.Bind(wave, "CenterX", 0.5f, new ConfigDescription("波中心X", new AcceptableValueRange<float>(0f, 1f)));
            _cfgWaveCenterY = Config.Bind(wave, "CenterY", 0.5f, new ConfigDescription("波中心Y", new AcceptableValueRange<float>(0f, 1f)));

            _cfgLogEnabled = Config.Bind(logging, "Enabled", false, "専用txtログ出力を有効化する");
            _cfgLogLevel = Config.Bind(logging, "Level", ProbeLogLevel.Info, "Error/Warning/Info/Debug/Trace の5段階");
            _cfgRenderSummaryIntervalSec = Config.Bind(logging, "RenderSummaryIntervalSec", 1.0f, new ConfigDescription("描画サマリーログ間隔", new AcceptableValueRange<float>(0.1f, 30f)));

            Config.SettingChanged += (_, __) =>
            {
                Log(ProbeLogLevel.Debug, "config changed");
                ScanAndAttach("config");
            };
        }

        private void ResetSessionStartState()
        {
            bool changed = false;
            if (_cfgUiVisible != null && _cfgUiVisible.Value)
            {
                _cfgUiVisible.Value = false;
                changed = true;
            }
            _windowRect = new Rect(40f, 60f, 520f, 360f);
            _scroll = Vector2.zero;
            _nextScanTime = 0f;
            _nextRenderSummaryTime = 0f;
            _lastVrActive = false;
            _lastCameraSummary = string.Empty;
            _lastCandidateCount = 0;
            _lastAttachedCount = 0;
            _renderLeft = 0;
            _renderRight = 0;
            _renderMono = 0;
            _lastRenderInfo = "none";
            _cameraSummaryLogged.Clear();
            _renderSignatureLogged.Clear();
            if (changed)
                Config.Save();
            Log(ProbeLogLevel.Info, "session runtime reset enabled=" + RuntimeEnabled + " uiVisible=false buffers=empty");
        }

        public static bool TryGetEnabled(out bool enabled)
        {
            enabled = Instance != null && Instance._cfgEnabled != null && Instance._cfgEnabled.Value;
            return Instance != null;
        }

        public static bool TrySetEnabled(bool enabled)
        {
            return Instance != null && Instance.SetEnabledInternal(enabled, "api");
        }

        public static bool TryGetUiVisible(out bool visible)
        {
            visible = Instance != null && Instance._cfgUiVisible != null && Instance._cfgUiVisible.Value;
            return Instance != null;
        }

        public static bool TrySetUiVisible(bool visible)
        {
            return Instance != null && Instance.SetUiVisibleInternal(visible, "api");
        }

        internal bool RuntimeEnabled => _cfgEnabled != null && _cfgEnabled.Value;
        internal bool AfterimageEnabled => _cfgAfterimageEnabled != null && _cfgAfterimageEnabled.Value;
        internal bool WaveEnabled => _cfgWaveEnabled != null && _cfgWaveEnabled.Value;
        internal float AfterimageAlpha => _cfgAfterimageAlpha != null ? Mathf.Clamp01(_cfgAfterimageAlpha.Value) : 0.2f;
        internal float AfterimageFeedback => _cfgAfterimageFeedback != null ? Mathf.Clamp01(_cfgAfterimageFeedback.Value) : 0.82f;
        internal Color AfterimageTint => new Color(
            _cfgAfterimageTintR != null ? Mathf.Clamp01(_cfgAfterimageTintR.Value) : 1f,
            _cfgAfterimageTintG != null ? Mathf.Clamp01(_cfgAfterimageTintG.Value) : 1f,
            _cfgAfterimageTintB != null ? Mathf.Clamp01(_cfgAfterimageTintB.Value) : 1f,
            AfterimageAlpha);
        internal float WaveAlpha => _cfgWaveAlpha != null ? Mathf.Clamp01(_cfgWaveAlpha.Value) : 0.2f;
        internal int WaveRingCount => _cfgWaveRingCount != null ? Mathf.Clamp(_cfgWaveRingCount.Value, 1, 16) : 4;
        internal float WaveSpeed => _cfgWaveSpeed != null ? Mathf.Max(0f, _cfgWaveSpeed.Value) : 0.25f;
        internal int WaveSegments => _cfgWaveSegments != null ? Mathf.Clamp(_cfgWaveSegments.Value, 16, 256) : 96;
        internal Vector2 WaveCenter => new Vector2(
            _cfgWaveCenterX != null ? Mathf.Clamp01(_cfgWaveCenterX.Value) : 0.5f,
            _cfgWaveCenterY != null ? Mathf.Clamp01(_cfgWaveCenterY.Value) : 0.5f);

        internal void Log(ProbeLogLevel level, string message)
        {
            _fileLogger?.Write(
                _cfgLogEnabled == null || _cfgLogEnabled.Value,
                _cfgLogLevel != null ? _cfgLogLevel.Value : ProbeLogLevel.Info,
                level,
                message);
        }

        private bool SetEnabledInternal(bool enabled, string source)
        {
            if (_cfgEnabled == null)
                return false;
            if (_cfgEnabled.Value != enabled)
                _cfgEnabled.Value = enabled;
            Config.Save();
            Log(ProbeLogLevel.Info, "enabled set source=" + source + " value=" + enabled);
            if (!enabled)
                DetachAllLayers("disabled");
            else
                ScanAndAttach("enabled");
            return true;
        }

        private bool SetUiVisibleInternal(bool visible, string source)
        {
            if (_cfgUiVisible == null)
                return false;
            if (_cfgUiVisible.Value != visible)
                _cfgUiVisible.Value = visible;
            Config.Save();
            Log(ProbeLogLevel.Info, "ui visible set source=" + source + " value=" + visible);
            return true;
        }

        private bool SafeVrActive()
        {
            try { return VR.Active; }
            catch { return false; }
        }

        private void LogVrState(string phase)
        {
            bool vrActive = SafeVrActive();
            Log(ProbeLogLevel.Info, "vr state phase=" + phase
                + " active=" + vrActive
                + " mode=" + (VR.Mode != null)
                + " camera=" + (VR.Camera != null)
                + " steamCam=" + (VR.Camera != null && VR.Camera.SteamCam != null)
                + " xrEnabled=" + XRSettings.enabled
                + " eyeTexture=" + XRSettings.eyeTextureWidth + "x" + XRSettings.eyeTextureHeight);
        }
    }
}
