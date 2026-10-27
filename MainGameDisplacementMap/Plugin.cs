using System.IO;
using BepInEx;
using CanonPlugins.Shared;
using UnityEngine;

namespace MainGameDisplacementMap
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.displacementmap";
        public const string PluginName = "MainGameDisplacementMap";
        public const string Version = "0.1.0";

        public static Plugin Instance { get; private set; }

        // External control (AstralMode etc.) - Phase 3 で本格実装
        internal bool _externalActive;
        internal float _externalUntil;
        internal bool _externalFullScreen;
        internal float _externalIntensity;
        internal float _externalOpacity = 1f;
        internal float _externalStrength = -1f;

        // Throttle timestamps for logging (to prevent log explosion)
        private static float _lastLateUpdateLogTime = 0f;
        private static float _lastCaptureLogTime = 0f;
        private static float _lastDiagnosticLogTime = 0f;
        private static float _lastDiagnosticRTLogTime = 0f;
        private static float _lastAllCamerasLogTime = 0f;
        private static float _lastOnPostRenderLogTime = 0f;
        private static float _lastDrawSkipLogTime = 0f;
        private static float _lastSelectedCameraLogTime = 0f;
        private static float _lastDiagnosticGUILogTime = 0f;
        private static float _lastCaptureDetailLogTime = 0f;
        private static float _lastDiagnosticCameraListLogTime = 0f;
        private static float _lastOverlayDrawCameraLogTime = 0f;
        private static float _lastVisibilityTestLogTime = 0f;

        private void Awake()
        {
            Instance = this;

            string dir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            _jsonPath = Path.Combine(dir, PluginName + ".json");
            _logPath = Path.Combine(dir, PluginName + ".log");

            BindConfig();
            LoadJson();
            SaveJson();

            _fileLog = new SimpleFileLogger { Enabled = _cfgVerboseLog.Value };
            _fileLog.Initialize(_logPath);

            Logger.LogInfo($"{PluginName} {Version} loaded.");
            _fileLog.Write("INFO", $"{PluginName} {Version} loaded");
        }

        // BlankMapAdd の追加マップ以外では一切発火させない。
        private bool _gateWasInRoom;

        private void LateUpdate()
        {
            // 部屋を出たら「無効化」と同じ停止経路を通す。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, StopRuntime)) return;

            if (!_cfgEnabled.Value)
            {
                StopRuntime();
                return;
            }

            Camera captureSrc = ResolveCamera();
            Camera overlayTarget = ResolveOverlayDrawCamera();
            if (overlayTarget == null) overlayTarget = captureSrc; // フォールバック（通常は別カメラが選ばれるはず）

            SyncOverlayDrawer(overlayTarget);

            if (captureSrc == null) return;

            EnsureCaptureResources(captureSrc);
            EnsureMaterial();
            SyncSourceFrameCapture(captureSrc, overlayTarget);

            // Throttled logging - only once per second to avoid log explosion
            if (_fileLog.Enabled && Time.unscaledTime - _lastLateUpdateLogTime > 1f)
            {
                _fileLog.Write("INFO", $"LateUpdate running: capture={captureSrc.name}");
                _lastLateUpdateLogTime = Time.unscaledTime;
            }

            _captureSource = captureSrc;
            if (_captureCoroutine == null)
                _captureCoroutine = StartCoroutine(CaptureEndOfFrame());
        }

        private void OnDestroy()
        {
            SaveJson();
            StopRuntime();
            if (Instance == this) Instance = null;
            _fileLog?.Write("INFO", "OnDestroy");
        }

        private bool IsExternalActiveNow()
        {
            if (!_externalActive) return false;
            if (Time.unscaledTime <= _externalUntil && _externalIntensity > 0.0001f) return true;
            _externalActive = false;
            _externalIntensity = 0f;
            return false;
        }

        // === 外部API (AstralMode 等から反射呼び出し想定。CircularDisplacement と同シグネチャ) ===

        public static bool IsAvailable => Instance != null;

        public static void SetExternalWarp(float intensity, string mode = "MapRipple")
        {
            SetExternalWarp(intensity, mode, 1f, -1f, -1f, -1f, 0.5f, 0.5f, 0.4f);
        }

        public static void SetExternalWarp(
            float intensity,
            string mode,
            float opacity,
            float strength,
            float radius,
            float innerRadius,
            float centerX,
            float centerY,
            float holdSeconds = 0.4f)
        {
            SetExternalWarp(intensity, mode, opacity, strength, radius, innerRadius, centerX, centerY, holdSeconds, false);
        }

        public static void SetExternalWarp(
            float intensity,
            string mode,
            float opacity,
            float strength,
            float radius,
            float innerRadius,
            float centerX,
            float centerY,
            float holdSeconds,
            bool fullScreen)
        {
            if (Instance == null) return;
            Instance.ApplyExternal(intensity, opacity, strength, radius, innerRadius, centerX, centerY, holdSeconds, fullScreen);
        }

        public static void ClearExternalWarp()
        {
            if (Instance == null) return;
            Instance._externalActive = false;
            Instance._externalIntensity = 0f;
            Instance._externalFullScreen = false;
        }

        private void ApplyExternal(float intensity, float opacity, float strength, float radius, float inner, float cx, float cy, float hold, bool full)
        {
            intensity = Mathf.Clamp01(intensity);
            if (intensity <= 0.0001f)
            {
                _externalActive = false;
                _externalIntensity = 0f;
                _externalFullScreen = false;
                return;
            }

            _externalActive = true;
            _externalUntil = Time.unscaledTime + Mathf.Max(0.03f, hold);
            _externalFullScreen = full;
            _externalIntensity = intensity;
            _externalOpacity = Mathf.Clamp01(opacity);
            _externalStrength = strength;

            // 外部から来たパラメータを一時的に cfg へ反映 (簡易)
            if (strength > 0.0001f) _cfgStrength.Value = strength;
            if (radius > 0.0001f) _cfgRadius.Value = radius;
            if (inner >= 0f) _cfgInner.Value = inner;
            if (cx >= 0f) _cfgCenterX.Value = Mathf.Clamp01(cx);
            if (cy >= 0f) _cfgCenterY.Value = Mathf.Clamp01(cy);
            if (opacity > 0f) _cfgOpacity.Value = Mathf.Clamp01(opacity);
        }

        // OnGUI診断プレビューはOFF
        // private void OnGUI() { ... } は削除済み相当（必要ならconfigで復活）
    }
}
