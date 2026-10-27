using Bindings = CanonPlugins.Shared.PluginInput;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CanonPlugins.Shared;
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGamePortraitCapture
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        internal const string GUID = "com.kks.maingame.portraitcapture";
        internal const string PluginName = "MainGamePortraitCapture";
        internal const string Version = "0.1.0";

        internal static new ManualLogSource Logger { get; private set; }
        internal static string PluginDir { get; private set; }

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object FileLogLock = new object();
        private string _settingsPath;
        private string _logPath;
        private PluginSettings _settings;
        private bool _captureBusy;
        private bool _cameraOverrideActive;
        private Vector3 _overridePosition;
        private Quaternion _overrideRotation;
        private float _overrideFov;
        private Camera _overrideCamera;
        private Transform _captureLookTarget;
        private Transform _captureNeckLookTarget;
        private EyeLookController _captureEyeLookController;
        private NeckLookControllerVer2 _captureNeckLookController;
        private Transform _captureTiltHead;
        private Vector3 _captureTiltBodyUp;
        private float _captureHeadTiltDegrees;
        private bool _captureHeadRollOverrideActive;
        private static Plugin _instance;

        private void Awake()
        {
            _instance = this;
            Logger = base.Logger;
            PluginDir = Path.GetDirectoryName(Info.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            _settingsPath = Path.Combine(PluginDir, "MainGamePortraitCaptureSettings.json");
            _logPath = Path.Combine(PluginDir, "MainGamePortraitCapture.log");
            ResetFileLog();
            _settings = SettingsStore.LoadOrCreate(_settingsPath);
            SetupConfig();
            Camera.onPreCull += OnCameraPreCull;
            LogInfo(PluginName + " " + Version + " initialized; hotkey=" + _cfgCaptureKey.Value);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            Camera.onPreCull -= OnCameraPreCull;
            _cameraOverrideActive = false;
        }

        private void Update()
        {
            if (_captureBusy)
            {
                TraceBoneDiagnostic("Update-enter", false);
                MaintainCaptureLookOverride();
            }
            if (!_cfgEnabled.Value || !_cfgDevelopmentHotkeyEnabled.Value || _captureBusy) return;
            if (TextInputGate.IsTextInputActive) return;
            if (Bindings.Down("MainGamePortraitCapture.capture", _cfgCaptureKey.Value.ToString())) StartCoroutine(CaptureSequence());
        }

        private void LateUpdate()
        {
            if (!_captureBusy && !_cameraOverrideActive) return;
            TraceBoneDiagnostic("LateUpdate-enter", false);
            ApplyCameraOverride();
            ApplyCaptureHeadTiltForRender();
            TraceBoneDiagnostic("LateUpdate-exit", false);
        }

        private void OnCameraPreCull(Camera camera)
        {
            if (!_cameraOverrideActive && !_captureBusy) return;
            TraceBoneDiagnostic("CameraPreCull-enter", false);
            if (_cameraOverrideActive && ReferenceEquals(camera, _overrideCamera))
            {
                ApplyCameraOverride();
                ApplyCaptureEyeLookForRender();
            }
            TraceBoneDiagnostic("CameraPreCull-exit", false);
        }

        private void ApplyCameraOverride()
        {
            if (!_cameraOverrideActive || _overrideCamera == null) return;
            _overrideCamera.transform.position = _overridePosition;
            _overrideCamera.transform.rotation = _overrideRotation;
            _overrideCamera.fieldOfView = _overrideFov;
        }

        public static bool TryCapture(out string reason)
        {
            Plugin instance = _instance;
            if (instance == null) { reason = "MainGamePortraitCapture is not loaded"; return false; }
            if (!instance._cfgEnabled.Value) { reason = "disabled"; return false; }
            if (instance._captureBusy) { reason = "capture is already running"; return false; }
            instance._captureBusy = true;
            instance.StartCoroutine(instance.CaptureSequence());
            reason = "accepted";
            return true;
        }

        public static bool TryCaptureMany(int count, out string reason)
        {
            Plugin instance = _instance;
            if (instance == null) { reason = "MainGamePortraitCapture is not loaded"; return false; }
            if (!instance._cfgEnabled.Value) { reason = "disabled"; return false; }
            if (instance._captureBusy) { reason = "capture is already running"; return false; }
            int requested = Mathf.Clamp(count, 1, 10);
            instance._captureBusy = true;
            instance.StartCoroutine(instance.CaptureManySequence(requested));
            reason = "accepted count=" + requested;
            return true;
        }

        internal static void LogWarning(string message)
        {
            Logger?.LogWarning(message);
        }

        private void LogInfo(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogInfo(message);
            WriteFileLog("INFO", message);
        }

        private void LogDebug(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (_cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value) return;
            Logger?.LogDebug(message);
            WriteFileLog("DEBUG", message);
        }

        private void LogError(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogError(message);
            WriteFileLog("ERROR", message);
        }

        private void ResetFileLog()
        {
            if (!_runtimeLoggingEnabled) return;
            try
            {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllText(_logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [INFO] === session start ===" + Environment.NewLine, Utf8NoBom);
            }
            catch { }
        }

        private void WriteFileLog(string level, string message)
        {
            try
            {
                lock (FileLogLock)
                {
                    File.AppendAllText(_logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + level + "] " + message + Environment.NewLine, Utf8NoBom);
                }
            }
            catch { }
        }
    }
}
