using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SaveData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameDollMode
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency("KSOX", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string GUID = "canon.maingame.dollmode";
        public const string PluginName = "MainGameDollMode";
        public const string Version = "1.0.0";

        internal static Plugin Instance;
        internal static new ManualLogSource Logger;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _logFileLock = new object();
        private readonly Dictionary<int, bool> _originalHideHighlightByCha = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> _originalEyesBlinkByCha = new Dictionary<int, bool>();
        private readonly Dictionary<int, EyeLookState> _originalEyeLookByCha = new Dictionary<int, EyeLookState>();
        private readonly Dictionary<int, EyeRotationState> _originalEyeRotationByCha = new Dictionary<int, EyeRotationState>();
        private readonly Dictionary<int, EyeTextureOffsetState> _originalEyeTextureOffsetByCha = new Dictionary<int, EyeTextureOffsetState>();
        private readonly Dictionary<int, FaceExpressionState> _originalFaceExpressionByCha = new Dictionary<int, FaceExpressionState>();
        private readonly Dictionary<int, EyeOverlayState> _originalEyeOverlayByCha = new Dictionary<int, EyeOverlayState>();
        private readonly Dictionary<int, bool> _originalFixationalEnabledByComponent = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> _originalAhegaoEnabledByComponent = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> _originalAhegaoActiveByGameObject = new Dictionary<int, bool>();
        private readonly Dictionary<string, float> _blockLogCooldownByPoint = new Dictionary<string, float>();
        private readonly Dictionary<int, EyeTraceSnapshot> _eyeTraceLastByCha = new Dictionary<int, EyeTraceSnapshot>();
        private readonly List<EyeTraceSignal> _eyeTraceSignals = new List<EyeTraceSignal>();
        private readonly Dictionary<string, float> _eyeTraceLogCooldownByKey = new Dictionary<string, float>();
        private readonly List<MonoBehaviour> _cachedFixationalBehaviours = new List<MonoBehaviour>();
        private readonly HashSet<int> _cachedFixationalIds = new HashSet<int>();
        private readonly List<MonoBehaviour> _cachedAhegaoBehaviours = new List<MonoBehaviour>();
        private readonly HashSet<int> _cachedAhegaoIds = new HashSet<int>();
        private ChaControl[] _cachedActiveCharacters;
        private float _nextActiveCharacterScanAt;

        private string _pluginDir = string.Empty;
        private string _logFilePath = string.Empty;
        private PluginSettings _settings;
        private Harmony _harmony;
        private bool _dollModeApplied;
        private bool _hasPendingSource;
        private string _pendingSource = "startup";
        private bool _isConfigSyncing;
        private int _internalFaceWriteDepth;
        private int _internalEyeLookWriteDepth;
        private Coroutine _eyeFreezeEndOfFrameCoroutine;
        private float _nextFixationalRescanAt;
        private int _lastFixationalRescanCount = -1;
        private bool _lastFixationalFallbackUsed;
        private float _nextAhegaoRescanAt;
        private int _lastAhegaoRescanCount = -1;
        private string _lastAhegaoKeywordFingerprint = string.Empty;
        private string _cachedAhegaoKeywordCsv;
        private List<string> _cachedAhegaoKeywords;
        private string _cachedAhegaoKeywordFingerprint = string.Empty;
        private bool _lastAhegaoGuardPatchReady;
        private bool _hasAhegaoGuardPatchState;
        private float _nextLateUpdateEyeLookOnlyLogAt;
        private int _suppressedLateUpdateEyeLookOnlyCount;
        private int _lastPreCullFreezeFrame = -1;
        private float _nextPreCullFreezeLogAt;
        private int _suppressedPreCullFreezeLogs;
        private bool _eyeOverlayApiResolved;
        private bool _eyeOverlayApiAvailable;
        private string _eyeOverlayApiError = string.Empty;
        private Type _eyeOverlayControllerType;
        private Type _eyeOverlayTexType;
        private MethodInfo _eyeOverlaySetOverlayTexMethod;
        private PropertyInfo _eyeOverlayStorageProperty;
        private MethodInfo _eyeOverlayStorageGetTextureMethod;
        private object _eyeOverlayTexTypeEyeOverL;
        private object _eyeOverlayTexTypeEyeOverR;
        private bool _dollModeEyeOverlayBytesLoaded;
        private byte[] _dollModeEyeOverlayPngBytes;
        private string _dollModeEyeOverlayResolvedPath = string.Empty;
        private string _dollModeEyeOverlayLoadError = string.Empty;
        private float _nextMotionLockLogAt;
        private string _lastMotionLockLane = string.Empty;
        private string _lastMotionLockState = string.Empty;
        private float _lastMotionLockValue = float.NaN;
        private bool _faceEnterTransitionActive;
        private float _faceEnterTransitionStartTime;
        private float _faceEnterTransitionDuration;
        private Coroutine _faceExitTransitionCoroutine;
        private bool _faceExitTransitionRunning;
        private readonly Dictionary<int, FaceExpressionState> _faceExitTransitionStartByCha = new Dictionary<int, FaceExpressionState>();
        private float _faceExitTransitionStartTime;
        private float _faceExitTransitionDuration;

        private const float FixationalRescanIntervalSeconds = 2f;
        private const float AhegaoRescanIntervalSeconds = 2f;
        private const float EyeTraceRotationDeltaThresholdDeg = 0.05f;
        private const float EyeTraceRepeatedDiffCooldownSeconds = 0.35f;
        private const float EyeTraceSignalWindowPaddingSeconds = 0.1f;
        private const float EyeTraceSignalRetentionSeconds = 2f;
        private const int EyeTraceSignalCapacity = 256;
        private const float LateUpdateEyeLookOnlyLogIntervalSeconds = 1f;
        private const float PreCullFreezeLogIntervalSeconds = 1f;
        private const float CauseProbeLogIntervalSeconds = 0.1f;

        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgDollModeEnabled;
        private ConfigEntry<bool> _cfgApplyFemaleCharacters;
        private ConfigEntry<bool> _cfgApplyMaleCharacters;
        private ConfigEntry<bool> _cfgInfoLogEnabled;
        private ConfigEntry<bool> _cfgVerboseLog;
        private ConfigEntry<bool> _cfgBlockEventLogEnabled;
        private ConfigEntry<bool> _cfgCauseProbeLogEnabled;
        private ConfigEntry<bool> _cfgEyeMovementTraceLog;
        private ConfigEntry<bool> _cfgDisableAhegaoPlugins;
        private ConfigEntry<string> _cfgAhegaoPluginKeywordCsv;
        private ConfigEntry<int> _cfgDollModeEyePattern;
        private ConfigEntry<int> _cfgDollModeEyebrowPattern;
        private ConfigEntry<int> _cfgDollModeMouthPattern;
        private ConfigEntry<float> _cfgDollModeCheekRate;
        private ConfigEntry<int> _cfgDollModeTearsLevel;
        private ConfigEntry<int> _cfgDollModeFaceSweatLevel;
        private ConfigEntry<bool> _cfgAllowFaceSiruDuringDollMode;
        private ConfigEntry<float> _cfgDollModeMouthOpen;
        private ConfigEntry<float> _cfgDollModeEyesOpen;
        private ConfigEntry<bool> _cfgDollModeMotionLockWEnabled;
        private ConfigEntry<float> _cfgDollModeMotionLockWValue;
        private ConfigEntry<bool> _cfgDollModeMotionLockSEnabled;
        private ConfigEntry<float> _cfgDollModeMotionLockSValue;
        private ConfigEntry<bool> _cfgDollModeEyeOverlayEnabled;
        private ConfigEntry<string> _cfgDollModeEyeOverlayPngPath;
        private ConfigEntry<float> _cfgDollModeTransitionSeconds;

        private sealed class EyeTraceSnapshot
        {
            public int ChaId;
            public int Frame;
            public float Time;
            public string Stage;
            public bool HasController;
            public bool ControllerEnabled;
            public int TargetId;
            public bool HasLeftEye;
            public bool HasRightEye;
            public Quaternion LeftLocalRotation;
            public Quaternion RightLocalRotation;
        }

        private sealed class EyeTraceSignal
        {
            public int Frame;
            public float Time;
            public string Category;
            public string Point;
        }

        private sealed class EyeLookState
        {
            public bool HasController;
            public bool ControllerEnabled;
            public Transform Target;
            public int Pattern;
            public int TargetType;
            public float TargetRate;
            public float TargetAngle;
            public float TargetRange;
        }

        private sealed class FaceExpressionState
        {
            public int EyePattern;
            public int EyebrowPattern;
            public int MouthPattern;
            public float CheekRate;
            public byte TearsLevel;
            public byte FaceSiruLevel;
            public float EyesOpenMax;
            public float MouthOpenMax;
            public bool MouthFixed;
            public float MouthFixedRate;
        }

        private sealed class EyeOverlayState
        {
            public bool Captured;
            public bool Applied;
            public byte[] OriginalEyeOverLeftPng;
            public byte[] OriginalEyeOverRightPng;
            public string AppliedPath = string.Empty;
        }

        private sealed class EyeRotationState
        {
            public bool HasLeftEye;
            public bool HasRightEye;
            public Transform LeftEye;
            public Transform RightEye;
            public Quaternion LeftLocalRotation;
            public Quaternion RightLocalRotation;
        }

        private sealed class EyeTexOffsetEntry
        {
            public Renderer Renderer;
            public int TexId;
            public Vector2 Offset;
        }

        private sealed class EyeTextureOffsetState
        {
            public bool HasLeftRendEye;
            public bool HasRightRendEye;
            public Renderer LeftRendEye;
            public Renderer RightRendEye;
            public Vector2 LeftExpressionOffset;
            public Vector2 RightExpressionOffset;
            public List<EyeTexOffsetEntry> EyeLookOffsets = new List<EyeTexOffsetEntry>();
        }

        private sealed class InternalFaceWriteScope : IDisposable
        {
            private Plugin _owner;

            public InternalFaceWriteScope(Plugin owner)
            {
                _owner = owner;
                if (_owner != null)
                {
                    _owner._internalFaceWriteDepth++;
                }
            }

            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                if (_owner._internalFaceWriteDepth > 0)
                {
                    _owner._internalFaceWriteDepth--;
                }

                _owner = null;
            }
        }

        private sealed class InternalEyeLookWriteScope : IDisposable
        {
            private Plugin _owner;

            public InternalEyeLookWriteScope(Plugin owner)
            {
                _owner = owner;
                if (_owner != null)
                {
                    _owner._internalEyeLookWriteDepth++;
                }
            }

            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                if (_owner._internalEyeLookWriteDepth > 0)
                {
                    _owner._internalEyeLookWriteDepth--;
                }

                _owner = null;
            }
        }

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            _logFilePath = Path.Combine(_pluginDir, PluginName + ".log");

            // Runtime log output is disabled.

            _settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
            BindConfigEntries();
            ApplyGuardHooks();
            _dollModeApplied = false;
            _nextFixationalRescanAt = 0f;
            _cachedFixationalBehaviours.Clear();
            _cachedFixationalIds.Clear();
            _lastFixationalRescanCount = -1;
            _lastFixationalFallbackUsed = false;
            _nextAhegaoRescanAt = 0f;
            _cachedAhegaoBehaviours.Clear();
            _cachedAhegaoIds.Clear();
            _lastAhegaoRescanCount = -1;
            _lastAhegaoKeywordFingerprint = string.Empty;
            _lastAhegaoGuardPatchReady = false;
            _hasAhegaoGuardPatchState = false;
            ResetEyeTraceState();
            _nextLateUpdateEyeLookOnlyLogAt = 0f;
            _suppressedLateUpdateEyeLookOnlyCount = 0;
            _lastPreCullFreezeFrame = -1;
            _nextPreCullFreezeLogAt = 0f;
            _suppressedPreCullFreezeLogs = 0;
            _nextMotionLockLogAt = 0f;
            _lastMotionLockLane = string.Empty;
            _lastMotionLockState = string.Empty;
            _lastMotionLockValue = float.NaN;
            ResetEyeOverlayApiCache();
            ResetDollModeEyeOverlayPngCache();

            LogInfo(
                "[startup]"
                + $" enabled={_settings.Enabled}"
                + $" dollMode={_settings.DollModeEnabled}"
                + $" female={_settings.ApplyFemaleCharacters}"
                + $" male={_settings.ApplyMaleCharacters}"
                + $" infoLog={_settings.InfoLogEnabled}"
                + $" eyeTrace={_settings.EyeMovementTraceLog}"
                + $" blockLogs={_settings.BlockEventLogEnabled}"
                + $" causeProbe={_settings.CauseProbeLogEnabled}"
                + $" ahegaoBlock={_settings.DisableAhegaoPlugins}"
                + $" ahegaoKeywords={_settings.AhegaoPluginKeywordCsv}"
                + $" eye={_settings.DollModeEyePattern}"
                + $" eyebrow={_settings.DollModeEyebrowPattern}"
                + $" mouth={_settings.DollModeMouthPattern}"
                + $" cheek={_settings.DollModeCheekRate:0.##}"
                + $" tears={_settings.DollModeTearsLevel}"
                + $" sweat={_settings.DollModeFaceSweatLevel}"
                + $" allowFaceSiru={_settings.AllowFaceSiruDuringDollMode}"
                + $" eyesOpen={_settings.DollModeEyesOpen:0.##}"
                + $" mouthOpen={_settings.DollModeMouthOpen:0.##}"
                + $" motionLockW={_settings.DollModeMotionLockWEnabled}:{_settings.DollModeMotionLockWValue:0.##}"
                + $" motionLockS={_settings.DollModeMotionLockSEnabled}:{_settings.DollModeMotionLockSValue:0.##}"
                + $" eyeOverlayEnabled={_settings.DollModeEyeOverlayEnabled}"
                + $" eyeOverlayPath={_settings.DollModeEyeOverlayPngPath}"
                + $" transitionSec={_settings.DollModeTransitionSeconds:0.##}");

            ApplyDollModeState("startup");
        }

        private bool _cameraPreCullHookAttached;

        private void SyncCameraPreCullHook()
        {
            bool want = BlankRoomGate.IsInBlankRoom && _settings != null
                && _settings.Enabled && _settings.DollModeEnabled;
            if (want == _cameraPreCullHookAttached) return;
            if (want) Camera.onPreCull += OnCameraPreCull;
            else Camera.onPreCull -= OnCameraPreCull;
            _cameraPreCullHookAttached = want;
        }

        private void Update()
        {
            SyncCameraPreCullHook();
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (_settings == null)
            {
                return;
            }

            if (_settings.Enabled && _settings.DollModeEnabled)
            {
                ProbeEyeStateTransitions("update-pre");
            }

            ApplyDollModeState("update");

            if (!_settings.Enabled || !_settings.DollModeEnabled)
            {
                return;
            }
        }

        private void LateUpdate()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (_settings == null)
            {
                return;
            }

            if (!_settings.Enabled || !_settings.DollModeEnabled)
            {
                return;
            }

            ProbeEyeStateTransitions("late-pre");

            // Apply on LateUpdate so doll-mode write wins over same-frame Update writers.
            ApplyHideHighlightToTargets("late-update", logAlways: false);

            ProbeEyeStateTransitions("late-post");
        }

        private void OnDestroy()
        {
            try
            {
                if (_cameraPreCullHookAttached)
                {
                    Camera.onPreCull -= OnCameraPreCull;
                    _cameraPreCullHookAttached = false;
                }
                CancelFaceExitTransition("ondestroy");
                StopFaceEnterTransition("ondestroy");
                StopEyeFreezeEndOfFrameLoop();
                if (_dollModeApplied)
                {
                    RestoreOriginalHighlightState("ondestroy");
                    _dollModeApplied = false;
                }

                ResetEyeTraceState();
            }
            catch (Exception ex)
            {
                LogWarn("[destroy] restore failed: " + ex.Message);
            }

            RemoveGuardHooks();
            UnsubscribeConfigEvents();
        }

        public static bool IsDollModeEnabled()
        {
            return Instance != null
                && Instance._settings != null
                && Instance._settings.Enabled
                && Instance._settings.DollModeEnabled;
        }

        public static bool SetDollModeEnabled(bool enabled, string source)
        {
            if (Instance == null)
            {
                return false;
            }

            return Instance.SetDollModeInternal(enabled, source);
        }

        public static bool SetDollModeEnabled(bool enabled)
        {
            return SetDollModeEnabled(enabled, "external");
        }

        private bool SetDollModeInternal(bool enabled, string source)
        {
            if (_settings == null)
            {
                LogWarn("[api] set failed reason=settings_null");
                return false;
            }

            string resolvedSource = string.IsNullOrWhiteSpace(source) ? "external" : source.Trim();
            bool before = _settings.Enabled && _settings.DollModeEnabled;

            _settings.DollModeEnabled = enabled;
            _settings.Normalize();
            SyncConfigFromSettings();
            _pendingSource = "api:" + resolvedSource;
            _hasPendingSource = true;

            SettingsStore.SaveToDefault(_pluginDir, _settings, LogWarn);
            SaveBepInExConfig();
            ApplyDollModeState("api-sync");

            bool after = _settings.Enabled && _settings.DollModeEnabled;
            LogInfo($"[api] set_doll_mode requested={enabled} before={before} after={after} source={resolvedSource} result=ok");
            return true;
        }

        private void BindConfigEntries()
        {
            if (_settings == null)
            {
                LogWarn("[config] bind skipped reason=settings_null");
                return;
            }

            _cfgEnabled = Config.Bind(
                "General",
                "Enabled",
                _settings.Enabled,
                new ConfigDescription("Master switch for MainGameDollMode."));
            _cfgDollModeEnabled = Config.Bind(
                "General",
                "DollModeEnabled",
                _settings.DollModeEnabled,
                new ConfigDescription("Enable eye-highlight-off doll mode."));
            _cfgApplyFemaleCharacters = Config.Bind(
                "Targets",
                "ApplyFemaleCharacters",
                _settings.ApplyFemaleCharacters,
                new ConfigDescription("Apply to female characters."));
            _cfgApplyMaleCharacters = Config.Bind(
                "Targets",
                "ApplyMaleCharacters",
                _settings.ApplyMaleCharacters,
                new ConfigDescription("Apply to male characters."));
            _cfgInfoLogEnabled = Config.Bind(
                "Runtime",
                "InfoLogEnabled",
                _settings.InfoLogEnabled,
                new ConfigDescription("Master switch for [Info] logs from MainGameDollMode."));
            _cfgVerboseLog = Config.Bind(
                "Runtime",
                "VerboseLog",
                _settings.VerboseLog,
                new ConfigDescription("Enable verbose runtime logs."));
            _cfgBlockEventLogEnabled = Config.Bind(
                "Runtime",
                "BlockEventLogEnabled",
                _settings.BlockEventLogEnabled,
                new ConfigDescription("Emit [block]/[external-block]/[kiss-block] logs while doll mode blocks events."));
            _cfgCauseProbeLogEnabled = Config.Bind(
                "Runtime",
                "CauseProbeLogEnabled",
                _settings.CauseProbeLogEnabled,
                new ConfigDescription("Emit detailed eye-cause probe logs to identify which method path is still moving eyes."));
            _cfgEyeMovementTraceLog = Config.Bind(
                "Runtime",
                "EyeMovementTraceLog",
                _settings.EyeMovementTraceLog,
                new ConfigDescription("Log eye movement timing and mutation source hints while doll mode is enabled."));
            _cfgDisableAhegaoPlugins = Config.Bind(
                "ExternalPlugins",
                "DisableAhegaoPlugins",
                _settings.DisableAhegaoPlugins,
                new ConfigDescription("Disable Ahegao-related plugin behaviours while doll mode is enabled."));
            _cfgAhegaoPluginKeywordCsv = Config.Bind(
                "ExternalPlugins",
                "AhegaoPluginKeywordCsv",
                _settings.AhegaoPluginKeywordCsv,
                new ConfigDescription("CSV keywords used to detect Ahegao-related plugin behaviours by type/assembly name."));
            _cfgDollModeEyePattern = Config.Bind(
                "DollModeFace",
                "EyePattern",
                _settings.DollModeEyePattern,
                new ConfigDescription("Doll mode eye pattern index (>= 0)."));
            _cfgDollModeEyebrowPattern = Config.Bind(
                "DollModeFace",
                "EyebrowPattern",
                _settings.DollModeEyebrowPattern,
                new ConfigDescription("Doll mode eyebrow pattern index (>= 0)."));
            _cfgDollModeMouthPattern = Config.Bind(
                "DollModeFace",
                "MouthPattern",
                _settings.DollModeMouthPattern,
                new ConfigDescription("Doll mode mouth pattern index (>= 0)."));
            _cfgDollModeCheekRate = Config.Bind(
                "DollModeFace",
                "CheekRate",
                _settings.DollModeCheekRate,
                new ConfigDescription("Doll mode cheek redness ratio (0.00 - 1.00).", new AcceptableValueRange<float>(0f, 1f)));
            _cfgDollModeTearsLevel = Config.Bind(
                "DollModeFace",
                "TearsLevel",
                _settings.DollModeTearsLevel,
                new ConfigDescription("Doll mode tears level (0 - 10).", new AcceptableValueRange<int>(0, 10)));
            _cfgDollModeFaceSweatLevel = Config.Bind(
                "DollModeFace",
                "FaceSweatLevel",
                _settings.DollModeFaceSweatLevel,
                new ConfigDescription("Doll mode face sweat level (0 - 3).", new AcceptableValueRange<int>(0, 3)));
            _cfgAllowFaceSiruDuringDollMode = Config.Bind(
                "DollModeFace",
                "AllowFaceSiruDuringDollMode",
                _settings.AllowFaceSiruDuringDollMode,
                new ConfigDescription("Allow external face siru updates while doll mode is enabled (recommended: true)."));
            _cfgDollModeEyesOpen = Config.Bind(
                "DollModeFace",
                "EyesOpen",
                _settings.DollModeEyesOpen,
                new ConfigDescription("Doll mode eyes open ratio (0.00 - 1.00).", new AcceptableValueRange<float>(0f, 1f)));
            _cfgDollModeMouthOpen = Config.Bind(
                "DollModeFace",
                "MouthOpen",
                _settings.DollModeMouthOpen,
                new ConfigDescription("Doll mode mouth open ratio (0.00 - 1.00).", new AcceptableValueRange<float>(0f, 1f)));
            _cfgDollModeMotionLockWEnabled = Config.Bind(
                "DollModeMotion",
                "LockWEnabled",
                _settings.DollModeMotionLockWEnabled,
                new ConfigDescription("Lock W-loop motion blend while doll mode is enabled."));
            _cfgDollModeMotionLockWValue = Config.Bind(
                "DollModeMotion",
                "LockWValue",
                _settings.DollModeMotionLockWValue,
                new ConfigDescription("W-loop motion blend fixed value (0.00 - 1.00).", new AcceptableValueRange<float>(0f, 1f)));
            _cfgDollModeMotionLockSEnabled = Config.Bind(
                "DollModeMotion",
                "LockSEnabled",
                _settings.DollModeMotionLockSEnabled,
                new ConfigDescription("Lock S-loop motion blend while doll mode is enabled."));
            _cfgDollModeMotionLockSValue = Config.Bind(
                "DollModeMotion",
                "LockSValue",
                _settings.DollModeMotionLockSValue,
                new ConfigDescription("S-loop motion blend fixed value (0.00 - 1.00).", new AcceptableValueRange<float>(0f, 1f)));
            _cfgDollModeEyeOverlayEnabled = Config.Bind(
                "DollModeOverlay",
                "EyeOverlayEnabled",
                _settings.DollModeEyeOverlayEnabled,
                new ConfigDescription("Apply eye overlay PNG while doll mode is enabled."));
            _cfgDollModeEyeOverlayPngPath = Config.Bind(
                "DollModeOverlay",
                "EyeOverlayPngPath",
                _settings.DollModeEyeOverlayPngPath,
                new ConfigDescription("Eye overlay PNG path (absolute or relative to plugin folder)."));
            _cfgDollModeTransitionSeconds = Config.Bind(
                "DollModeFace",
                "TransitionSeconds",
                _settings.DollModeTransitionSeconds,
                new ConfigDescription("Face transition seconds when doll mode is toggled (0.00 - 10.00).", new AcceptableValueRange<float>(0f, 10f)));

            _cfgEnabled.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeEnabled.SettingChanged += OnConfigSettingChanged;
            _cfgApplyFemaleCharacters.SettingChanged += OnConfigSettingChanged;
            _cfgApplyMaleCharacters.SettingChanged += OnConfigSettingChanged;
            _cfgInfoLogEnabled.SettingChanged += OnConfigSettingChanged;
            _cfgVerboseLog.SettingChanged += OnConfigSettingChanged;
            _cfgBlockEventLogEnabled.SettingChanged += OnConfigSettingChanged;
            _cfgCauseProbeLogEnabled.SettingChanged += OnConfigSettingChanged;
            _cfgEyeMovementTraceLog.SettingChanged += OnConfigSettingChanged;
            _cfgDisableAhegaoPlugins.SettingChanged += OnConfigSettingChanged;
            _cfgAhegaoPluginKeywordCsv.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeEyePattern.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeEyebrowPattern.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeMouthPattern.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeCheekRate.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeTearsLevel.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeFaceSweatLevel.SettingChanged += OnConfigSettingChanged;
            _cfgAllowFaceSiruDuringDollMode.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeEyesOpen.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeMouthOpen.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeMotionLockWEnabled.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeMotionLockWValue.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeMotionLockSEnabled.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeMotionLockSValue.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeEyeOverlayEnabled.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeEyeOverlayPngPath.SettingChanged += OnConfigSettingChanged;
            _cfgDollModeTransitionSeconds.SettingChanged += OnConfigSettingChanged;

            // JSON is the source of truth; force BepInEx config entries to JSON state.
            SyncConfigFromSettings();
            SaveBepInExConfig();

            LogInfo(
                "[config] bind completed"
                + $" enabled={_settings.Enabled}"
                + $" dollMode={_settings.DollModeEnabled}"
                + $" female={_settings.ApplyFemaleCharacters}"
                + $" male={_settings.ApplyMaleCharacters}"
                + $" infoLog={_settings.InfoLogEnabled}"
                + $" verbose={_settings.VerboseLog}"
                + $" blockLogs={_settings.BlockEventLogEnabled}"
                + $" causeProbe={_settings.CauseProbeLogEnabled}"
                + $" eyeTrace={_settings.EyeMovementTraceLog}"
                + $" ahegaoBlock={_settings.DisableAhegaoPlugins}"
                + $" ahegaoKeywords={_settings.AhegaoPluginKeywordCsv}"
                + $" eye={_settings.DollModeEyePattern}"
                + $" eyebrow={_settings.DollModeEyebrowPattern}"
                + $" mouth={_settings.DollModeMouthPattern}"
                + $" cheek={_settings.DollModeCheekRate:0.##}"
                + $" tears={_settings.DollModeTearsLevel}"
                + $" sweat={_settings.DollModeFaceSweatLevel}"
                + $" allowFaceSiru={_settings.AllowFaceSiruDuringDollMode}"
                + $" eyesOpen={_settings.DollModeEyesOpen:0.##}"
                + $" mouthOpen={_settings.DollModeMouthOpen:0.##}"
                + $" motionLockW={_settings.DollModeMotionLockWEnabled}:{_settings.DollModeMotionLockWValue:0.##}"
                + $" motionLockS={_settings.DollModeMotionLockSEnabled}:{_settings.DollModeMotionLockSValue:0.##}"
                + $" eyeOverlayEnabled={_settings.DollModeEyeOverlayEnabled}"
                + $" eyeOverlayPath={_settings.DollModeEyeOverlayPngPath}"
                + $" transitionSec={_settings.DollModeTransitionSeconds:0.##}"
                + " source=json result=ok");
        }

        private void UnsubscribeConfigEvents()
        {
            if (_cfgEnabled != null) _cfgEnabled.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeEnabled != null) _cfgDollModeEnabled.SettingChanged -= OnConfigSettingChanged;
            if (_cfgApplyFemaleCharacters != null) _cfgApplyFemaleCharacters.SettingChanged -= OnConfigSettingChanged;
            if (_cfgApplyMaleCharacters != null) _cfgApplyMaleCharacters.SettingChanged -= OnConfigSettingChanged;
            if (_cfgInfoLogEnabled != null) _cfgInfoLogEnabled.SettingChanged -= OnConfigSettingChanged;
            if (_cfgVerboseLog != null) _cfgVerboseLog.SettingChanged -= OnConfigSettingChanged;
            if (_cfgBlockEventLogEnabled != null) _cfgBlockEventLogEnabled.SettingChanged -= OnConfigSettingChanged;
            if (_cfgCauseProbeLogEnabled != null) _cfgCauseProbeLogEnabled.SettingChanged -= OnConfigSettingChanged;
            if (_cfgEyeMovementTraceLog != null) _cfgEyeMovementTraceLog.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDisableAhegaoPlugins != null) _cfgDisableAhegaoPlugins.SettingChanged -= OnConfigSettingChanged;
            if (_cfgAhegaoPluginKeywordCsv != null) _cfgAhegaoPluginKeywordCsv.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeEyePattern != null) _cfgDollModeEyePattern.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeEyebrowPattern != null) _cfgDollModeEyebrowPattern.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeMouthPattern != null) _cfgDollModeMouthPattern.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeCheekRate != null) _cfgDollModeCheekRate.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeTearsLevel != null) _cfgDollModeTearsLevel.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeFaceSweatLevel != null) _cfgDollModeFaceSweatLevel.SettingChanged -= OnConfigSettingChanged;
            if (_cfgAllowFaceSiruDuringDollMode != null) _cfgAllowFaceSiruDuringDollMode.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeEyesOpen != null) _cfgDollModeEyesOpen.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeMouthOpen != null) _cfgDollModeMouthOpen.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeMotionLockWEnabled != null) _cfgDollModeMotionLockWEnabled.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeMotionLockWValue != null) _cfgDollModeMotionLockWValue.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeMotionLockSEnabled != null) _cfgDollModeMotionLockSEnabled.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeMotionLockSValue != null) _cfgDollModeMotionLockSValue.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeEyeOverlayEnabled != null) _cfgDollModeEyeOverlayEnabled.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeEyeOverlayPngPath != null) _cfgDollModeEyeOverlayPngPath.SettingChanged -= OnConfigSettingChanged;
            if (_cfgDollModeTransitionSeconds != null) _cfgDollModeTransitionSeconds.SettingChanged -= OnConfigSettingChanged;
        }

        private void OnConfigSettingChanged(object sender, EventArgs e)
        {
            if (_isConfigSyncing || _settings == null)
            {
                return;
            }

            bool before = _settings.Enabled && _settings.DollModeEnabled;

            _settings.Enabled = _cfgEnabled != null && _cfgEnabled.Value;
            _settings.DollModeEnabled = _cfgDollModeEnabled != null && _cfgDollModeEnabled.Value;
            _settings.ApplyFemaleCharacters = _cfgApplyFemaleCharacters != null && _cfgApplyFemaleCharacters.Value;
            _settings.ApplyMaleCharacters = _cfgApplyMaleCharacters != null && _cfgApplyMaleCharacters.Value;
            _settings.InfoLogEnabled = _cfgInfoLogEnabled != null && _cfgInfoLogEnabled.Value;
            _settings.VerboseLog = _cfgVerboseLog != null && _cfgVerboseLog.Value;
            _settings.BlockEventLogEnabled = _cfgBlockEventLogEnabled != null && _cfgBlockEventLogEnabled.Value;
            _settings.CauseProbeLogEnabled = _cfgCauseProbeLogEnabled != null && _cfgCauseProbeLogEnabled.Value;
            _settings.EyeMovementTraceLog = _cfgEyeMovementTraceLog != null && _cfgEyeMovementTraceLog.Value;
            _settings.DisableAhegaoPlugins = _cfgDisableAhegaoPlugins != null && _cfgDisableAhegaoPlugins.Value;
            if (_cfgAhegaoPluginKeywordCsv != null) _settings.AhegaoPluginKeywordCsv = _cfgAhegaoPluginKeywordCsv.Value;
            if (_cfgDollModeEyePattern != null) _settings.DollModeEyePattern = _cfgDollModeEyePattern.Value;
            if (_cfgDollModeEyebrowPattern != null) _settings.DollModeEyebrowPattern = _cfgDollModeEyebrowPattern.Value;
            if (_cfgDollModeMouthPattern != null) _settings.DollModeMouthPattern = _cfgDollModeMouthPattern.Value;
            if (_cfgDollModeCheekRate != null) _settings.DollModeCheekRate = _cfgDollModeCheekRate.Value;
            if (_cfgDollModeTearsLevel != null) _settings.DollModeTearsLevel = _cfgDollModeTearsLevel.Value;
            if (_cfgDollModeFaceSweatLevel != null) _settings.DollModeFaceSweatLevel = _cfgDollModeFaceSweatLevel.Value;
            if (_cfgAllowFaceSiruDuringDollMode != null) _settings.AllowFaceSiruDuringDollMode = _cfgAllowFaceSiruDuringDollMode.Value;
            if (_cfgDollModeEyesOpen != null) _settings.DollModeEyesOpen = _cfgDollModeEyesOpen.Value;
            if (_cfgDollModeMouthOpen != null) _settings.DollModeMouthOpen = _cfgDollModeMouthOpen.Value;
            if (_cfgDollModeMotionLockWEnabled != null) _settings.DollModeMotionLockWEnabled = _cfgDollModeMotionLockWEnabled.Value;
            if (_cfgDollModeMotionLockWValue != null) _settings.DollModeMotionLockWValue = _cfgDollModeMotionLockWValue.Value;
            if (_cfgDollModeMotionLockSEnabled != null) _settings.DollModeMotionLockSEnabled = _cfgDollModeMotionLockSEnabled.Value;
            if (_cfgDollModeMotionLockSValue != null) _settings.DollModeMotionLockSValue = _cfgDollModeMotionLockSValue.Value;
            if (_cfgDollModeEyeOverlayEnabled != null) _settings.DollModeEyeOverlayEnabled = _cfgDollModeEyeOverlayEnabled.Value;
            if (_cfgDollModeEyeOverlayPngPath != null) _settings.DollModeEyeOverlayPngPath = _cfgDollModeEyeOverlayPngPath.Value;
            if (_cfgDollModeTransitionSeconds != null) _settings.DollModeTransitionSeconds = _cfgDollModeTransitionSeconds.Value;
            _settings.Normalize();
            ResetDollModeEyeOverlayPngCache();

            // Push normalized values back to UI-visible config entries.
            SyncConfigFromSettings();
            SaveBepInExConfig();
            SettingsStore.SaveToDefault(_pluginDir, _settings, LogWarn);

            _pendingSource = "config-manager";
            _hasPendingSource = true;
            ApplyDollModeState("config-manager");
            if (_settings.Enabled && _settings.DollModeEnabled)
            {
                ApplyHideHighlightToTargets("config-manager", logAlways: true);
            }

            bool after = _settings.Enabled && _settings.DollModeEnabled;
            LogInfo(
                "[config] changed"
                + $" before={before}"
                + $" after={after}"
                + $" enabled={_settings.Enabled}"
                + $" dollMode={_settings.DollModeEnabled}"
                + $" female={_settings.ApplyFemaleCharacters}"
                + $" male={_settings.ApplyMaleCharacters}"
                + $" infoLog={_settings.InfoLogEnabled}"
                + $" verbose={_settings.VerboseLog}"
                + $" blockLogs={_settings.BlockEventLogEnabled}"
                + $" causeProbe={_settings.CauseProbeLogEnabled}"
                + $" eyeTrace={_settings.EyeMovementTraceLog}"
                + $" ahegaoBlock={_settings.DisableAhegaoPlugins}"
                + $" ahegaoKeywords={_settings.AhegaoPluginKeywordCsv}"
                + $" eye={_settings.DollModeEyePattern}"
                + $" eyebrow={_settings.DollModeEyebrowPattern}"
                + $" mouth={_settings.DollModeMouthPattern}"
                + $" cheek={_settings.DollModeCheekRate:0.##}"
                + $" tears={_settings.DollModeTearsLevel}"
                + $" sweat={_settings.DollModeFaceSweatLevel}"
                + $" allowFaceSiru={_settings.AllowFaceSiruDuringDollMode}"
                + $" eyesOpen={_settings.DollModeEyesOpen:0.##}"
                + $" mouthOpen={_settings.DollModeMouthOpen:0.##}"
                + $" motionLockW={_settings.DollModeMotionLockWEnabled}:{_settings.DollModeMotionLockWValue:0.##}"
                + $" motionLockS={_settings.DollModeMotionLockSEnabled}:{_settings.DollModeMotionLockSValue:0.##}"
                + $" eyeOverlayEnabled={_settings.DollModeEyeOverlayEnabled}"
                + $" eyeOverlayPath={_settings.DollModeEyeOverlayPngPath}"
                + $" transitionSec={_settings.DollModeTransitionSeconds:0.##}"
                + " jsonSave=ok result=applied");
        }

        private void SyncConfigFromSettings()
        {
            if (_settings == null)
            {
                return;
            }

            _isConfigSyncing = true;
            try
            {
                if (_cfgEnabled != null) _cfgEnabled.Value = _settings.Enabled;
                if (_cfgDollModeEnabled != null) _cfgDollModeEnabled.Value = _settings.DollModeEnabled;
                if (_cfgApplyFemaleCharacters != null) _cfgApplyFemaleCharacters.Value = _settings.ApplyFemaleCharacters;
                if (_cfgApplyMaleCharacters != null) _cfgApplyMaleCharacters.Value = _settings.ApplyMaleCharacters;
                if (_cfgInfoLogEnabled != null) _cfgInfoLogEnabled.Value = _settings.InfoLogEnabled;
                if (_cfgVerboseLog != null) _cfgVerboseLog.Value = _settings.VerboseLog;
                if (_cfgBlockEventLogEnabled != null) _cfgBlockEventLogEnabled.Value = _settings.BlockEventLogEnabled;
                if (_cfgCauseProbeLogEnabled != null) _cfgCauseProbeLogEnabled.Value = _settings.CauseProbeLogEnabled;
                if (_cfgEyeMovementTraceLog != null) _cfgEyeMovementTraceLog.Value = _settings.EyeMovementTraceLog;
                if (_cfgDisableAhegaoPlugins != null) _cfgDisableAhegaoPlugins.Value = _settings.DisableAhegaoPlugins;
                if (_cfgAhegaoPluginKeywordCsv != null) _cfgAhegaoPluginKeywordCsv.Value = _settings.AhegaoPluginKeywordCsv;
                if (_cfgDollModeEyePattern != null) _cfgDollModeEyePattern.Value = _settings.DollModeEyePattern;
                if (_cfgDollModeEyebrowPattern != null) _cfgDollModeEyebrowPattern.Value = _settings.DollModeEyebrowPattern;
                if (_cfgDollModeMouthPattern != null) _cfgDollModeMouthPattern.Value = _settings.DollModeMouthPattern;
                if (_cfgDollModeCheekRate != null) _cfgDollModeCheekRate.Value = _settings.DollModeCheekRate;
                if (_cfgDollModeTearsLevel != null) _cfgDollModeTearsLevel.Value = _settings.DollModeTearsLevel;
                if (_cfgDollModeFaceSweatLevel != null) _cfgDollModeFaceSweatLevel.Value = _settings.DollModeFaceSweatLevel;
                if (_cfgAllowFaceSiruDuringDollMode != null) _cfgAllowFaceSiruDuringDollMode.Value = _settings.AllowFaceSiruDuringDollMode;
                if (_cfgDollModeEyesOpen != null) _cfgDollModeEyesOpen.Value = _settings.DollModeEyesOpen;
                if (_cfgDollModeMouthOpen != null) _cfgDollModeMouthOpen.Value = _settings.DollModeMouthOpen;
                if (_cfgDollModeMotionLockWEnabled != null) _cfgDollModeMotionLockWEnabled.Value = _settings.DollModeMotionLockWEnabled;
                if (_cfgDollModeMotionLockWValue != null) _cfgDollModeMotionLockWValue.Value = _settings.DollModeMotionLockWValue;
                if (_cfgDollModeMotionLockSEnabled != null) _cfgDollModeMotionLockSEnabled.Value = _settings.DollModeMotionLockSEnabled;
                if (_cfgDollModeMotionLockSValue != null) _cfgDollModeMotionLockSValue.Value = _settings.DollModeMotionLockSValue;
                if (_cfgDollModeEyeOverlayEnabled != null) _cfgDollModeEyeOverlayEnabled.Value = _settings.DollModeEyeOverlayEnabled;
                if (_cfgDollModeEyeOverlayPngPath != null) _cfgDollModeEyeOverlayPngPath.Value = _settings.DollModeEyeOverlayPngPath;
                if (_cfgDollModeTransitionSeconds != null) _cfgDollModeTransitionSeconds.Value = _settings.DollModeTransitionSeconds;
            }
            finally
            {
                _isConfigSyncing = false;
            }
        }

        private void SaveBepInExConfig()
        {
            try
            {
                Config.Save();
            }
            catch (Exception ex)
            {
                LogWarn("[config] save failed: " + ex.Message);
            }
        }

        private void ApplyGuardHooks()
        {
            try
            {
                _harmony = new Harmony(GUID + ".guard");
                DollModeGuardHooks.Apply(_harmony, LogInfo, LogWarn, LogError);
                LogInfo("[guard] hooks applied result=ok");
            }
            catch (Exception ex)
            {
                LogError("[guard] hooks apply failed: " + ex.Message);
            }
        }

        private void RemoveGuardHooks()
        {
            try
            {
                _harmony?.UnpatchSelf();
                _harmony = null;
                LogInfo("[guard] hooks removed result=ok");
            }
            catch (Exception ex)
            {
                LogWarn("[guard] hooks remove failed: " + ex.Message);
            }
        }

        internal bool ShouldBlockGameEvents()
        {
            return _settings != null
                && _settings.Enabled
                && (_settings.DollModeEnabled || _faceExitTransitionRunning);
        }

        internal bool TryApplyMotionLock(HFlag flags, string hookPoint, string ownerType)
        {
            if (flags == null)
            {
                return false;
            }

            string lane;
            float targetValue;
            string stateName;
            if (!TryResolveMotionLock(flags, out lane, out targetValue, out stateName))
            {
                return false;
            }

            float beforeMotion = flags.motion;
            float beforeMotion1 = flags.motion1;
            float rawSample = Mathf.Clamp01((beforeMotion + beforeMotion1) * 0.5f);
            float lockValue = Mathf.Clamp01(targetValue);
            float appliedValue = lockValue;

            if (_settings != null && _settings.DollModeEnabled)
            {
                float enterWeight = ResolveFaceEnterTransitionWeight();
                if (enterWeight < 1f)
                {
                    // Transition is evaluated from live raw value each frame to avoid loop-switch warps.
                    appliedValue = Mathf.Lerp(rawSample, lockValue, enterWeight);
                }
            }
            else if (_faceExitTransitionRunning)
            {
                float exitWeight = ResolveFaceExitTransitionWeight();
                // Transition is evaluated from lock target to current live raw value.
                appliedValue = exitWeight < 1f
                    ? Mathf.Lerp(lockValue, rawSample, exitWeight)
                    : rawSample;
            }

            appliedValue = Mathf.Clamp01(appliedValue);
            flags.motion = appliedValue;
            flags.motion1 = appliedValue;
            float afterMotion = flags.motion;
            float afterMotion1 = flags.motion1;

            LogMotionLockApplied(
                hookPoint,
                ownerType,
                lane,
                stateName,
                appliedValue,
                beforeMotion,
                afterMotion,
                beforeMotion1,
                afterMotion1);

            return true;
        }

        private bool TryResolveMotionLock(HFlag flags, out string lane, out float targetValue, out string stateName)
        {
            lane = string.Empty;
            targetValue = 0f;
            stateName = string.Empty;

            if (flags == null || _settings == null || !ShouldBlockGameEvents())
            {
                return false;
            }

            stateName = NormalizeStateName(flags.nowAnimStateName);
            bool isWLoop = IsWLoopState(stateName);
            bool isSLoop = IsSLoopState(stateName);

            if (isWLoop && _settings.DollModeMotionLockWEnabled)
            {
                lane = "W";
                targetValue = _settings.DollModeMotionLockWValue;
                return true;
            }

            if (isSLoop && _settings.DollModeMotionLockSEnabled)
            {
                lane = "S";
                targetValue = _settings.DollModeMotionLockSValue;
                return true;
            }

            return false;
        }

        private void LogMotionLockApplied(
            string hookPoint,
            string ownerType,
            string lane,
            string stateName,
            float value,
            float beforeMotion,
            float afterMotion,
            float beforeMotion1,
            float afterMotion1)
        {
            if (_settings == null)
            {
                return;
            }

            bool changedMain = IsDifferent(beforeMotion, afterMotion);
            bool changedSub = IsDifferent(beforeMotion1, afterMotion1);
            bool signatureChanged =
                !string.Equals(_lastMotionLockLane, lane, StringComparison.Ordinal)
                || !string.Equals(_lastMotionLockState, stateName, StringComparison.Ordinal)
                || IsDifferent(_lastMotionLockValue, value);
            bool verbose = _settings.VerboseLog;
            float now = Time.unscaledTime;

            if (!signatureChanged && !changedMain && !changedSub && !verbose && now < _nextMotionLockLogAt)
            {
                return;
            }

            _nextMotionLockLogAt = verbose ? 0f : now + 1f;
            _lastMotionLockLane = lane ?? string.Empty;
            _lastMotionLockState = stateName ?? string.Empty;
            _lastMotionLockValue = value;

            LogInfo(
                "[doll-mode][motion-lock]"
                + " frame=" + Time.frameCount
                + " t=" + now.ToString("0.###")
                + " point=" + (string.IsNullOrWhiteSpace(hookPoint) ? "(unknown)" : hookPoint)
                + " owner=" + (string.IsNullOrWhiteSpace(ownerType) ? "(unknown)" : ownerType)
                + " lane=" + (string.IsNullOrWhiteSpace(lane) ? "(none)" : lane)
                + " state=" + (string.IsNullOrWhiteSpace(stateName) ? "(empty)" : stateName)
                + " value=" + value.ToString("0.##")
                + " before=" + beforeMotion.ToString("0.###")
                + " after=" + afterMotion.ToString("0.###")
                + " before1=" + beforeMotion1.ToString("0.###")
                + " after1=" + afterMotion1.ToString("0.###")
                + " changedMain=" + BoolTo01(changedMain)
                + " changedSub=" + BoolTo01(changedSub)
                + " result=applied");
        }

        private static string NormalizeStateName(string stateName)
        {
            return (stateName ?? string.Empty).Trim();
        }

        private static bool IsWLoopState(string stateName)
        {
            return !string.IsNullOrWhiteSpace(stateName)
                && stateName.IndexOf("WLoop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSLoopState(string stateName)
        {
            return !string.IsNullOrWhiteSpace(stateName)
                && stateName.IndexOf("SLoop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal bool IsTargetCharacterPublic(ChaControl cha)
        {
            return IsTargetCharacter(cha);
        }

        internal bool ShouldBlockFaceExpressionApi(ChaControl cha)
        {
            if (!ShouldBlockGameEvents())
            {
                return false;
            }

            if (_internalFaceWriteDepth > 0)
            {
                return false;
            }

            return IsTargetCharacter(cha);
        }

        private IDisposable BeginInternalFaceWrite()
        {
            return new InternalFaceWriteScope(this);
        }

        private IDisposable BeginInternalEyeLookWrite()
        {
            return new InternalEyeLookWriteScope(this);
        }

        internal bool ShouldBlockEyeLook(EyeLookController eyeLookController)
        {
            if (!ShouldBlockGameEvents())
            {
                return false;
            }

            if (_internalEyeLookWriteDepth > 0)
            {
                return false;
            }

            if (eyeLookController == null)
            {
                return false;
            }

            ChaControl cha = null;
            try
            {
                cha = eyeLookController.GetComponentInParent<ChaControl>();
            }
            catch
            {
                return false;
            }

            return IsTargetCharacter(cha);
        }

        internal bool ShouldBlockLookTargetApi(ChaControl cha)
        {
            if (!ShouldBlockGameEvents())
            {
                return false;
            }

            if (_internalEyeLookWriteDepth > 0)
            {
                return false;
            }

            return IsTargetCharacter(cha);
        }

        internal bool ShouldBlockEyeLookCalc(EyeLookCalc eyeLookCalc)
        {
            if (!ShouldBlockGameEvents())
            {
                return false;
            }

            if (eyeLookCalc == null)
            {
                return false;
            }

            ChaControl cha = null;
            try
            {
                cha = eyeLookCalc.GetComponentInParent<ChaControl>();
            }
            catch
            {
                return false;
            }

            return IsTargetCharacter(cha);
        }

        internal bool ShouldBlockEyeLookMaterial(EyeLookMaterialControll controller)
        {
            if (!ShouldBlockGameEvents())
            {
                return false;
            }

            if (controller == null)
            {
                return false;
            }

            ChaControl cha = null;
            try
            {
                cha = controller.GetComponentInParent<ChaControl>();
            }
            catch
            {
                return false;
            }

            return IsTargetCharacter(cha);
        }

        internal bool ShouldBlockFixationalInstance(object instance)
        {
            if (!ShouldBlockGameEvents())
            {
                return false;
            }

            if (instance == null)
            {
                return true;
            }

            var behaviour = instance as MonoBehaviour;
            if (behaviour == null)
            {
                return true;
            }

            if (!IsFixationalBehaviour(behaviour))
            {
                return false;
            }

            return ShouldAffectFixationalBehaviour(behaviour);
        }

        private bool IsBlockEventLogEnabled()
        {
            return _settings != null && _settings.BlockEventLogEnabled;
        }

        private bool IsCauseProbeEnabled()
        {
            return _settings != null
                && _settings.CauseProbeLogEnabled
                && _settings.Enabled
                && _settings.DollModeEnabled;
        }

        internal void OnCauseProbe(string point, bool blocked, ChaControl cha = null, string note = null)
        {
            if (string.IsNullOrWhiteSpace(point))
            {
                return;
            }

            if (!IsCauseProbeEnabled())
            {
                return;
            }

            string signalPoint = string.IsNullOrWhiteSpace(note)
                ? point
                : point + ":" + note;
            RecordEyeTraceSignal(blocked ? "cause-blocked" : "cause-pass", signalPoint);

            float now = Time.unscaledTime;
            string key = "cause:" + point + ":" + (blocked ? "1" : "0");
            float nextAllowed;
            if (_blockLogCooldownByPoint.TryGetValue(key, out nextAllowed) && now < nextAllowed)
            {
                return;
            }

            _blockLogCooldownByPoint[key] = now + CauseProbeLogIntervalSeconds;
            int chaId = cha != null ? cha.GetInstanceID() : 0;
            LogInfo(
                "[cause-probe]"
                + " frame=" + Time.frameCount
                + " t=" + now.ToString("0.###")
                + " point=" + point
                + " blocked=" + BoolTo01(blocked)
                + " cha=" + chaId
                + " note=" + (string.IsNullOrWhiteSpace(note) ? "(none)" : note)
                + " result=observed");
        }

        internal void OnGameEventBlocked(string point)
        {
            if (string.IsNullOrWhiteSpace(point))
            {
                return;
            }

            if (!ShouldBlockGameEvents())
            {
                return;
            }

            RecordEyeTraceSignal("blocked-game", point);

            if (!IsBlockEventLogEnabled())
            {
                return;
            }

            float now = Time.unscaledTime;
            float nextAllowed;
            if (_blockLogCooldownByPoint.TryGetValue(point, out nextAllowed) && now < nextAllowed)
            {
                return;
            }

            _blockLogCooldownByPoint[point] = now + 1f;
            LogInfo(
                "[block]"
                + " frame=" + Time.frameCount
                + " t=" + now.ToString("0.###")
                + " point=" + point
                + " mode=doll result=blocked");
        }

        internal void OnKissEventBlocked(string point)
        {
            if (string.IsNullOrWhiteSpace(point))
            {
                return;
            }

            if (!ShouldBlockGameEvents())
            {
                return;
            }

            RecordEyeTraceSignal("blocked-kiss", point);

            if (!IsBlockEventLogEnabled())
            {
                return;
            }

            string key = "kiss:" + point;
            float now = Time.unscaledTime;
            float nextAllowed;
            if (_blockLogCooldownByPoint.TryGetValue(key, out nextAllowed) && now < nextAllowed)
            {
                return;
            }

            _blockLogCooldownByPoint[key] = now + 1f;
            LogInfo(
                "[kiss-block]"
                + " frame=" + Time.frameCount
                + " t=" + now.ToString("0.###")
                + " point=" + point
                + " mode=doll result=blocked");
        }

        internal void OnExternalPluginBlocked(string point)
        {
            if (string.IsNullOrWhiteSpace(point))
            {
                return;
            }

            if (!ShouldBlockGameEvents())
            {
                return;
            }

            RecordEyeTraceSignal("blocked-external", point);

            if (!IsBlockEventLogEnabled())
            {
                return;
            }

            string key = "external:" + point;
            float now = Time.unscaledTime;
            float nextAllowed;
            if (_blockLogCooldownByPoint.TryGetValue(key, out nextAllowed) && now < nextAllowed)
            {
                return;
            }

            _blockLogCooldownByPoint[key] = now + 1f;
            LogInfo(
                "[external-block]"
                + " frame=" + Time.frameCount
                + " t=" + now.ToString("0.###")
                + " point=" + point
                + " mode=doll result=blocked");
        }

        private void ApplyDollModeState(string source)
        {
            SyncCameraPreCullHook();
            bool shouldEnable = _settings != null && _settings.Enabled && _settings.DollModeEnabled;
            string reason = ResolveSource(source);

            if (shouldEnable)
            {
                if (_faceExitTransitionRunning)
                {
                    CancelFaceExitTransition("cancel-by-enable:" + reason);
                }

                if (!_dollModeApplied)
                {
                    _dollModeApplied = true;
                    ResetEyeTraceState();
                    _nextLateUpdateEyeLookOnlyLogAt = 0f;
                    _suppressedLateUpdateEyeLookOnlyCount = 0;
                    _lastPreCullFreezeFrame = -1;
                    _nextPreCullFreezeLogAt = 0f;
                    _suppressedPreCullFreezeLogs = 0;
                    _nextMotionLockLogAt = 0f;
                    _lastMotionLockLane = string.Empty;
                    _lastMotionLockState = string.Empty;
                    _lastMotionLockValue = float.NaN;
                    StartEyeFreezeEndOfFrameLoop();
                    _nextFixationalRescanAt = 0f;
                    _nextAhegaoRescanAt = 0f;
                    ResetDollModeEyeOverlayPngCache();
                    _cachedActiveCharacters = null;
                    StartFaceEnterTransition(reason);
                    LogInfo(
                        "[doll-mode] enter"
                        + " source=" + reason
                        + " eventBlock=enabled"
                        + " transitionSec=" + ResolveFaceTransitionDurationSeconds().ToString("0.##"));
                    ApplyHideHighlightToTargets(reason, logAlways: true);
                    ProbeEyeStateTransitions("enter-post");
                }
                else if (_eyeFreezeEndOfFrameCoroutine == null)
                {
                    StartEyeFreezeEndOfFrameLoop();
                }
                return;
            }

            if (_dollModeApplied)
            {
                if (TryStartFaceExitTransition(reason))
                {
                    return;
                }

                FinalizeDollModeExit(reason);
            }
        }

        private string ResolveSource(string fallback)
        {
            if (_hasPendingSource)
            {
                _hasPendingSource = false;
                return _pendingSource;
            }

            return string.IsNullOrWhiteSpace(fallback) ? "unknown" : fallback;
        }

        private float ResolveFaceTransitionDurationSeconds()
        {
            if (_settings == null)
            {
                return 1f;
            }

            return Mathf.Clamp(_settings.DollModeTransitionSeconds, 0f, 10f);
        }

        private void StartFaceEnterTransition(string reason)
        {
            float duration = ResolveFaceTransitionDurationSeconds();
            if (duration <= 0.0001f)
            {
                _faceEnterTransitionActive = false;
                _faceEnterTransitionDuration = 0f;
                _faceEnterTransitionStartTime = 0f;
                return;
            }

            _faceEnterTransitionActive = true;
            _faceEnterTransitionDuration = duration;
            _faceEnterTransitionStartTime = Time.unscaledTime;
            LogInfo(
                "[doll-mode] transition-enter start"
                + " source=" + reason
                + " duration=" + duration.ToString("0.##")
                + " result=started");
        }

        private void StopFaceEnterTransition(string reason)
        {
            if (!_faceEnterTransitionActive)
            {
                return;
            }

            _faceEnterTransitionActive = false;
            _faceEnterTransitionDuration = 0f;
            _faceEnterTransitionStartTime = 0f;
            LogInfo(
                "[doll-mode] transition-enter stop"
                + " source=" + reason
                + " result=stopped");
        }

        private float ResolveFaceExitTransitionWeight()
        {
            if (!_faceExitTransitionRunning || _faceExitTransitionDuration <= 0.0001f)
            {
                return 1f;
            }

            float elapsed = Time.unscaledTime - _faceExitTransitionStartTime;
            return Mathf.Clamp01(elapsed / _faceExitTransitionDuration);
        }

        private float ResolveFaceEnterTransitionWeight()
        {
            if (!_faceEnterTransitionActive)
            {
                return 1f;
            }

            if (_faceEnterTransitionDuration <= 0.0001f)
            {
                _faceEnterTransitionActive = false;
                return 1f;
            }

            float elapsed = Time.unscaledTime - _faceEnterTransitionStartTime;
            float weight = Mathf.Clamp01(elapsed / _faceEnterTransitionDuration);
            if (weight >= 1f)
            {
                _faceEnterTransitionActive = false;
                LogInfo(
                    "[doll-mode] transition-enter end"
                    + " duration=" + _faceEnterTransitionDuration.ToString("0.##")
                    + " result=completed");
                return 1f;
            }

            return weight;
        }

        private bool TryStartFaceExitTransition(string reason)
        {
            if (_faceExitTransitionRunning)
            {
                return true;
            }

            float duration = ResolveFaceTransitionDurationSeconds();
            if (duration <= 0.0001f)
            {
                return false;
            }

            StopFaceEnterTransition("exit-start:" + reason);

            _faceExitTransitionStartByCha.Clear();
            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindNonIndependent();
            var map = new Dictionary<int, ChaControl>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha == null)
                {
                    continue;
                }

                if (SavedActorMatches(cha)) map[cha.GetInstanceID()] = cha;
            }

            foreach (var pair in _originalFaceExpressionByCha)
            {
                ChaControl cha;
                if (!map.TryGetValue(pair.Key, out cha) || cha == null)
                {
                    continue;
                }

                _faceExitTransitionStartByCha[pair.Key] = CaptureFaceExpressionState(cha);
            }

            _faceExitTransitionRunning = true;
            _faceExitTransitionStartTime = Time.unscaledTime;
            _faceExitTransitionDuration = duration;
            _faceExitTransitionCoroutine = StartCoroutine(FaceExitTransitionCoroutine(reason, duration));
            LogInfo(
                "[doll-mode] transition-exit start"
                + " source=" + reason
                + " duration=" + duration.ToString("0.##")
                + " targets=" + _faceExitTransitionStartByCha.Count
                + " motionTransition=dynamic_raw"
                + " result=started");
            return true;
        }

        private void CancelFaceExitTransition(string reason)
        {
            if (!_faceExitTransitionRunning)
            {
                return;
            }

            if (_faceExitTransitionCoroutine != null)
            {
                try
                {
                    StopCoroutine(_faceExitTransitionCoroutine);
                }
                catch
                {
                    // ignore stop errors during scene teardown or fast toggles
                }
            }

            _faceExitTransitionCoroutine = null;
            _faceExitTransitionRunning = false;
            _faceExitTransitionStartTime = 0f;
            _faceExitTransitionDuration = 0f;
            _faceExitTransitionStartByCha.Clear();
            LogInfo(
                "[doll-mode] transition-exit cancel"
                + " source=" + reason
                + " result=canceled");
        }

        private IEnumerator FaceExitTransitionCoroutine(string reason, float duration)
        {
            float startedAt = Time.unscaledTime;
            int scannedTotal = 0;
            int changedTotal = 0;
            int failedTotal = 0;

            while (true)
            {
                float elapsed = Time.unscaledTime - startedAt;
                float weight = duration <= 0.0001f ? 1f : Mathf.Clamp01(elapsed / duration);

                int scanned;
                int changed;
                int failed;
                ApplyFaceExitTransitionStep(weight, out scanned, out changed, out failed);
                scannedTotal = scanned;
                changedTotal += changed;
                failedTotal += failed;

                if (weight >= 1f)
                {
                    break;
                }

                yield return null;
            }

            _faceExitTransitionCoroutine = null;
            _faceExitTransitionRunning = false;
            _faceExitTransitionStartTime = 0f;
            _faceExitTransitionDuration = 0f;
            _faceExitTransitionStartByCha.Clear();

            FinalizeDollModeExit(reason);
            LogInfo(
                "[doll-mode] transition-exit end"
                + " source=" + reason
                + " duration=" + duration.ToString("0.##")
                + " scanned=" + scannedTotal
                + " changed=" + changedTotal
                + " failed=" + failedTotal
                + " result=completed");
        }

        private void ApplyFaceExitTransitionStep(float weight, out int scanned, out int changed, out int failed)
        {
            scanned = 0;
            changed = 0;
            failed = 0;

            if (_faceExitTransitionStartByCha.Count <= 0 || _originalFaceExpressionByCha.Count <= 0)
            {
                return;
            }

            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindNonIndependent();
            var map = new Dictionary<int, ChaControl>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha == null)
                {
                    continue;
                }

                if (SavedActorMatches(cha)) map[cha.GetInstanceID()] = cha;
            }

            foreach (var pair in _faceExitTransitionStartByCha)
            {
                scanned++;

                ChaControl cha;
                if (!map.TryGetValue(pair.Key, out cha) || cha == null)
                {
                    continue;
                }

                FaceExpressionState fromState = pair.Value;
                FaceExpressionState toState;
                if (!_originalFaceExpressionByCha.TryGetValue(pair.Key, out toState) || toState == null)
                {
                    continue;
                }

                FaceExpressionState blended = BlendFaceExpressionState(fromState, toState, weight);
                bool patternRestored;
                bool openRestored;
                bool cheekRestored;
                bool tearsRestored;
                bool sweatRestored;
                if (TryRestoreFaceExpression(
                    cha,
                    blended,
                    out patternRestored,
                    out openRestored,
                    out cheekRestored,
                    out tearsRestored,
                    out sweatRestored))
                {
                    if (patternRestored || openRestored || cheekRestored || tearsRestored || sweatRestored)
                    {
                        changed++;
                    }
                }
                else
                {
                    failed++;
                }
            }
        }

        private void FinalizeDollModeExit(string reason)
        {
            StopFaceEnterTransition("exit-finalize:" + reason);
            StopEyeFreezeEndOfFrameLoop();
            RestoreOriginalHighlightState(reason);
            _dollModeApplied = false;
            _cachedFixationalBehaviours.Clear();
            _cachedFixationalIds.Clear();
            _nextFixationalRescanAt = 0f;
            _lastFixationalRescanCount = -1;
            _lastFixationalFallbackUsed = false;
            _cachedAhegaoBehaviours.Clear();
            _cachedAhegaoIds.Clear();
            _nextAhegaoRescanAt = 0f;
            _lastAhegaoRescanCount = -1;
            _lastAhegaoKeywordFingerprint = string.Empty;
            _lastAhegaoGuardPatchReady = false;
            _hasAhegaoGuardPatchState = false;
            ResetEyeTraceState();
            _nextLateUpdateEyeLookOnlyLogAt = 0f;
            _suppressedLateUpdateEyeLookOnlyCount = 0;
            _lastPreCullFreezeFrame = -1;
            _nextPreCullFreezeLogAt = 0f;
            _suppressedPreCullFreezeLogs = 0;
            _nextMotionLockLogAt = 0f;
            _lastMotionLockLane = string.Empty;
            _lastMotionLockState = string.Empty;
            _lastMotionLockValue = float.NaN;
            ResetDollModeEyeOverlayPngCache();
            LogInfo("[doll-mode] exit source=" + reason + " eventBlock=disabled");
        }

        private void StartEyeFreezeEndOfFrameLoop()
        {
            if (_eyeFreezeEndOfFrameCoroutine != null)
            {
                return;
            }

            _eyeFreezeEndOfFrameCoroutine = StartCoroutine(EyeFreezeEndOfFrameLoop());
            LogInfo("[doll-mode] eye-freeze-eof loop=start result=ok");
        }

        private void StopEyeFreezeEndOfFrameLoop()
        {
            if (_eyeFreezeEndOfFrameCoroutine == null)
            {
                return;
            }

            try
            {
                StopCoroutine(_eyeFreezeEndOfFrameCoroutine);
            }
            catch
            {
                // ignore stop errors during shutdown
            }

            _eyeFreezeEndOfFrameCoroutine = null;
            LogInfo("[doll-mode] eye-freeze-eof loop=stop result=ok");
        }

        private void OnCameraPreCull(Camera cam)
        {
            if (!BlankRoomGate.IsInBlankRoom || _settings == null || !_settings.Enabled || !_settings.DollModeEnabled)
            {
                return;
            }

            int frame = Time.frameCount;
            if (_lastPreCullFreezeFrame == frame)
            {
                return;
            }

            _lastPreCullFreezeFrame = frame;

            int scanned;
            int changedEyeLookStop;
            int changedEyeRotation;
            int changedEyeTexOffset;
            int changedFaceCheekForce;
            int failedFaceCheekForce;
            int failed;
            ApplyEyeFreezeCore(
                "pre-cull",
                out scanned,
                out changedEyeLookStop,
                out changedEyeRotation,
                out changedEyeTexOffset,
                out changedFaceCheekForce,
                out failedFaceCheekForce,
                out failed);

            bool hasMutation =
                changedEyeLookStop > 0
                || changedEyeRotation > 0
                || changedEyeTexOffset > 0
                || changedFaceCheekForce > 0
                || failedFaceCheekForce > 0
                || failed > 0;
            bool verbose = _settings.VerboseLog;
            if (!hasMutation && !verbose)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!verbose && now < _nextPreCullFreezeLogAt)
            {
                _suppressedPreCullFreezeLogs++;
                return;
            }

            int suppressed = _suppressedPreCullFreezeLogs;
            _suppressedPreCullFreezeLogs = 0;
            _nextPreCullFreezeLogAt = now + PreCullFreezeLogIntervalSeconds;
            string cameraName = cam != null ? cam.name : "(null)";

            LogInfo(
                "[doll-mode] eye-freeze-pre-cull"
                + " frame=" + frame
                + " t=" + now.ToString("0.###")
                + " camera=" + cameraName
                + " scanned=" + scanned
                + " changedEyeLookStop=" + changedEyeLookStop
                + " changedEyeRotation=" + changedEyeRotation
                + " changedEyeTexOffset=" + changedEyeTexOffset
                + " changedFaceCheekForce=" + changedFaceCheekForce
                + " failedFaceCheekForce=" + failedFaceCheekForce
                + " failed=" + failed
                + " trackedEyeLook=" + _originalEyeLookByCha.Count
                + " trackedEyeRotation=" + _originalEyeRotationByCha.Count
                + " trackedEyeTexOffset=" + _originalEyeTextureOffsetByCha.Count
                + " suppressed=" + suppressed
                + " result=ok");
        }

        private void ApplyEyeFreezeCore(
            string signalSuffix,
            out int scanned,
            out int changedEyeLookStop,
            out int changedEyeRotation,
            out int changedEyeTexOffset,
            out int changedFaceCheekForce,
            out int failedFaceCheekForce,
            out int failed)
        {
            scanned = 0;
            changedEyeLookStop = 0;
            changedEyeRotation = 0;
            changedEyeTexOffset = 0;
            changedFaceCheekForce = 0;
            failedFaceCheekForce = 0;
            failed = 0;
            float desiredCheekRate = Mathf.Clamp01(_settings != null ? _settings.DollModeCheekRate : 0f);

            ChaControl[] all;
            try
            {
                all = GetActiveCharacters();
            }
            catch (Exception ex)
            {
                failed = 1;
                LogWarn("[doll-mode] eye-freeze scan failed source=" + signalSuffix + " error=" + ex.Message);
                return;
            }

            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (!IsTargetCharacter(cha))
                {
                    continue;
                }

                scanned++;
                int key = cha.GetInstanceID();
                if (!_originalEyeLookByCha.ContainsKey(key))
                {
                    RememberActor(cha);
                    _originalEyeLookByCha[key] = CaptureEyeLookState(cha);
                }

                if (!_originalEyeRotationByCha.ContainsKey(key))
                {
                    RememberActor(cha);
                    _originalEyeRotationByCha[key] = CaptureEyeRotationState(cha);
                }

                if (!_originalEyeTextureOffsetByCha.ContainsKey(key))
                {
                    RememberActor(cha);
                    _originalEyeTextureOffsetByCha[key] = CaptureEyeTextureOffsetState(cha);
                }

                bool eyeLookStopped;
                if (TryStopEyeLook(cha, out eyeLookStopped))
                {
                    if (eyeLookStopped)
                    {
                        changedEyeLookStop++;
                        RecordEyeTraceSignal("internal-write", "StopEyeLook(" + signalSuffix + ")");
                    }
                }
                else
                {
                    failed++;
                }

                EyeRotationState desiredEyeRotation;
                if (_originalEyeRotationByCha.TryGetValue(key, out desiredEyeRotation))
                {
                    bool eyeRotationChanged;
                    if (TryApplyEyeRotationFreeze(desiredEyeRotation, out eyeRotationChanged))
                    {
                        if (eyeRotationChanged)
                        {
                            changedEyeRotation++;
                            RecordEyeTraceSignal("internal-write", "ApplyEyeRotationFreeze(" + signalSuffix + ")");
                        }
                    }
                    else
                    {
                        failed++;
                    }
                }

                EyeTextureOffsetState desiredEyeTextureOffset;
                if (_originalEyeTextureOffsetByCha.TryGetValue(key, out desiredEyeTextureOffset))
                {
                    bool eyeTexOffsetChanged;
                    if (TryApplyEyeTextureOffsetFreeze(desiredEyeTextureOffset, out eyeTexOffsetChanged))
                    {
                        if (eyeTexOffsetChanged)
                        {
                            changedEyeTexOffset++;
                            RecordEyeTraceSignal("internal-write", "ApplyEyeTexOffsetFreeze(" + signalSuffix + ")");
                        }
                    }
                    else
                    {
                        failed++;
                    }
                }

                // Always re-apply cheek every pass; material state can be overwritten even when fileStatus value is unchanged.
                try
                {
                    float cheekBefore = ReadCurrentFaceCheekRate(cha);
                    using (BeginInternalFaceWrite())
                    {
                        cha.ChangeHohoAkaRate(desiredCheekRate);
                    }

                    float cheekAfter = ReadCurrentFaceCheekRate(cha);
                    if (!AreRatesEqual(cheekAfter, desiredCheekRate))
                    {
                        failedFaceCheekForce++;
                    }
                    else if (!AreRatesEqual(cheekBefore, desiredCheekRate))
                    {
                        changedFaceCheekForce++;
                        RecordEyeTraceSignal("internal-write", "ForceFaceCheek(" + signalSuffix + ")");
                    }
                }
                catch (Exception ex)
                {
                    failedFaceCheekForce++;
                    LogWarn("[doll-mode] cheek-force failed source=" + signalSuffix + " error=" + ex.Message);
                }
            }
        }

        private IEnumerator EyeFreezeEndOfFrameLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;

                if (_settings == null || !_settings.Enabled || !_settings.DollModeEnabled)
                {
                    continue;
                }

                ProbeEyeStateTransitions("eof-pre");

                int scanned;
                int changedEyeLookStop;
                int changed;
                int changedEyeTexOffset;
                int changedFaceCheekForce;
                int failedFaceCheekForce;
                int failed;
                ApplyEyeFreezeCore(
                    "eof",
                    out scanned,
                    out changedEyeLookStop,
                    out changed,
                    out changedEyeTexOffset,
                    out changedFaceCheekForce,
                    out failedFaceCheekForce,
                    out failed);

                if (changed > 0
                    || changedEyeLookStop > 0
                    || changedEyeTexOffset > 0
                    || changedFaceCheekForce > 0
                    || failedFaceCheekForce > 0
                    || failed > 0
                    || (_settings != null && _settings.VerboseLog))
                {
                    LogInfo(
                        "[doll-mode] eye-freeze-eof"
                        + " scanned=" + scanned
                        + " changedEyeLookStop=" + changedEyeLookStop
                        + " changed=" + changed
                        + " changedEyeTexOffset=" + changedEyeTexOffset
                        + " changedFaceCheekForce=" + changedFaceCheekForce
                        + " failedFaceCheekForce=" + failedFaceCheekForce
                        + " failed=" + failed
                        + " trackedEyeLook=" + _originalEyeLookByCha.Count
                        + " trackedEyeRotation=" + _originalEyeRotationByCha.Count
                        + " trackedEyeTexOffset=" + _originalEyeTextureOffsetByCha.Count
                        + " result=ok");
                }

                ProbeEyeStateTransitions("eof-post");
            }
        }

        private void ApplyHideHighlightToTargets(string source, bool logAlways)
        {
            ChaControl[] all = GetActiveCharacters(logAlways);
            int scanned = 0;
            int targets = 0;
            int trackedNew = 0;
            int changedHighlight = 0;
            int alreadyHidden = 0;
            int failedHighlight = 0;
            int changedBlink = 0;
            int alreadyBlinkOff = 0;
            int failedBlink = 0;
            int changedEyeLookStop = 0;
            int alreadyEyeLookStopped = 0;
            int failedEyeLookStop = 0;
            int changedEyeRotation = 0;
            int alreadyEyeRotation = 0;
            int failedEyeRotation = 0;
            int changedEyeTexOffset = 0;
            int alreadyEyeTexOffset = 0;
            int failedEyeTexOffset = 0;
            int changedFacePattern = 0;
            int alreadyFacePattern = 0;
            int failedFacePattern = 0;
            int changedFaceCheek = 0;
            int alreadyFaceCheek = 0;
            int failedFaceCheek = 0;
            int changedFaceTears = 0;
            int alreadyFaceTears = 0;
            int failedFaceTears = 0;
            int changedFaceSweat = 0;
            int alreadyFaceSweat = 0;
            int failedFaceSweat = 0;
            int changedFaceOpen = 0;
            int alreadyFaceOpen = 0;
            int failedFaceOpen = 0;
            int changedEyeOverlay = 0;
            int alreadyEyeOverlay = 0;
            int failedEyeOverlay = 0;
            int fixationalScanned = 0;
            int fixationalTargets = 0;
            int changedFixationalDisable = 0;
            int alreadyFixationalDisabled = 0;
            int failedFixationalDisable = 0;
            int ahegaoScanned = 0;
            int ahegaoTargets = 0;
            int changedAhegaoDisable = 0;
            int alreadyAhegaoDisabled = 0;
            int failedAhegaoDisable = 0;

            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                scanned++;
                if (!IsTargetCharacter(cha))
                {
                    continue;
                }

                targets++;
                int key = cha.GetInstanceID();
                if (!_originalHideHighlightByCha.ContainsKey(key))
                {
                    RememberActor(cha);
                    _originalHideHighlightByCha[key] = ReadCurrentHideHighlight(cha);
                    trackedNew++;
                }
                if (!_originalEyesBlinkByCha.ContainsKey(key))
                {
                    RememberActor(cha);
                    _originalEyesBlinkByCha[key] = ReadCurrentEyesBlink(cha);
                }
                if (!_originalEyeLookByCha.ContainsKey(key))
                {
                    RememberActor(cha);
                    _originalEyeLookByCha[key] = CaptureEyeLookState(cha);
                }
                if (!_originalEyeRotationByCha.ContainsKey(key))
                {
                    RememberActor(cha);
                    _originalEyeRotationByCha[key] = CaptureEyeRotationState(cha);
                }
                if (!_originalEyeTextureOffsetByCha.ContainsKey(key))
                {
                    RememberActor(cha);
                    _originalEyeTextureOffsetByCha[key] = CaptureEyeTextureOffsetState(cha);
                }
                if (!_originalFaceExpressionByCha.ContainsKey(key))
                {
                    RememberActor(cha);
                    _originalFaceExpressionByCha[key] = CaptureFaceExpressionState(cha);
                }
                if (!_originalEyeOverlayByCha.ContainsKey(key))
                {
                    RememberActor(cha);
                    _originalEyeOverlayByCha[key] = new EyeOverlayState();
                }

                bool currentHide = ReadCurrentHideHighlight(cha);
                if (currentHide)
                {
                    alreadyHidden++;
                }
                else
                {
                    try
                    {
                        cha.HideEyeHighlight(true);
                        changedHighlight++;
                        RecordEyeTraceSignal("internal-write", "ChaControl.HideEyeHighlight(true)");
                    }
                    catch (Exception ex)
                    {
                        failedHighlight++;
                        LogWarn("[doll-mode] apply highlight failed: " + ex.Message);
                    }
                }

                bool currentBlink = ReadCurrentEyesBlink(cha);
                if (!currentBlink)
                {
                    alreadyBlinkOff++;
                }
                else
                {
                    try
                    {
                        cha.ChangeEyesBlinkFlag(false);
                        changedBlink++;
                        RecordEyeTraceSignal("internal-write", "ChaControl.ChangeEyesBlinkFlag(false)");
                    }
                    catch (Exception ex)
                    {
                        failedBlink++;
                        LogWarn("[doll-mode] apply blink failed: " + ex.Message);
                    }
                }

                bool eyeLookStopped;
                if (TryStopEyeLook(cha, out eyeLookStopped))
                {
                    if (eyeLookStopped)
                    {
                        changedEyeLookStop++;
                        RecordEyeTraceSignal("internal-write", "StopEyeLook");
                    }
                    else
                    {
                        alreadyEyeLookStopped++;
                    }
                }
                else
                {
                    failedEyeLookStop++;
                }

                EyeRotationState desiredEyeRotation;
                if (_originalEyeRotationByCha.TryGetValue(key, out desiredEyeRotation))
                {
                    bool eyeRotationChanged;
                    if (TryApplyEyeRotationFreeze(desiredEyeRotation, out eyeRotationChanged))
                    {
                        if (eyeRotationChanged)
                        {
                            changedEyeRotation++;
                            RecordEyeTraceSignal("internal-write", "ApplyEyeRotationFreeze");
                        }
                        else
                        {
                            alreadyEyeRotation++;
                        }
                    }
                    else
                    {
                        failedEyeRotation++;
                    }
                }

                EyeTextureOffsetState desiredEyeTextureOffset;
                if (_originalEyeTextureOffsetByCha.TryGetValue(key, out desiredEyeTextureOffset))
                {
                    bool eyeTexOffsetChanged;
                    if (TryApplyEyeTextureOffsetFreeze(desiredEyeTextureOffset, out eyeTexOffsetChanged))
                    {
                        if (eyeTexOffsetChanged)
                        {
                            changedEyeTexOffset++;
                            RecordEyeTraceSignal("internal-write", "ApplyEyeTexOffsetFreeze");
                        }
                        else
                        {
                            alreadyEyeTexOffset++;
                        }
                    }
                    else
                    {
                        failedEyeTexOffset++;
                    }
                }

                bool facePatternChanged;
                bool faceCheekChanged;
                bool faceTearsChanged;
                bool faceSweatChanged;
                bool faceOpenChanged;
                if (TryApplyDollFaceExpression(
                    cha,
                    out facePatternChanged,
                    out faceOpenChanged,
                    out faceCheekChanged,
                    out faceTearsChanged,
                    out faceSweatChanged))
                {
                    if (facePatternChanged)
                    {
                        changedFacePattern++;
                        RecordEyeTraceSignal("internal-write", "ApplyFacePattern");
                    }
                    else
                    {
                        alreadyFacePattern++;
                    }

                    if (faceCheekChanged)
                    {
                        changedFaceCheek++;
                        RecordEyeTraceSignal("internal-write", "ApplyFaceCheek");
                    }
                    else
                    {
                        alreadyFaceCheek++;
                    }

                    if (faceSweatChanged)
                    {
                        changedFaceSweat++;
                        RecordEyeTraceSignal("internal-write", "ApplyFaceSweat");
                    }
                    else
                    {
                        alreadyFaceSweat++;
                    }

                    if (faceTearsChanged)
                    {
                        changedFaceTears++;
                        RecordEyeTraceSignal("internal-write", "ApplyFaceTears");
                    }
                    else
                    {
                        alreadyFaceTears++;
                    }

                    if (faceOpenChanged)
                    {
                        changedFaceOpen++;
                        RecordEyeTraceSignal("internal-write", "ApplyFaceOpen");
                    }
                    else
                    {
                        alreadyFaceOpen++;
                    }
                }
                else
                {
                    failedFacePattern++;
                    failedFaceCheek++;
                    failedFaceTears++;
                    failedFaceSweat++;
                    failedFaceOpen++;
                }

                bool overlayChanged;
                bool overlayAlready;
                if (TryApplyOrMaintainEyeOverlay(cha, key, out overlayChanged, out overlayAlready))
                {
                    if (overlayChanged)
                    {
                        changedEyeOverlay++;
                    }
                    else if (overlayAlready)
                    {
                        alreadyEyeOverlay++;
                    }
                }
                else
                {
                    failedEyeOverlay++;
                }
            }

            TryDisableFixationalComponents(
                out fixationalScanned,
                out fixationalTargets,
                out changedFixationalDisable,
                out alreadyFixationalDisabled,
                out failedFixationalDisable);
            TryDisableAhegaoComponents(
                out ahegaoScanned,
                out ahegaoTargets,
                out changedAhegaoDisable,
                out alreadyAhegaoDisabled,
                out failedAhegaoDisable);

            bool onlyEyeLookStopReapply =
                changedEyeLookStop > 0
                && changedHighlight == 0
                && changedBlink == 0
                && changedEyeRotation == 0
                && changedEyeTexOffset == 0
                && changedFacePattern == 0
                && changedFaceCheek == 0
                && changedFaceTears == 0
                && changedFaceSweat == 0
                && changedFaceOpen == 0
                && changedEyeOverlay == 0
                && changedFixationalDisable == 0
                && changedAhegaoDisable == 0
                && failedHighlight == 0
                && failedBlink == 0
                && failedEyeLookStop == 0
                && failedEyeRotation == 0
                && failedEyeTexOffset == 0
                && failedFacePattern == 0
                && failedFaceCheek == 0
                && failedFaceTears == 0
                && failedFaceSweat == 0
                && failedFaceOpen == 0
                && failedEyeOverlay == 0
                && failedFixationalDisable == 0
                && failedAhegaoDisable == 0;

            bool suppressOnlyEyeLookReapplyLog =
                !logAlways
                && string.Equals(source, "late-update", StringComparison.Ordinal)
                && onlyEyeLookStopReapply
                && !(_settings != null && _settings.VerboseLog);

            int suppressedLateUpdateEyeLookOnly = 0;
            if (suppressOnlyEyeLookReapplyLog)
            {
                float now = Time.unscaledTime;
                if (now < _nextLateUpdateEyeLookOnlyLogAt)
                {
                    _suppressedLateUpdateEyeLookOnlyCount++;
                    return;
                }

                suppressedLateUpdateEyeLookOnly = _suppressedLateUpdateEyeLookOnlyCount;
                _suppressedLateUpdateEyeLookOnlyCount = 0;
                _nextLateUpdateEyeLookOnlyLogAt = now + LateUpdateEyeLookOnlyLogIntervalSeconds;
            }
            else
            {
                _suppressedLateUpdateEyeLookOnlyCount = 0;
            }

            if (logAlways
                || changedHighlight > 0
                || changedBlink > 0
                || changedEyeLookStop > 0
                || changedEyeRotation > 0
                || changedEyeTexOffset > 0
                || changedFacePattern > 0
                || changedFaceCheek > 0
                || changedFaceTears > 0
                || changedFaceSweat > 0
                || changedFaceOpen > 0
                || changedEyeOverlay > 0
                || changedFixationalDisable > 0
                || changedAhegaoDisable > 0
                || (_settings != null && _settings.VerboseLog))
            {
                float now = Time.unscaledTime;
                LogInfo(
                    "[doll-mode] apply"
                    + " frame=" + Time.frameCount
                    + " t=" + now.ToString("0.###")
                    + " source=" + source
                    + " scanned=" + scanned
                    + " targets=" + targets
                    + " trackedNew=" + trackedNew
                    + " changedHighlight=" + changedHighlight
                    + " alreadyHidden=" + alreadyHidden
                    + " failedHighlight=" + failedHighlight
                    + " changedBlinkOff=" + changedBlink
                    + " alreadyBlinkOff=" + alreadyBlinkOff
                    + " failedBlink=" + failedBlink
                    + " changedEyeLookStop=" + changedEyeLookStop
                    + " alreadyEyeLookStopped=" + alreadyEyeLookStopped
                    + " failedEyeLookStop=" + failedEyeLookStop
                    + " changedEyeRotation=" + changedEyeRotation
                    + " alreadyEyeRotation=" + alreadyEyeRotation
                    + " failedEyeRotation=" + failedEyeRotation
                    + " changedEyeTexOffset=" + changedEyeTexOffset
                    + " alreadyEyeTexOffset=" + alreadyEyeTexOffset
                    + " failedEyeTexOffset=" + failedEyeTexOffset
                    + " changedFacePattern=" + changedFacePattern
                    + " alreadyFacePattern=" + alreadyFacePattern
                    + " failedFacePattern=" + failedFacePattern
                    + " changedFaceCheek=" + changedFaceCheek
                    + " alreadyFaceCheek=" + alreadyFaceCheek
                    + " failedFaceCheek=" + failedFaceCheek
                    + " changedFaceTears=" + changedFaceTears
                    + " alreadyFaceTears=" + alreadyFaceTears
                    + " failedFaceTears=" + failedFaceTears
                    + " changedFaceSweat=" + changedFaceSweat
                    + " alreadyFaceSweat=" + alreadyFaceSweat
                    + " failedFaceSweat=" + failedFaceSweat
                    + " changedFaceOpen=" + changedFaceOpen
                    + " alreadyFaceOpen=" + alreadyFaceOpen
                    + " failedFaceOpen=" + failedFaceOpen
                    + " changedEyeOverlay=" + changedEyeOverlay
                    + " alreadyEyeOverlay=" + alreadyEyeOverlay
                    + " failedEyeOverlay=" + failedEyeOverlay
                    + " fixationalScanned=" + fixationalScanned
                    + " fixationalTargets=" + fixationalTargets
                    + " changedFixationalDisable=" + changedFixationalDisable
                    + " alreadyFixationalDisabled=" + alreadyFixationalDisabled
                    + " failedFixationalDisable=" + failedFixationalDisable
                    + " ahegaoScanned=" + ahegaoScanned
                    + " ahegaoTargets=" + ahegaoTargets
                    + " changedAhegaoDisable=" + changedAhegaoDisable
                    + " alreadyAhegaoDisabled=" + alreadyAhegaoDisabled
                    + " failedAhegaoDisable=" + failedAhegaoDisable
                    + " trackedHighlight=" + _originalHideHighlightByCha.Count
                    + " trackedBlink=" + _originalEyesBlinkByCha.Count
                    + " trackedEyeLook=" + _originalEyeLookByCha.Count
                    + " trackedEyeRotation=" + _originalEyeRotationByCha.Count
                    + " trackedEyeTexOffset=" + _originalEyeTextureOffsetByCha.Count
                    + " trackedFaceExpression=" + _originalFaceExpressionByCha.Count
                    + " trackedEyeOverlay=" + _originalEyeOverlayByCha.Count
                    + " trackedFixational=" + _originalFixationalEnabledByComponent.Count
                    + " trackedAhegao=" + _originalAhegaoEnabledByComponent.Count
                    + " trackedAhegaoGo=" + _originalAhegaoActiveByGameObject.Count
                    + " suppressedLateEyeLookOnly=" + suppressedLateUpdateEyeLookOnly
                    + " result=ok");
            }
        }

        private readonly Dictionary<int, ChaControl> _savedActorReferences = new Dictionary<int, ChaControl>();
        private readonly Dictionary<int, UnityEngine.SceneManagement.Scene> _savedActorScenes = new Dictionary<int, UnityEngine.SceneManagement.Scene>();
        private readonly Dictionary<int, HSceneProc> _savedActorHScenes = new Dictionary<int, HSceneProc>();
        private void RememberActor(ChaControl cha) { int id = cha.GetInstanceID(); _savedActorReferences[id] = cha; _savedActorScenes[id] = cha.gameObject.scene; _savedActorHScenes[id] = UnityEngine.Object.FindObjectOfType<HSceneProc>(); }
        private bool SavedActorMatches(ChaControl cha) {
            int id = cha.GetInstanceID();
            return _savedActorReferences.TryGetValue(id, out var actor) && ReferenceEquals(actor, cha) && _savedActorScenes.TryGetValue(id, out var scene) && scene == cha.gameObject.scene && _savedActorHScenes.TryGetValue(id, out var hScene) && (ReferenceEquals(hScene, null) || hScene != null) && !CanonPlugins.Shared.IndependentActorFilter.IsIndependent(cha);
        }

        private void RestoreOriginalHighlightState(string source)
        {
            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindNonIndependent();
            var map = new Dictionary<int, ChaControl>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha == null)
                {
                    continue;
                }

                if (SavedActorMatches(cha)) map[cha.GetInstanceID()] = cha;
            }

            var keys = new HashSet<int>(_originalHideHighlightByCha.Keys);
            foreach (int key in _originalEyesBlinkByCha.Keys)
            {
                keys.Add(key);
            }
            foreach (int key in _originalEyeLookByCha.Keys)
            {
                keys.Add(key);
            }
            foreach (int key in _originalEyeRotationByCha.Keys)
            {
                keys.Add(key);
            }
            foreach (int key in _originalEyeTextureOffsetByCha.Keys)
            {
                keys.Add(key);
            }
            foreach (int key in _originalFaceExpressionByCha.Keys)
            {
                keys.Add(key);
            }
            foreach (int key in _originalEyeOverlayByCha.Keys)
            {
                keys.Add(key);
            }

            int tracked = keys.Count;
            int restoredHighlight = 0;
            int unchangedHighlight = 0;
            int restoredBlink = 0;
            int unchangedBlink = 0;
            int restoredEyeLook = 0;
            int unchangedEyeLook = 0;
            int restoredEyeRotation = 0;
            int unchangedEyeRotation = 0;
            int restoredEyeTexOffset = 0;
            int unchangedEyeTexOffset = 0;
            int restoredFacePattern = 0;
            int unchangedFacePattern = 0;
            int restoredFaceCheek = 0;
            int unchangedFaceCheek = 0;
            int restoredFaceTears = 0;
            int unchangedFaceTears = 0;
            int restoredFaceSweat = 0;
            int unchangedFaceSweat = 0;
            int restoredFaceOpen = 0;
            int unchangedFaceOpen = 0;
            int trackedEyeOverlay = _originalEyeOverlayByCha.Count;
            int restoredEyeOverlay = 0;
            int unchangedEyeOverlay = 0;
            int failedEyeOverlay = 0;
            int missing = 0;
            int failed = 0;
            int trackedEyeRotation = _originalEyeRotationByCha.Count;
            int trackedEyeTexOffset = _originalEyeTextureOffsetByCha.Count;
            int trackedFaceExpression = _originalFaceExpressionByCha.Count;
            int trackedFixational = _originalFixationalEnabledByComponent.Count;
            int restoredFixational = 0;
            int unchangedFixational = 0;
            int missingFixational = 0;
            int failedFixational = 0;
            int trackedAhegao = _originalAhegaoEnabledByComponent.Count;
            int trackedAhegaoGo = _originalAhegaoActiveByGameObject.Count;
            int restoredAhegao = 0;
            int unchangedAhegao = 0;
            int missingAhegao = 0;
            int failedAhegao = 0;

            foreach (int key in keys)
            {
                ChaControl cha;
                if (!map.TryGetValue(key, out cha) || cha == null)
                {
                    missing++;
                    continue;
                }

                bool desiredHide;
                if (!_originalHideHighlightByCha.TryGetValue(key, out desiredHide))
                {
                    desiredHide = ReadCurrentHideHighlight(cha);
                }

                bool desiredBlink;
                if (!_originalEyesBlinkByCha.TryGetValue(key, out desiredBlink))
                {
                    desiredBlink = ReadCurrentEyesBlink(cha);
                }

                bool currentHide = ReadCurrentHideHighlight(cha);
                if (currentHide != desiredHide)
                {
                    try
                    {
                        cha.HideEyeHighlight(desiredHide);
                        restoredHighlight++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        LogWarn("[doll-mode] restore highlight failed: " + ex.Message);
                    }
                }
                else
                {
                    unchangedHighlight++;
                }

                bool currentBlink = ReadCurrentEyesBlink(cha);
                if (currentBlink != desiredBlink)
                {
                    try
                    {
                        cha.ChangeEyesBlinkFlag(desiredBlink);
                        restoredBlink++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        LogWarn("[doll-mode] restore blink failed: " + ex.Message);
                    }
                }
                else
                {
                    unchangedBlink++;
                }

                EyeLookState desiredEyeLook;
                if (_originalEyeLookByCha.TryGetValue(key, out desiredEyeLook))
                {
                    bool eyeLookRestored;
                    if (TryRestoreEyeLook(cha, desiredEyeLook, out eyeLookRestored))
                    {
                        if (eyeLookRestored)
                        {
                            restoredEyeLook++;
                        }
                        else
                        {
                            unchangedEyeLook++;
                        }
                    }
                    else
                    {
                        failed++;
                    }
                }

                EyeRotationState desiredEyeRotation;
                if (_originalEyeRotationByCha.TryGetValue(key, out desiredEyeRotation))
                {
                    bool eyeRotationRestored;
                    if (TryRestoreEyeRotation(desiredEyeRotation, out eyeRotationRestored))
                    {
                        if (eyeRotationRestored)
                        {
                            restoredEyeRotation++;
                        }
                        else
                        {
                            unchangedEyeRotation++;
                        }
                    }
                    else
                    {
                        failed++;
                    }
                }

                EyeTextureOffsetState desiredEyeTextureOffset;
                if (_originalEyeTextureOffsetByCha.TryGetValue(key, out desiredEyeTextureOffset))
                {
                    bool eyeTexOffsetRestored;
                    if (TryRestoreEyeTextureOffset(desiredEyeTextureOffset, out eyeTexOffsetRestored))
                    {
                        if (eyeTexOffsetRestored)
                        {
                            restoredEyeTexOffset++;
                        }
                        else
                        {
                            unchangedEyeTexOffset++;
                        }
                    }
                    else
                    {
                        failed++;
                    }
                }

                FaceExpressionState desiredFaceExpression;
                if (_originalFaceExpressionByCha.TryGetValue(key, out desiredFaceExpression))
                {
                    bool facePatternRestored;
                    bool faceCheekRestored;
                    bool faceTearsRestored;
                    bool faceSweatRestored;
                    bool faceOpenRestored;
                    if (TryRestoreFaceExpression(
                        cha,
                        desiredFaceExpression,
                        out facePatternRestored,
                        out faceOpenRestored,
                        out faceCheekRestored,
                        out faceTearsRestored,
                        out faceSweatRestored))
                    {
                        if (facePatternRestored)
                        {
                            restoredFacePattern++;
                        }
                        else
                        {
                            unchangedFacePattern++;
                        }

                        if (faceCheekRestored)
                        {
                            restoredFaceCheek++;
                        }
                        else
                        {
                            unchangedFaceCheek++;
                        }

                        if (faceSweatRestored)
                        {
                            restoredFaceSweat++;
                        }
                        else
                        {
                            unchangedFaceSweat++;
                        }

                        if (faceTearsRestored)
                        {
                            restoredFaceTears++;
                        }
                        else
                        {
                            unchangedFaceTears++;
                        }

                        if (faceOpenRestored)
                        {
                            restoredFaceOpen++;
                        }
                        else
                        {
                            unchangedFaceOpen++;
                        }
                    }
                    else
                    {
                        failed++;
                    }
                }

                EyeOverlayState desiredEyeOverlay;
                if (_originalEyeOverlayByCha.TryGetValue(key, out desiredEyeOverlay))
                {
                    bool overlayRestored;
                    if (TryRestoreEyeOverlay(cha, desiredEyeOverlay, out overlayRestored))
                    {
                        if (overlayRestored)
                        {
                            restoredEyeOverlay++;
                        }
                        else
                        {
                            unchangedEyeOverlay++;
                        }
                    }
                    else
                    {
                        failedEyeOverlay++;
                        failed++;
                    }
                }
            }

            RestoreFixationalComponents(
                out restoredFixational,
                out unchangedFixational,
                out missingFixational,
                out failedFixational);
            failed += failedFixational;
            RestoreAhegaoComponents(
                out restoredAhegao,
                out unchangedAhegao,
                out missingAhegao,
                out failedAhegao);
            failed += failedAhegao;

            _savedActorReferences.Clear(); _savedActorScenes.Clear(); _savedActorHScenes.Clear();
            _originalHideHighlightByCha.Clear();
            _originalEyesBlinkByCha.Clear();
            _originalEyeLookByCha.Clear();
            _originalEyeRotationByCha.Clear();
            _originalEyeTextureOffsetByCha.Clear();
            _originalFaceExpressionByCha.Clear();
            _originalEyeOverlayByCha.Clear();
            _originalFixationalEnabledByComponent.Clear();
            _originalAhegaoEnabledByComponent.Clear();
            _originalAhegaoActiveByGameObject.Clear();
            LogInfo(
                "[doll-mode] restore"
                + " source=" + source
                + " tracked=" + tracked
                + " restoredHighlight=" + restoredHighlight
                + " unchangedHighlight=" + unchangedHighlight
                + " restoredBlink=" + restoredBlink
                + " unchangedBlink=" + unchangedBlink
                + " restoredEyeLook=" + restoredEyeLook
                + " unchangedEyeLook=" + unchangedEyeLook
                + " trackedEyeRotation=" + trackedEyeRotation
                + " restoredEyeRotation=" + restoredEyeRotation
                + " unchangedEyeRotation=" + unchangedEyeRotation
                + " trackedEyeTexOffset=" + trackedEyeTexOffset
                + " restoredEyeTexOffset=" + restoredEyeTexOffset
                + " unchangedEyeTexOffset=" + unchangedEyeTexOffset
                + " trackedFaceExpression=" + trackedFaceExpression
                + " restoredFacePattern=" + restoredFacePattern
                + " unchangedFacePattern=" + unchangedFacePattern
                + " restoredFaceCheek=" + restoredFaceCheek
                + " unchangedFaceCheek=" + unchangedFaceCheek
                + " restoredFaceTears=" + restoredFaceTears
                + " unchangedFaceTears=" + unchangedFaceTears
                + " restoredFaceSweat=" + restoredFaceSweat
                + " unchangedFaceSweat=" + unchangedFaceSweat
                + " restoredFaceOpen=" + restoredFaceOpen
                + " unchangedFaceOpen=" + unchangedFaceOpen
                + " trackedEyeOverlay=" + trackedEyeOverlay
                + " restoredEyeOverlay=" + restoredEyeOverlay
                + " unchangedEyeOverlay=" + unchangedEyeOverlay
                + " failedEyeOverlay=" + failedEyeOverlay
                + " trackedFixational=" + trackedFixational
                + " restoredFixational=" + restoredFixational
                + " unchangedFixational=" + unchangedFixational
                + " missingFixational=" + missingFixational
                + " failedFixational=" + failedFixational
                + " trackedAhegao=" + trackedAhegao
                + " trackedAhegaoGo=" + trackedAhegaoGo
                + " restoredAhegao=" + restoredAhegao
                + " unchangedAhegao=" + unchangedAhegao
                + " missingAhegao=" + missingAhegao
                + " failedAhegao=" + failedAhegao
                + " missing=" + missing
                + " failed=" + failed
                + " result=ok");
        }

        private ChaControl[] GetActiveCharacters(bool refresh = false)
        {
            float now = Time.unscaledTime;
            if (refresh || _cachedActiveCharacters == null || now >= _nextActiveCharacterScanAt)
            {
                _cachedActiveCharacters = CanonPlugins.Shared.IndependentActorFilter.FindNonIndependent();
                _nextActiveCharacterScanAt = now + 0.25f;
            }
            return _cachedActiveCharacters;
        }

        private bool IsTargetCharacter(ChaControl cha)
        {
            if (CanonPlugins.Shared.IndependentActorFilter.IsIndependent(cha)) return false;
            if (cha == null)
            {
                return false;
            }

            bool isFemale = cha.sex == 1;
            bool isMale = cha.sex == 0;

            if (isFemale && _settings.ApplyFemaleCharacters)
            {
                return true;
            }

            if (isMale && _settings.ApplyMaleCharacters)
            {
                return true;
            }

            return false;
        }

        private void ResetEyeOverlayApiCache()
        {
            _eyeOverlayApiResolved = false;
            _eyeOverlayApiAvailable = false;
            _eyeOverlayApiError = string.Empty;
            _eyeOverlayControllerType = null;
            _eyeOverlayTexType = null;
            _eyeOverlaySetOverlayTexMethod = null;
            _eyeOverlayStorageProperty = null;
            _eyeOverlayStorageGetTextureMethod = null;
            _eyeOverlayTexTypeEyeOverL = null;
            _eyeOverlayTexTypeEyeOverR = null;
        }

        private void ResetDollModeEyeOverlayPngCache()
        {
            _dollModeEyeOverlayBytesLoaded = false;
            _dollModeEyeOverlayPngBytes = null;
            _dollModeEyeOverlayResolvedPath = string.Empty;
            _dollModeEyeOverlayLoadError = string.Empty;
        }

        private bool ShouldUseDollModeEyeOverlay()
        {
            return _settings != null
                && _settings.DollModeEyeOverlayEnabled
                && !string.IsNullOrWhiteSpace(_settings.DollModeEyeOverlayPngPath);
        }

        private bool EnsureDollModeEyeOverlayPngLoaded(out byte[] pngBytes, out string resolvedPath)
        {
            pngBytes = null;
            resolvedPath = string.Empty;

            if (!ShouldUseDollModeEyeOverlay())
            {
                return false;
            }

            if (_dollModeEyeOverlayBytesLoaded)
            {
                pngBytes = _dollModeEyeOverlayPngBytes;
                resolvedPath = _dollModeEyeOverlayResolvedPath;
                return pngBytes != null;
            }

            _dollModeEyeOverlayBytesLoaded = true;
            string path = ResolveEyeOverlayPngPath(_settings.DollModeEyeOverlayPngPath);
            resolvedPath = path;
            _dollModeEyeOverlayResolvedPath = path;

            if (string.IsNullOrWhiteSpace(path))
            {
                _dollModeEyeOverlayLoadError = "path_empty";
                LogWarn("[doll-mode][eye-overlay] png load failed: path is empty");
                return false;
            }

            if (!File.Exists(path))
            {
                _dollModeEyeOverlayLoadError = "file_not_found";
                LogWarn("[doll-mode][eye-overlay] png load failed: file not found path=" + path);
                return false;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0)
                {
                    _dollModeEyeOverlayLoadError = "file_empty";
                    LogWarn("[doll-mode][eye-overlay] png load failed: file is empty path=" + path);
                    return false;
                }

                _dollModeEyeOverlayPngBytes = bytes;
                _dollModeEyeOverlayLoadError = string.Empty;
                pngBytes = bytes;
                LogInfo(
                    "[doll-mode][eye-overlay] png loaded"
                    + " path=" + path
                    + " bytes=" + bytes.Length
                    + " result=ok");
                return true;
            }
            catch (Exception ex)
            {
                _dollModeEyeOverlayLoadError = ex.Message;
                LogWarn("[doll-mode][eye-overlay] png load failed path=" + path + " error=" + ex.Message);
                return false;
            }
        }

        private string ResolveEyeOverlayPngPath(string rawPath)
        {
            string normalized = (rawPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            try
            {
                if (Path.IsPathRooted(normalized))
                {
                    return Path.GetFullPath(normalized);
                }

                return Path.GetFullPath(Path.Combine(_pluginDir ?? string.Empty, normalized));
            }
            catch
            {
                return normalized;
            }
        }

        private bool TryResolveEyeOverlayApi()
        {
            if (_eyeOverlayApiResolved)
            {
                return _eyeOverlayApiAvailable;
            }

            _eyeOverlayApiResolved = true;
            _eyeOverlayApiAvailable = false;
            _eyeOverlayApiError = string.Empty;

            Type controllerType = TryFindTypeByFullNameFast("KoiSkinOverlayX.KoiSkinOverlayController");
            Type texType = TryFindTypeByFullNameFast("KoiSkinOverlayX.TexType");
            if (controllerType == null || texType == null)
            {
                _eyeOverlayApiError = "KSOX type not found";
                LogWarn("[doll-mode][eye-overlay] api unavailable: " + _eyeOverlayApiError);
                return false;
            }

            MethodInfo setOverlayTex = controllerType.GetMethod(
                "SetOverlayTex",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(byte[]), texType },
                null);
            PropertyInfo overlayStorage = controllerType.GetProperty(
                "OverlayStorage",
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo getTexture = overlayStorage != null
                ? overlayStorage.PropertyType.GetMethod(
                    "GetTexture",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { texType },
                    null)
                : null;

            if (setOverlayTex == null || overlayStorage == null || getTexture == null)
            {
                _eyeOverlayApiError = "KSOX method/property not found";
                LogWarn("[doll-mode][eye-overlay] api unavailable: " + _eyeOverlayApiError);
                return false;
            }

            object eyeOverL;
            object eyeOverR;
            try
            {
                eyeOverL = Enum.Parse(texType, "EyeOverL");
                eyeOverR = Enum.Parse(texType, "EyeOverR");
            }
            catch (Exception ex)
            {
                _eyeOverlayApiError = "TexType parse failed: " + ex.Message;
                LogWarn("[doll-mode][eye-overlay] api unavailable: " + _eyeOverlayApiError);
                return false;
            }

            _eyeOverlayControllerType = controllerType;
            _eyeOverlayTexType = texType;
            _eyeOverlaySetOverlayTexMethod = setOverlayTex;
            _eyeOverlayStorageProperty = overlayStorage;
            _eyeOverlayStorageGetTextureMethod = getTexture;
            _eyeOverlayTexTypeEyeOverL = eyeOverL;
            _eyeOverlayTexTypeEyeOverR = eyeOverR;
            _eyeOverlayApiAvailable = true;

            LogInfo("[doll-mode][eye-overlay] api resolved result=ok");
            return true;
        }

        private static Type TryFindTypeByFullNameFast(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            Assembly[] assemblies;
            try
            {
                assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }
            catch
            {
                return null;
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly asm = assemblies[i];
                if (asm == null)
                {
                    continue;
                }

                try
                {
                    Type found = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
                    if (found != null)
                    {
                        return found;
                    }
                }
                catch
                {
                    // ignore per-assembly type lookup errors
                }
            }

            return null;
        }

        private bool TryGetEyeOverlayController(ChaControl cha, out object controller)
        {
            controller = null;
            if (cha == null || !TryResolveEyeOverlayApi() || _eyeOverlayControllerType == null)
            {
                return false;
            }

            try
            {
                controller = ((Component)cha).GetComponent(_eyeOverlayControllerType);
                return controller != null;
            }
            catch
            {
                return false;
            }
        }

        private bool TryApplyOrMaintainEyeOverlay(ChaControl cha, int key, out bool changed, out bool already)
        {
            changed = false;
            already = false;

            if (cha == null)
            {
                return true;
            }

            EyeOverlayState state;
            if (!_originalEyeOverlayByCha.TryGetValue(key, out state) || state == null)
            {
                state = new EyeOverlayState();
                RememberActor(cha);
                _originalEyeOverlayByCha[key] = state;
            }

            bool wantOverlay = ShouldUseDollModeEyeOverlay();
            byte[] overlayBytes = null;
            string overlayPath = string.Empty;
            if (wantOverlay && !EnsureDollModeEyeOverlayPngLoaded(out overlayBytes, out overlayPath))
            {
                already = true;
                return true;
            }

            object controller;
            if (!TryGetEyeOverlayController(cha, out controller))
            {
                already = true;
                return true;
            }

            if (!state.Captured)
            {
                byte[] originalLeft;
                byte[] originalRight;
                if (!TryCaptureEyeOverlayState(controller, out originalLeft, out originalRight))
                {
                    return false;
                }

                state.OriginalEyeOverLeftPng = originalLeft;
                state.OriginalEyeOverRightPng = originalRight;
                state.Captured = true;
            }

            if (wantOverlay)
            {
                if (state.Applied && string.Equals(state.AppliedPath, overlayPath, StringComparison.OrdinalIgnoreCase))
                {
                    already = true;
                    return true;
                }

                if (!TrySetEyeOverlayPng(controller, overlayBytes, overlayBytes))
                {
                    return false;
                }

                state.Applied = true;
                state.AppliedPath = overlayPath ?? string.Empty;
                changed = true;
                RecordEyeTraceSignal("internal-write", "ApplyEyeOverlay");
                return true;
            }

            if (state.Applied)
            {
                bool restored;
                if (!TryRestoreEyeOverlay(cha, state, out restored))
                {
                    return false;
                }

                if (restored)
                {
                    changed = true;
                }
                else
                {
                    already = true;
                }

                return true;
            }

            already = true;
            return true;
        }

        private bool TryRestoreEyeOverlay(ChaControl cha, EyeOverlayState state, out bool restored)
        {
            restored = false;
            if (cha == null || state == null || !state.Applied || !state.Captured)
            {
                return true;
            }

            object controller;
            if (!TryGetEyeOverlayController(cha, out controller))
            {
                return false;
            }

            if (!TrySetEyeOverlayPng(controller, state.OriginalEyeOverLeftPng, state.OriginalEyeOverRightPng))
            {
                return false;
            }

            state.Applied = false;
            state.AppliedPath = string.Empty;
            restored = true;
            RecordEyeTraceSignal("internal-write", "RestoreEyeOverlay");
            return true;
        }

        private bool TryCaptureEyeOverlayState(object controller, out byte[] leftEyeOverPng, out byte[] rightEyeOverPng)
        {
            leftEyeOverPng = null;
            rightEyeOverPng = null;

            if (controller == null || !TryResolveEyeOverlayApi())
            {
                return false;
            }

            try
            {
                object storage = _eyeOverlayStorageProperty.GetValue(controller, null);
                if (storage == null)
                {
                    return true;
                }

                Texture leftTexture = _eyeOverlayStorageGetTextureMethod.Invoke(storage, new[] { _eyeOverlayTexTypeEyeOverL }) as Texture;
                Texture rightTexture = _eyeOverlayStorageGetTextureMethod.Invoke(storage, new[] { _eyeOverlayTexTypeEyeOverR }) as Texture;

                if (!TryEncodeTextureToPng(leftTexture, out leftEyeOverPng))
                {
                    return false;
                }

                if (!TryEncodeTextureToPng(rightTexture, out rightEyeOverPng))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode][eye-overlay] capture failed: " + ex.Message);
                return false;
            }
        }

        private bool TrySetEyeOverlayPng(object controller, byte[] leftEyeOverPng, byte[] rightEyeOverPng)
        {
            if (controller == null || !TryResolveEyeOverlayApi())
            {
                return false;
            }

            try
            {
                _eyeOverlaySetOverlayTexMethod.Invoke(controller, new object[] { leftEyeOverPng, _eyeOverlayTexTypeEyeOverL });
                _eyeOverlaySetOverlayTexMethod.Invoke(controller, new object[] { rightEyeOverPng, _eyeOverlayTexTypeEyeOverR });
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode][eye-overlay] apply failed: " + ex.Message);
                return false;
            }
        }

        private bool TryEncodeTextureToPng(Texture source, out byte[] pngBytes)
        {
            pngBytes = null;
            if (source == null)
            {
                return true;
            }

            RenderTexture temp = null;
            RenderTexture previous = null;
            Texture2D readable = null;
            try
            {
                int width = Math.Max(1, source.width);
                int height = Math.Max(1, source.height);
                temp = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, temp);
                previous = RenderTexture.active;
                RenderTexture.active = temp;

                readable = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                pngBytes = ImageConversion.EncodeToPNG(readable);
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode][eye-overlay] encode failed: " + ex.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = previous;

                if (readable != null)
                {
                    Destroy(readable);
                }

                if (temp != null)
                {
                    RenderTexture.ReleaseTemporary(temp);
                }
            }
        }

        private void TryDisableFixationalComponents(
            out int scanned,
            out int targets,
            out int changed,
            out int alreadyDisabled,
            out int failed)
        {
            scanned = 0;
            targets = 0;
            changed = 0;
            alreadyDisabled = 0;
            failed = 0;

            List<MonoBehaviour> components = GetFixationalBehaviours(forceRescan: false);
            scanned = components.Count;
            for (int i = 0; i < components.Count; i++)
            {
                MonoBehaviour behaviour = components[i];
                if (!ShouldAffectFixationalBehaviour(behaviour))
                {
                    continue;
                }

                targets++;
                int key = behaviour.GetInstanceID();
                if (!_originalFixationalEnabledByComponent.ContainsKey(key))
                {
                    _originalFixationalEnabledByComponent[key] = behaviour.enabled;
                }

                if (!behaviour.enabled)
                {
                    alreadyDisabled++;
                    continue;
                }

                try
                {
                    behaviour.enabled = false;
                    changed++;
                    string point = (behaviour.GetType().FullName ?? behaviour.GetType().Name) + ".enabled";
                    RecordEyeTraceSignal("internal-write", point + "=false");
                    OnExternalPluginBlocked(point);
                }
                catch (Exception ex)
                {
                    failed++;
                    LogWarn("[doll-mode] apply fixational disable failed: " + ex.Message);
                }
            }
        }

        private void RestoreFixationalComponents(
            out int restored,
            out int unchanged,
            out int missing,
            out int failed)
        {
            restored = 0;
            unchanged = 0;
            missing = 0;
            failed = 0;

            if (_originalFixationalEnabledByComponent.Count == 0)
            {
                return;
            }

            List<MonoBehaviour> components = GetFixationalBehaviours(forceRescan: true);
            var map = new Dictionary<int, MonoBehaviour>(components.Count);
            for (int i = 0; i < components.Count; i++)
            {
                MonoBehaviour behaviour = components[i];
                if (behaviour == null)
                {
                    continue;
                }

                map[behaviour.GetInstanceID()] = behaviour;
            }

            foreach (KeyValuePair<int, bool> item in _originalFixationalEnabledByComponent)
            {
                MonoBehaviour behaviour;
                if (!map.TryGetValue(item.Key, out behaviour) || behaviour == null)
                {
                    missing++;
                    continue;
                }

                bool desiredEnabled = item.Value;
                if (behaviour.enabled == desiredEnabled)
                {
                    unchanged++;
                    continue;
                }

                try
                {
                    behaviour.enabled = desiredEnabled;
                    restored++;
                }
                catch (Exception ex)
                {
                    failed++;
                    LogWarn("[doll-mode] restore fixational failed: " + ex.Message);
                }
            }
        }

        private void TryDisableAhegaoComponents(
            out int scanned,
            out int targets,
            out int changed,
            out int alreadyDisabled,
            out int failed)
        {
            scanned = 0;
            targets = 0;
            changed = 0;
            alreadyDisabled = 0;
            failed = 0;

            if (_settings == null || !_settings.DisableAhegaoPlugins)
            {
                if (_originalAhegaoEnabledByComponent.Count > 0 || _originalAhegaoActiveByGameObject.Count > 0)
                {
                    int restored;
                    int unchanged;
                    int missing;
                    int restoreFailed;
                    RestoreAhegaoComponents(
                        out restored,
                        out unchanged,
                        out missing,
                        out restoreFailed);
                    _originalAhegaoEnabledByComponent.Clear();
                    _originalAhegaoActiveByGameObject.Clear();

                    if (restored > 0 || restoreFailed > 0)
                    {
                        LogInfo(
                            "[ahegao-block] restore-on-config"
                            + " restored=" + restored
                            + " unchanged=" + unchanged
                            + " missing=" + missing
                            + " failed=" + restoreFailed
                            + " result=ok");
                    }
                }

                return;
            }

            List<MonoBehaviour> components = GetAhegaoBehaviours(forceRescan: false);
            scanned = components.Count;
            for (int i = 0; i < components.Count; i++)
            {
                MonoBehaviour behaviour = components[i];
                if (!ShouldAffectAhegaoBehaviour(behaviour))
                {
                    continue;
                }

                targets++;
                int key = behaviour.GetInstanceID();
                if (!_originalAhegaoEnabledByComponent.ContainsKey(key))
                {
                    _originalAhegaoEnabledByComponent[key] = behaviour.enabled;
                }

                bool changedAny = false;
                string typeLabel = behaviour.GetType().FullName ?? behaviour.GetType().Name;

                if (behaviour.enabled)
                {
                    try
                    {
                        behaviour.enabled = false;
                        changedAny = true;
                        RecordEyeTraceSignal("internal-write", typeLabel + ".enabled=false");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        LogWarn("[doll-mode] apply ahegao disable failed: " + ex.Message);
                    }
                }

                if (changedAny)
                {
                    changed++;
                    OnExternalPluginBlocked(typeLabel + ".blocked");
                }
                else
                {
                    alreadyDisabled++;
                }
            }
        }

        private void RestoreAhegaoComponents(
            out int restored,
            out int unchanged,
            out int missing,
            out int failed)
        {
            restored = 0;
            unchanged = 0;
            missing = 0;
            failed = 0;

            if (_originalAhegaoEnabledByComponent.Count == 0 && _originalAhegaoActiveByGameObject.Count == 0)
            {
                return;
            }

            List<MonoBehaviour> components = GetAhegaoBehaviours(forceRescan: true);
            var map = new Dictionary<int, MonoBehaviour>(components.Count);
            for (int i = 0; i < components.Count; i++)
            {
                MonoBehaviour behaviour = components[i];
                if (behaviour == null)
                {
                    continue;
                }

                map[behaviour.GetInstanceID()] = behaviour;
            }

            foreach (KeyValuePair<int, bool> item in _originalAhegaoEnabledByComponent)
            {
                MonoBehaviour behaviour;
                if (!map.TryGetValue(item.Key, out behaviour) || behaviour == null)
                {
                    missing++;
                    continue;
                }

                bool desiredEnabled = item.Value;
                if (behaviour.enabled == desiredEnabled)
                {
                    unchanged++;
                    continue;
                }

                try
                {
                    behaviour.enabled = desiredEnabled;
                    restored++;
                }
                catch (Exception ex)
                {
                    failed++;
                    LogWarn("[doll-mode] restore ahegao failed: " + ex.Message);
                }
            }

            if (_originalAhegaoActiveByGameObject.Count > 0)
            {
                GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();

                var objectMap = new Dictionary<int, GameObject>(allObjects != null ? allObjects.Length : 0);
                if (allObjects != null)
                {
                    for (int i = 0; i < allObjects.Length; i++)
                    {
                        GameObject obj = allObjects[i];
                        if (!IsSceneGameObject(obj))
                        {
                            continue;
                        }

                        objectMap[obj.GetInstanceID()] = obj;
                    }
                }

                foreach (KeyValuePair<int, bool> item in _originalAhegaoActiveByGameObject)
                {
                    GameObject obj;
                    if (!objectMap.TryGetValue(item.Key, out obj) || obj == null)
                    {
                        missing++;
                        continue;
                    }

                    bool desiredActive = item.Value;
                    if (obj.activeSelf == desiredActive)
                    {
                        unchanged++;
                        continue;
                    }

                    try
                    {
                        obj.SetActive(desiredActive);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        LogWarn("[doll-mode] restore ahegao-go failed: " + ex.Message);
                    }
                }
            }
        }

        private List<MonoBehaviour> GetAhegaoBehaviours(bool forceRescan)
        {
            List<string> keywords = GetAhegaoKeywordsCached();
            string fingerprint = _cachedAhegaoKeywordFingerprint;

            float now = Time.unscaledTime;
            if (forceRescan || now >= _nextAhegaoRescanAt || !string.Equals(_lastAhegaoKeywordFingerprint, fingerprint, StringComparison.Ordinal))
            {
                bool patchReady = _harmony != null && DollModeGuardHooks.EnsureMainGameAhegaoBlock(_harmony);
                if (!_hasAhegaoGuardPatchState || patchReady != _lastAhegaoGuardPatchReady)
                {
                    LogInfo(
                        "[ahegao-guard]"
                        + " patchReady=" + BoolTo01(patchReady)
                        + " source=rescan result=ok");
                    _lastAhegaoGuardPatchReady = patchReady;
                    _hasAhegaoGuardPatchState = true;
                }

                RescanAhegaoBehaviours(keywords, fingerprint);
                _nextAhegaoRescanAt = now + AhegaoRescanIntervalSeconds;
            }

            return _cachedAhegaoBehaviours;
        }

        private void RescanAhegaoBehaviours(List<string> keywords, string fingerprint)
        {
            _cachedAhegaoBehaviours.Clear();
            _cachedAhegaoIds.Clear();

            MonoBehaviour[] all = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();

            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    MonoBehaviour behaviour = all[i];
                    if (!IsSceneBehaviourCandidate(behaviour))
                    {
                        continue;
                    }

                    if (!IsAhegaoBehaviour(behaviour, keywords))
                    {
                        continue;
                    }

                    int id = behaviour.GetInstanceID();
                    if (_cachedAhegaoIds.Add(id))
                    {
                        _cachedAhegaoBehaviours.Add(behaviour);
                    }
                }
            }

            int count = _cachedAhegaoBehaviours.Count;
            if (count != _lastAhegaoRescanCount || !string.Equals(_lastAhegaoKeywordFingerprint, fingerprint, StringComparison.Ordinal))
            {
                LogInfo(
                    "[ahegao-rescan]"
                    + " keywords=" + fingerprint
                    + " total=" + count
                    + " result=ok");

                _lastAhegaoRescanCount = count;
                _lastAhegaoKeywordFingerprint = fingerprint;
            }
        }

        private bool ShouldAffectAhegaoBehaviour(MonoBehaviour behaviour)
        {
            List<string> keywords = GetAhegaoKeywordsCached();
            if (!IsAhegaoBehaviour(behaviour, keywords))
            {
                return false;
            }

            ChaControl cha = null;
            try
            {
                cha = behaviour.GetComponentInParent<ChaControl>();
            }
            catch
            {
                return true;
            }

            if (cha == null)
            {
                return true;
            }

            return IsTargetCharacter(cha);
        }

        private List<string> GetAhegaoKeywordsCached()
        {
            string csv = _settings != null ? _settings.AhegaoPluginKeywordCsv : string.Empty;
            if (_cachedAhegaoKeywords == null || !string.Equals(_cachedAhegaoKeywordCsv, csv, StringComparison.Ordinal))
            {
                _cachedAhegaoKeywords = ParseAhegaoKeywords();
                _cachedAhegaoKeywordFingerprint = BuildKeywordFingerprint(_cachedAhegaoKeywords);
                _cachedAhegaoKeywordCsv = csv;
                _nextAhegaoRescanAt = 0f;
            }
            return _cachedAhegaoKeywords;
        }

        private List<string> ParseAhegaoKeywords()
        {
            string csv = _settings != null ? _settings.AhegaoPluginKeywordCsv : string.Empty;
            if (string.IsNullOrWhiteSpace(csv))
            {
                csv = "ahegao,maingameahegao,ksplug,kplug";
            }

            string[] tokens = csv.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var keywords = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tokens.Length; i++)
            {
                string keyword = (tokens[i] ?? string.Empty).Trim().ToLowerInvariant();
                if (keyword.Length == 0)
                {
                    continue;
                }

                if (unique.Add(keyword))
                {
                    keywords.Add(keyword);
                }
            }

            if (keywords.Count == 0)
            {
                keywords.Add("ahegao");
                keywords.Add("ksplug");
            }

            return keywords;
        }

        private static string BuildKeywordFingerprint(List<string> keywords)
        {
            if (keywords == null || keywords.Count == 0)
            {
                return "(none)";
            }

            var sb = new StringBuilder();
            for (int i = 0; i < keywords.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }

                sb.Append(keywords[i]);
            }

            return sb.ToString();
        }

        private static bool IsAhegaoBehaviour(MonoBehaviour behaviour, List<string> keywords)
        {
            if (behaviour == null || keywords == null || keywords.Count == 0)
            {
                return false;
            }

            Type type = behaviour.GetType();
            string typeName = (type.FullName ?? type.Name ?? string.Empty).ToLowerInvariant();

            string assemblyName = string.Empty;
            try
            {
                assemblyName = (type.Assembly.GetName().Name ?? string.Empty).ToLowerInvariant();
            }
            catch
            {
                assemblyName = string.Empty;
            }

            for (int i = 0; i < keywords.Count; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (typeName.IndexOf(keyword, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }

                if (assemblyName.IndexOf(keyword, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSceneBehaviourCandidate(MonoBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return false;
            }

            GameObject go;
            try
            {
                go = behaviour.gameObject;
            }
            catch
            {
                return false;
            }

            return IsSceneGameObject(go);
        }

        private static bool IsSceneGameObject(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            try
            {
                return go.scene.IsValid();
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCharacterHierarchyObject(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            try
            {
                return go.GetComponentInParent<ChaControl>() != null;
            }
            catch
            {
                return false;
            }
        }

        private List<MonoBehaviour> GetFixationalBehaviours(bool forceRescan)
        {
            float now = Time.unscaledTime;
            if (forceRescan || now >= _nextFixationalRescanAt)
            {
                RescanFixationalBehaviours();
                _nextFixationalRescanAt = now + FixationalRescanIntervalSeconds;
            }

            return _cachedFixationalBehaviours;
        }

        private void RescanFixationalBehaviours()
        {
            _cachedFixationalBehaviours.Clear();
            _cachedFixationalIds.Clear();

            int fromKnownTypes = 0;
            bool usedFallback = false;
            Type[] types = DollModeGuardHooks.GetFixationalTypesSnapshot();
            if (types != null && types.Length > 0)
            {
                fromKnownTypes = CollectFixationalByKnownTypes(types, _cachedFixationalBehaviours, _cachedFixationalIds);
            }

            if (_cachedFixationalBehaviours.Count == 0)
            {
                usedFallback = true;
                CollectFixationalByNameFallback(_cachedFixationalBehaviours, _cachedFixationalIds);
            }

            int count = _cachedFixationalBehaviours.Count;
            if (count != _lastFixationalRescanCount || usedFallback != _lastFixationalFallbackUsed)
            {
                string source = usedFallback ? "fallback_name_scan" : "known_types";
                LogInfo(
                    "[fixational-rescan]"
                    + " source=" + source
                    + " total=" + count
                    + " fromKnownTypes=" + fromKnownTypes
                    + " result=ok");

                _lastFixationalRescanCount = count;
                _lastFixationalFallbackUsed = usedFallback;
            }
        }

        private static int CollectFixationalByKnownTypes(
            Type[] types,
            List<MonoBehaviour> results,
            HashSet<int> uniqueIds)
        {
            int added = 0;
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    continue;
                }

                UnityEngine.Object[] found;
                try
                {
                    found = UnityEngine.Object.FindObjectsOfType(type);
                }
                catch
                {
                    continue;
                }

                if (found == null)
                {
                    continue;
                }

                for (int f = 0; f < found.Length; f++)
                {
                    var behaviour = found[f] as MonoBehaviour;
                    if (behaviour == null)
                    {
                        continue;
                    }

                    int id = behaviour.GetInstanceID();
                    if (uniqueIds.Add(id))
                    {
                        results.Add(behaviour);
                        added++;
                    }
                }
            }

            return added;
        }

        private static void CollectFixationalByNameFallback(
            List<MonoBehaviour> results,
            HashSet<int> uniqueIds)
        {
            MonoBehaviour[] all;
            try
            {
                all = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            }
            catch
            {
                return;
            }

            if (all == null)
            {
                return;
            }

            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour behaviour = all[i];
                if (!IsFixationalBehaviour(behaviour))
                {
                    continue;
                }

                int id = behaviour.GetInstanceID();
                if (uniqueIds.Add(id))
                {
                    results.Add(behaviour);
                }
            }
        }

        private bool ShouldAffectFixationalBehaviour(MonoBehaviour behaviour)
        {
            if (behaviour == null || !IsFixationalBehaviour(behaviour))
            {
                return false;
            }

            ChaControl cha = null;
            try
            {
                cha = behaviour.GetComponentInParent<ChaControl>();
            }
            catch
            {
                return true;
            }

            if (cha == null)
            {
                return true;
            }

            return IsTargetCharacter(cha);
        }

        private static bool IsFixationalBehaviour(MonoBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return false;
            }

            Type type = behaviour.GetType();
            string typeName = type.FullName ?? type.Name ?? string.Empty;
            return typeName.IndexOf("FixationalEyeMovement", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private EyeRotationState CaptureEyeRotationState(ChaControl cha)
        {
            var state = new EyeRotationState();
            try
            {
                Transform leftEye;
                Transform rightEye;
                if (!TryResolveEyeTransforms(cha, out leftEye, out rightEye))
                {
                    return state;
                }

                if (leftEye != null)
                {
                    state.HasLeftEye = true;
                    state.LeftEye = leftEye;
                    state.LeftLocalRotation = leftEye.localRotation;
                }

                if (rightEye != null)
                {
                    state.HasRightEye = true;
                    state.RightEye = rightEye;
                    state.RightLocalRotation = rightEye.localRotation;
                }
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] capture eye-rotation failed: " + ex.Message);
            }

            return state;
        }

        private bool TryApplyEyeRotationFreeze(EyeRotationState desired, out bool changed)
        {
            changed = false;
            try
            {
                if (desired == null)
                {
                    return true;
                }

                if (desired.HasLeftEye && desired.LeftEye != null && !AreQuaternionsEqual(desired.LeftEye.localRotation, desired.LeftLocalRotation))
                {
                    desired.LeftEye.localRotation = desired.LeftLocalRotation;
                    changed = true;
                }

                if (desired.HasRightEye && desired.RightEye != null && !AreQuaternionsEqual(desired.RightEye.localRotation, desired.RightLocalRotation))
                {
                    desired.RightEye.localRotation = desired.RightLocalRotation;
                    changed = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] apply eye-rotation failed: " + ex.Message);
                return false;
            }
        }

        private bool TryRestoreEyeRotation(EyeRotationState desired, out bool restored)
        {
            restored = false;
            try
            {
                if (desired == null)
                {
                    return true;
                }

                if (desired.HasLeftEye && desired.LeftEye != null && !AreQuaternionsEqual(desired.LeftEye.localRotation, desired.LeftLocalRotation))
                {
                    desired.LeftEye.localRotation = desired.LeftLocalRotation;
                    restored = true;
                }

                if (desired.HasRightEye && desired.RightEye != null && !AreQuaternionsEqual(desired.RightEye.localRotation, desired.RightLocalRotation))
                {
                    desired.RightEye.localRotation = desired.RightLocalRotation;
                    restored = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] restore eye-rotation failed: " + ex.Message);
                return false;
            }
        }

        private EyeTextureOffsetState CaptureEyeTextureOffsetState(ChaControl cha)
        {
            var state = new EyeTextureOffsetState();
            try
            {
                ChaInfo info = cha as ChaInfo;
                if (info == null)
                {
                    return state;
                }

                Renderer[] rendEye = info.rendEye;
                if (rendEye != null)
                {
                    if (rendEye.Length > 0 && rendEye[0] != null)
                    {
                        Material mat = rendEye[0].material;
                        if (IsUsableTextureProperty(mat, ChaShader._expression))
                        {
                            state.HasLeftRendEye = true;
                            state.LeftRendEye = rendEye[0];
                            state.LeftExpressionOffset = mat.GetTextureOffset(ChaShader._expression);
                        }
                    }

                    if (rendEye.Length > 1 && rendEye[1] != null)
                    {
                        Material mat = rendEye[1].material;
                        if (IsUsableTextureProperty(mat, ChaShader._expression))
                        {
                            state.HasRightRendEye = true;
                            state.RightRendEye = rendEye[1];
                            state.RightExpressionOffset = mat.GetTextureOffset(ChaShader._expression);
                        }
                    }
                }

                var unique = new HashSet<string>(StringComparer.Ordinal);
                EyeLookMaterialControll[] eyeLookCtrls = info.eyeLookMatCtrl;
                if (eyeLookCtrls != null)
                {
                    for (int i = 0; i < eyeLookCtrls.Length; i++)
                    {
                        EyeLookMaterialControll ctrl = eyeLookCtrls[i];
                        if (ctrl == null || ctrl._renderer == null || ctrl.texStates == null)
                        {
                            continue;
                        }

                        Material mat = ctrl._renderer.material;
                        if (mat == null)
                        {
                            continue;
                        }

                        int rendererId = ctrl._renderer.GetInstanceID();
                        for (int t = 0; t < ctrl.texStates.Length; t++)
                        {
                            var ts = ctrl.texStates[t];
                            int texId = ts.texID;
                            if (!IsUsableTextureProperty(mat, texId))
                            {
                                continue;
                            }

                            string key = rendererId + "|" + texId;
                            if (!unique.Add(key))
                            {
                                continue;
                            }

                            state.EyeLookOffsets.Add(
                                new EyeTexOffsetEntry
                                {
                                    Renderer = ctrl._renderer,
                                    TexId = texId,
                                    Offset = mat.GetTextureOffset(texId)
                                });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] capture eye-tex-offset failed: " + ex.Message);
            }

            return state;
        }

        private bool TryApplyEyeTextureOffsetFreeze(EyeTextureOffsetState desired, out bool changed)
        {
            changed = false;

            try
            {
                if (desired == null)
                {
                    return true;
                }

                bool changedPart = false;
                if (desired.HasLeftRendEye
                    && !TrySetRendererTextureOffset(
                        desired.LeftRendEye,
                        ChaShader._expression,
                        desired.LeftExpressionOffset,
                        out changedPart))
                {
                    return false;
                }

                if (changedPart)
                {
                    changed = true;
                }

                if (desired.HasRightRendEye
                    && !TrySetRendererTextureOffset(
                        desired.RightRendEye,
                        ChaShader._expression,
                        desired.RightExpressionOffset,
                        out changedPart))
                {
                    return false;
                }

                if (changedPart)
                {
                    changed = true;
                }

                if (desired.EyeLookOffsets != null)
                {
                    for (int i = 0; i < desired.EyeLookOffsets.Count; i++)
                    {
                        EyeTexOffsetEntry entry = desired.EyeLookOffsets[i];
                        if (entry == null)
                        {
                            continue;
                        }

                        if (!TrySetRendererTextureOffset(entry.Renderer, entry.TexId, entry.Offset, out changedPart))
                        {
                            return false;
                        }

                        if (changedPart)
                        {
                            changed = true;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] apply eye-tex-offset failed: " + ex.Message);
                return false;
            }
        }

        private bool TryRestoreEyeTextureOffset(EyeTextureOffsetState desired, out bool restored)
        {
            if (!TryApplyEyeTextureOffsetFreeze(desired, out restored))
            {
                LogWarn("[doll-mode] restore eye-tex-offset failed");
                return false;
            }

            return true;
        }

        private bool TrySetRendererTextureOffset(Renderer renderer, int texId, Vector2 desired, out bool changed)
        {
            changed = false;
            try
            {
                if (renderer == null)
                {
                    return true;
                }

                Material mat = renderer.material;
                if (mat == null)
                {
                    return true;
                }

                if (!IsUsableTextureProperty(mat, texId))
                {
                    return true;
                }

                Vector2 current = mat.GetTextureOffset(texId);
                if (AreVectorsEqual(current, desired))
                {
                    return true;
                }

                mat.SetTextureOffset(texId, desired);
                changed = true;
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] set eye-tex-offset failed: " + ex.Message);
                return false;
            }
        }

        private static bool IsUsableTextureProperty(Material mat, int texId)
        {
            if (mat == null || texId <= 0)
            {
                return false;
            }

            try
            {
                return mat.HasProperty(texId);
            }
            catch
            {
                return false;
            }
        }

        private static bool AreVectorsEqual(Vector2 left, Vector2 right)
        {
            return Mathf.Abs(left.x - right.x) <= 0.0001f
                && Mathf.Abs(left.y - right.y) <= 0.0001f;
        }

        private static bool AreQuaternionsEqual(Quaternion left, Quaternion right)
        {
            return Mathf.Abs(left.x - right.x) <= 0.0001f
                && Mathf.Abs(left.y - right.y) <= 0.0001f
                && Mathf.Abs(left.z - right.z) <= 0.0001f
                && Mathf.Abs(left.w - right.w) <= 0.0001f;
        }

        private static bool TryResolveEyeTransforms(ChaControl cha, out Transform leftEye, out Transform rightEye)
        {
            leftEye = null;
            rightEye = null;

            try
            {
                EyeLookController eyeLookCtrl = cha != null ? cha.eyeLookCtrl : null;
                EyeLookCalc eyeLookCalc = eyeLookCtrl != null ? eyeLookCtrl.eyeLookScript : null;
                EyeObject[] eyeObjs = eyeLookCalc != null ? eyeLookCalc.eyeObjs : null;
                if (eyeObjs == null || eyeObjs.Length == 0)
                {
                    return false;
                }

                for (int i = 0; i < eyeObjs.Length; i++)
                {
                    EyeObject eyeObj = eyeObjs[i];
                    if (eyeObj == null || eyeObj.eyeTransform == null)
                    {
                        continue;
                    }

                    if (eyeObj.eyeLR == EYE_LR.EYE_L)
                    {
                        leftEye = eyeObj.eyeTransform;
                    }
                    else if (eyeObj.eyeLR == EYE_LR.EYE_R)
                    {
                        rightEye = eyeObj.eyeTransform;
                    }
                }

                if (leftEye == null || rightEye == null)
                {
                    for (int i = 0; i < eyeObjs.Length; i++)
                    {
                        EyeObject eyeObj = eyeObjs[i];
                        if (eyeObj == null || eyeObj.eyeTransform == null)
                        {
                            continue;
                        }

                        if (leftEye == null)
                        {
                            leftEye = eyeObj.eyeTransform;
                            continue;
                        }

                        if (rightEye == null && eyeObj.eyeTransform != leftEye)
                        {
                            rightEye = eyeObj.eyeTransform;
                            continue;
                        }
                    }
                }

                return leftEye != null || rightEye != null;
            }
            catch
            {
                return false;
            }
        }

        private FaceExpressionState CaptureFaceExpressionState(ChaControl cha)
        {
            return new FaceExpressionState
            {
                EyebrowPattern = ReadCurrentFaceEyebrowPattern(cha),
                EyePattern = ReadCurrentFaceEyePattern(cha),
                MouthPattern = ReadCurrentFaceMouthPattern(cha),
                CheekRate = ReadCurrentFaceCheekRate(cha),
                TearsLevel = ReadCurrentFaceTearsLevel(cha),
                FaceSiruLevel = ReadCurrentFaceSiruLevel(cha),
                EyesOpenMax = ReadCurrentFaceEyesOpenMax(cha),
                MouthOpenMax = ReadCurrentFaceMouthOpenMax(cha),
                MouthFixed = ReadCurrentFaceMouthFixed(cha),
                MouthFixedRate = ReadCurrentFaceMouthFixedRate(cha)
            };
        }

        private FaceExpressionState BuildDollModeFaceExpressionState()
        {
            return new FaceExpressionState
            {
                EyebrowPattern = Mathf.Max(0, _settings != null ? _settings.DollModeEyebrowPattern : 0),
                EyePattern = Mathf.Max(0, _settings != null ? _settings.DollModeEyePattern : 0),
                MouthPattern = Mathf.Max(0, _settings != null ? _settings.DollModeMouthPattern : 0),
                CheekRate = Mathf.Clamp01(_settings != null ? _settings.DollModeCheekRate : 0f),
                TearsLevel = (byte)Mathf.Clamp(_settings != null ? _settings.DollModeTearsLevel : 0, 0, 10),
                FaceSiruLevel = (byte)Mathf.Clamp(_settings != null ? _settings.DollModeFaceSweatLevel : 0, 0, 3),
                EyesOpenMax = Mathf.Clamp01(_settings != null ? _settings.DollModeEyesOpen : 0.80f),
                MouthOpenMax = Mathf.Clamp01(_settings != null ? _settings.DollModeMouthOpen : 0.30f),
                MouthFixed = true,
                MouthFixedRate = Mathf.Clamp01(_settings != null ? _settings.DollModeMouthOpen : 0.30f)
            };
        }

        private static FaceExpressionState CloneFaceExpressionState(FaceExpressionState src)
        {
            if (src == null)
            {
                return null;
            }

            return new FaceExpressionState
            {
                EyebrowPattern = src.EyebrowPattern,
                EyePattern = src.EyePattern,
                MouthPattern = src.MouthPattern,
                CheekRate = src.CheekRate,
                TearsLevel = src.TearsLevel,
                FaceSiruLevel = src.FaceSiruLevel,
                EyesOpenMax = src.EyesOpenMax,
                MouthOpenMax = src.MouthOpenMax,
                MouthFixed = src.MouthFixed,
                MouthFixedRate = src.MouthFixedRate
            };
        }

        private static FaceExpressionState BlendFaceExpressionState(FaceExpressionState from, FaceExpressionState to, float weight)
        {
            FaceExpressionState fromSafe = from ?? to;
            FaceExpressionState toSafe = to ?? from;
            if (fromSafe == null && toSafe == null)
            {
                return new FaceExpressionState();
            }

            if (fromSafe == null)
            {
                return CloneFaceExpressionState(toSafe);
            }

            if (toSafe == null)
            {
                return CloneFaceExpressionState(fromSafe);
            }

            float w = Mathf.Clamp01(weight);
            bool switchPattern = w >= 0.5f;

            return new FaceExpressionState
            {
                EyebrowPattern = Mathf.Max(0, switchPattern ? toSafe.EyebrowPattern : fromSafe.EyebrowPattern),
                EyePattern = Mathf.Max(0, switchPattern ? toSafe.EyePattern : fromSafe.EyePattern),
                MouthPattern = Mathf.Max(0, switchPattern ? toSafe.MouthPattern : fromSafe.MouthPattern),
                CheekRate = Mathf.Lerp(Mathf.Clamp01(fromSafe.CheekRate), Mathf.Clamp01(toSafe.CheekRate), w),
                TearsLevel = (byte)Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(fromSafe.TearsLevel, toSafe.TearsLevel, w)),
                    0,
                    10),
                FaceSiruLevel = (byte)Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(fromSafe.FaceSiruLevel, toSafe.FaceSiruLevel, w)),
                    0,
                    3),
                EyesOpenMax = Mathf.Lerp(Mathf.Clamp01(fromSafe.EyesOpenMax), Mathf.Clamp01(toSafe.EyesOpenMax), w),
                MouthOpenMax = Mathf.Lerp(Mathf.Clamp01(fromSafe.MouthOpenMax), Mathf.Clamp01(toSafe.MouthOpenMax), w),
                MouthFixed = switchPattern ? toSafe.MouthFixed : fromSafe.MouthFixed,
                MouthFixedRate = Mathf.Lerp(
                    Mathf.Clamp(fromSafe.MouthFixedRate, -1f, 1f),
                    Mathf.Clamp(toSafe.MouthFixedRate, -1f, 1f),
                    w)
            };
        }

        private bool TryApplyDollFaceExpression(
            ChaControl cha,
            out bool patternChanged,
            out bool openChanged,
            out bool cheekChanged,
            out bool tearsChanged,
            out bool sweatChanged)
        {
            patternChanged = false;
            openChanged = false;
            cheekChanged = false;
            tearsChanged = false;
            sweatChanged = false;

            try
            {
                if (cha == null)
                {
                    return true;
                }

                FaceExpressionState dollTarget = BuildDollModeFaceExpressionState();
                FaceExpressionState desiredState = dollTarget;

                float enterWeight = ResolveFaceEnterTransitionWeight();
                if (enterWeight < 1f)
                {
                    int key = cha.GetInstanceID();
                    FaceExpressionState originalState;
                    if (_originalFaceExpressionByCha.TryGetValue(key, out originalState) && originalState != null)
                    {
                        desiredState = BlendFaceExpressionState(originalState, dollTarget, enterWeight);
                    }
                }

                return TryRestoreFaceExpression(
                    cha,
                    desiredState,
                    out patternChanged,
                    out openChanged,
                    out cheekChanged,
                    out tearsChanged,
                    out sweatChanged);
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] apply face-expression failed: " + ex.Message);
                return false;
            }
        }

        private bool TryRestoreFaceExpression(
            ChaControl cha,
            FaceExpressionState desired,
            out bool patternRestored,
            out bool openRestored,
            out bool cheekRestored,
            out bool tearsRestored,
            out bool sweatRestored)
        {
            patternRestored = false;
            openRestored = false;
            cheekRestored = false;
            tearsRestored = false;
            sweatRestored = false;

            try
            {
                if (cha == null || desired == null)
                {
                    return true;
                }

                int desiredEyebrowPattern = Mathf.Max(0, desired.EyebrowPattern);
                int desiredEyePattern = Mathf.Max(0, desired.EyePattern);
                int desiredMouthPattern = Mathf.Max(0, desired.MouthPattern);
                float desiredCheekRate = Mathf.Clamp01(desired.CheekRate);
                byte desiredTearsLevel = desired.TearsLevel;
                bool allowFaceSiru = _settings != null && _settings.AllowFaceSiruDuringDollMode;
                byte desiredFaceSiruLevel = desired.FaceSiruLevel;
                float desiredEyesOpen = Mathf.Clamp01(desired.EyesOpenMax);
                float desiredMouthOpen = Mathf.Clamp01(desired.MouthOpenMax);
                bool desiredMouthFixed = desired.MouthFixed;
                float desiredMouthFixedRate = Mathf.Clamp(desired.MouthFixedRate, -1f, 1f);

                using (BeginInternalFaceWrite())
                {
                    if (ReadCurrentFaceEyebrowPattern(cha) != desiredEyebrowPattern)
                    {
                        cha.ChangeEyebrowPtn(desiredEyebrowPattern, true);
                        patternRestored = true;
                    }

                    if (ReadCurrentFaceEyePattern(cha) != desiredEyePattern)
                    {
                        cha.ChangeEyesPtn(desiredEyePattern, true);
                        patternRestored = true;
                    }

                    if (ReadCurrentFaceMouthPattern(cha) != desiredMouthPattern)
                    {
                        cha.ChangeMouthPtn(desiredMouthPattern, true);
                        patternRestored = true;
                    }

                    if (!AreRatesEqual(ReadCurrentFaceCheekRate(cha), desiredCheekRate))
                    {
                        cha.ChangeHohoAkaRate(desiredCheekRate);
                        cheekRestored = true;
                    }

                    bool tearsLevelRestored;
                    if (!TrySetFaceTearsLevel(cha, desiredTearsLevel, out tearsLevelRestored))
                    {
                        return false;
                    }

                    if (tearsLevelRestored)
                    {
                        tearsRestored = true;
                    }

                    bool faceSiruRestored = false;
                    if (!allowFaceSiru
                        && !TrySetFaceSiruLevel(cha, desiredFaceSiruLevel, out faceSiruRestored))
                    {
                        return false;
                    }

                    if (faceSiruRestored)
                    {
                        sweatRestored = true;
                    }

                    if (!AreRatesEqual(ReadCurrentFaceEyesOpenMax(cha), desiredEyesOpen))
                    {
                        cha.ChangeEyesOpenMax(desiredEyesOpen);
                        openRestored = true;
                    }

                    if (!AreRatesEqual(ReadCurrentFaceMouthOpenMax(cha), desiredMouthOpen))
                    {
                        cha.ChangeMouthOpenMax(desiredMouthOpen);
                        openRestored = true;
                    }

                    if (ReadCurrentFaceMouthFixed(cha) != desiredMouthFixed)
                    {
                        cha.ChangeMouthFixed(desiredMouthFixed);
                        openRestored = true;
                    }

                    if (desiredMouthFixed
                        && cha.mouthCtrl != null
                        && !AreRatesEqual(cha.mouthCtrl.FixedRate, desiredMouthFixedRate))
                    {
                        cha.mouthCtrl.FixedRate = desiredMouthFixedRate;
                        openRestored = true;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] restore face-expression failed: " + ex.Message);
                return false;
            }
        }

        private static bool AreRatesEqual(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.0001f;
        }

        private static bool ReadCurrentHideHighlight(ChaControl cha)
        {
            try
            {
                return cha != null && cha.fileStatus != null && cha.fileStatus.hideEyesHighlight;
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadCurrentEyesBlink(ChaControl cha)
        {
            try
            {
                return cha != null && cha.fileStatus != null && cha.fileStatus.eyesBlink;
            }
            catch
            {
                return true;
            }
        }

        private static int ReadCurrentFaceEyebrowPattern(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.eyebrowPtn : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static int ReadCurrentFaceEyePattern(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.eyesPtn : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static int ReadCurrentFaceMouthPattern(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.mouthPtn : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static float ReadCurrentFaceCheekRate(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.hohoAkaRate : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static byte ReadCurrentFaceTearsLevel(ChaControl cha)
        {
            try
            {
                return cha != null ? cha.tearsLv : (byte)0;
            }
            catch
            {
                return 0;
            }
        }

        private bool TrySetFaceTearsLevel(ChaControl cha, byte desiredLevel, out bool changed)
        {
            changed = false;

            try
            {
                if (cha == null)
                {
                    return true;
                }

                byte before = ReadCurrentFaceTearsLevel(cha);
                if (before == desiredLevel)
                {
                    return true;
                }

                cha.tearsLv = desiredLevel;
                changed = ReadCurrentFaceTearsLevel(cha) != before;
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] tears apply failed: " + ex.Message);
                return false;
            }
        }

        private static byte ReadCurrentFaceSiruLevel(ChaControl cha)
        {
            try
            {
                if (cha == null || cha.fileStatus == null || cha.fileStatus.siruLv == null)
                {
                    return 0;
                }

                int faceIndex = (int)ChaFileDefine.SiruParts.SiruKao;
                if (faceIndex < 0 || faceIndex >= cha.fileStatus.siruLv.Length)
                {
                    return 0;
                }

                return cha.fileStatus.siruLv[faceIndex];
            }
            catch
            {
                return 0;
            }
        }

        private bool TrySetFaceSiruLevel(ChaControl cha, byte desiredLevel, out bool changed)
        {
            changed = false;

            try
            {
                if (cha == null || cha.fileStatus == null)
                {
                    return true;
                }

                byte before = ReadCurrentFaceSiruLevel(cha);
                cha.SetSiruFlags(ChaFileDefine.SiruParts.SiruKao, desiredLevel);

                byte[] siruLevels = cha.fileStatus.siruLv;
                int faceIndex = (int)ChaFileDefine.SiruParts.SiruKao;
                if (siruLevels != null && faceIndex >= 0 && faceIndex < siruLevels.Length)
                {
                    if (siruLevels[faceIndex] != desiredLevel)
                    {
                        siruLevels[faceIndex] = desiredLevel;
                    }
                }

                changed = before != desiredLevel;
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] apply face-sweat failed: " + ex.Message);
                return false;
            }
        }

        private static float ReadCurrentFaceEyesOpenMax(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.eyesOpenMax : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        private static float ReadCurrentFaceMouthOpenMax(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.mouthOpenMax : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        private static bool ReadCurrentFaceMouthFixed(ChaControl cha)
        {
            try
            {
                return cha != null && cha.fileStatus != null && cha.fileStatus.mouthFixed;
            }
            catch
            {
                return false;
            }
        }

        private static float ReadCurrentFaceMouthFixedRate(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.mouthCtrl != null) ? cha.mouthCtrl.FixedRate : -1f;
            }
            catch
            {
                return -1f;
            }
        }

        private EyeLookState CaptureEyeLookState(ChaControl cha)
        {
            var state = new EyeLookState
            {
                HasController = false,
                ControllerEnabled = false,
                Target = null,
                Pattern = 0,
                TargetType = 0,
                TargetRate = 0.5f,
                TargetAngle = 0f,
                TargetRange = 1f
            };

            try
            {
                if (cha == null || cha.eyeLookCtrl == null)
                {
                    return state;
                }

                state.HasController = true;
                state.ControllerEnabled = cha.eyeLookCtrl.enabled;
                state.Target = cha.eyeLookCtrl.target;
                state.Pattern = ReadCurrentEyesLookPattern(cha);
                state.TargetType = ReadCurrentEyesTargetType(cha);
                state.TargetRate = ReadCurrentEyesTargetRate(cha);
                state.TargetAngle = ReadCurrentEyesTargetAngle(cha);
                state.TargetRange = ReadCurrentEyesTargetRange(cha);
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] capture eyeLook failed: " + ex.Message);
            }

            return state;
        }

        private bool TryStopEyeLook(ChaControl cha, out bool stopped)
        {
            stopped = false;
            try
            {
                if (cha == null || cha.eyeLookCtrl == null)
                {
                    return true;
                }

                bool wasStopped = !cha.eyeLookCtrl.enabled && cha.eyeLookCtrl.target == null;
                using (BeginInternalEyeLookWrite())
                {
                    cha.eyeLookCtrl.target = null;
                    cha.eyeLookCtrl.enabled = false;
                }

                stopped = !wasStopped;
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] apply eyeLook stop failed: " + ex.Message);
                return false;
            }
        }

        private bool TryRestoreEyeLook(ChaControl cha, EyeLookState desired, out bool restored)
        {
            restored = false;

            try
            {
                if (cha == null || desired == null || !desired.HasController || cha.eyeLookCtrl == null)
                {
                    return true;
                }

                bool changed = false;
                int currentPattern = ReadCurrentEyesLookPattern(cha);
                if (currentPattern != desired.Pattern)
                {
                    using (BeginInternalEyeLookWrite())
                    {
                        cha.ChangeLookEyesPtn(desired.Pattern);
                    }

                    changed = true;
                }

                Transform currentTarget = cha.eyeLookCtrl.target;
                Transform desiredTarget = desired.Target;
                bool sameTarget = currentTarget == desiredTarget;
                if (!sameTarget)
                {
                    using (BeginInternalEyeLookWrite())
                    {
                        if (desiredTarget != null)
                        {
                            cha.eyeLookCtrl.target = desiredTarget;
                        }
                        else
                        {
                            cha.ChangeLookEyesTarget(
                                desired.TargetType,
                                null,
                                desired.TargetRate,
                                desired.TargetAngle,
                                desired.TargetRange);
                        }
                    }

                    changed = true;
                }

                if (cha.eyeLookCtrl.enabled != desired.ControllerEnabled)
                {
                    using (BeginInternalEyeLookWrite())
                    {
                        cha.eyeLookCtrl.enabled = desired.ControllerEnabled;
                    }

                    changed = true;
                }

                restored = changed;
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[doll-mode] restore eyeLook failed: " + ex.Message);
                return false;
            }
        }

        private static int ReadCurrentEyesLookPattern(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.eyesLookPtn : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static int ReadCurrentEyesTargetType(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.eyesTargetType : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static float ReadCurrentEyesTargetRate(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.eyesTargetRate : 0.5f;
            }
            catch
            {
                return 0.5f;
            }
        }

        private static float ReadCurrentEyesTargetAngle(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.eyesTargetAngle : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static float ReadCurrentEyesTargetRange(ChaControl cha)
        {
            try
            {
                return (cha != null && cha.fileStatus != null) ? cha.fileStatus.eyesTargetRange : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        private void ProbeEyeStateTransitions(string stage)
        {
            if (!IsEyeTraceEnabled())
            {
                return;
            }

            ChaControl[] all;
            try
            {
                all = CanonPlugins.Shared.IndependentActorFilter.FindNonIndependent();
            }
            catch (Exception ex)
            {
                LogWarn("[trace][eye] scan failed stage=" + stage + " error=" + ex.Message);
                return;
            }

            var active = new HashSet<int>();
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (!IsTargetCharacter(cha))
                {
                    continue;
                }

                int key = cha.GetInstanceID();
                active.Add(key);
                EyeTraceSnapshot current = CaptureEyeTraceSnapshot(cha, stage);

                EyeTraceSnapshot previous;
                if (_eyeTraceLastByCha.TryGetValue(key, out previous))
                {
                    TryLogEyeTraceDiff(previous, current);
                }

                _eyeTraceLastByCha[key] = current;
            }

            if (_eyeTraceLastByCha.Count == 0)
            {
                return;
            }

            var removeKeys = new List<int>();
            foreach (KeyValuePair<int, EyeTraceSnapshot> pair in _eyeTraceLastByCha)
            {
                if (!active.Contains(pair.Key))
                {
                    removeKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < removeKeys.Count; i++)
            {
                _eyeTraceLastByCha.Remove(removeKeys[i]);
            }
        }

        private EyeTraceSnapshot CaptureEyeTraceSnapshot(ChaControl cha, string stage)
        {
            var snapshot = new EyeTraceSnapshot
            {
                ChaId = cha != null ? cha.GetInstanceID() : 0,
                Frame = Time.frameCount,
                Time = Time.unscaledTime,
                Stage = string.IsNullOrWhiteSpace(stage) ? "unknown" : stage,
                HasController = false,
                ControllerEnabled = false,
                TargetId = 0,
                HasLeftEye = false,
                HasRightEye = false
            };

            if (cha == null)
            {
                return snapshot;
            }

            try
            {
                EyeLookController ctrl = cha.eyeLookCtrl;
                if (ctrl != null)
                {
                    snapshot.HasController = true;
                    snapshot.ControllerEnabled = ctrl.enabled;
                    snapshot.TargetId = ctrl.target != null ? ctrl.target.GetInstanceID() : 0;
                }
            }
            catch
            {
                // ignore trace snapshot read failures
            }

            try
            {
                Transform leftEye;
                Transform rightEye;
                if (TryResolveEyeTransforms(cha, out leftEye, out rightEye))
                {
                    if (leftEye != null)
                    {
                        snapshot.HasLeftEye = true;
                        snapshot.LeftLocalRotation = leftEye.localRotation;
                    }

                    if (rightEye != null)
                    {
                        snapshot.HasRightEye = true;
                        snapshot.RightLocalRotation = rightEye.localRotation;
                    }
                }
            }
            catch
            {
                // ignore trace snapshot read failures
            }

            return snapshot;
        }

        private void TryLogEyeTraceDiff(EyeTraceSnapshot previous, EyeTraceSnapshot current)
        {
            if (previous == null || current == null)
            {
                return;
            }

            var diff = new StringBuilder();
            var flags = new StringBuilder();

            if (previous.HasController != current.HasController)
            {
                AppendTraceDiff(
                    diff,
                    "hasCtrl",
                    BoolTo01(previous.HasController) + "->" + BoolTo01(current.HasController));
                flags.Append("ctrl-presence;");
            }

            if (previous.HasController && current.HasController)
            {
                if (previous.ControllerEnabled != current.ControllerEnabled)
                {
                    AppendTraceDiff(
                        diff,
                        "ctrlEnabled",
                        BoolTo01(previous.ControllerEnabled) + "->" + BoolTo01(current.ControllerEnabled));
                    flags.Append("ctrl-enabled;");
                }

                if (previous.TargetId != current.TargetId)
                {
                    AppendTraceDiff(
                        diff,
                        "targetId",
                        previous.TargetId + "->" + current.TargetId);
                    flags.Append("target;");
                }
            }

            if (previous.HasLeftEye && current.HasLeftEye)
            {
                float leftDelta = Quaternion.Angle(previous.LeftLocalRotation, current.LeftLocalRotation);
                if (leftDelta > EyeTraceRotationDeltaThresholdDeg)
                {
                    AppendTraceDiff(diff, "leftRotDeltaDeg", leftDelta.ToString("0.###"));
                    flags.Append("left-rot;");
                }
            }
            else if (previous.HasLeftEye != current.HasLeftEye)
            {
                AppendTraceDiff(
                    diff,
                    "hasLeftEye",
                    BoolTo01(previous.HasLeftEye) + "->" + BoolTo01(current.HasLeftEye));
                flags.Append("left-eye-presence;");
            }

            if (previous.HasRightEye && current.HasRightEye)
            {
                float rightDelta = Quaternion.Angle(previous.RightLocalRotation, current.RightLocalRotation);
                if (rightDelta > EyeTraceRotationDeltaThresholdDeg)
                {
                    AppendTraceDiff(diff, "rightRotDeltaDeg", rightDelta.ToString("0.###"));
                    flags.Append("right-rot;");
                }
            }
            else if (previous.HasRightEye != current.HasRightEye)
            {
                AppendTraceDiff(
                    diff,
                    "hasRightEye",
                    BoolTo01(previous.HasRightEye) + "->" + BoolTo01(current.HasRightEye));
                flags.Append("right-eye-presence;");
            }

            if (diff.Length == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            string cooldownKey =
                current.ChaId
                + "|"
                + previous.Stage
                + "->"
                + current.Stage
                + "|"
                + flags;

            if (!(_settings != null && _settings.VerboseLog))
            {
                float nextAllowed;
                if (_eyeTraceLogCooldownByKey.TryGetValue(cooldownKey, out nextAllowed) && now < nextAllowed)
                {
                    return;
                }

                _eyeTraceLogCooldownByKey[cooldownKey] = now + EyeTraceRepeatedDiffCooldownSeconds;
            }

            string recentSignals = BuildEyeTraceSignalSummary(previous.Time, current.Time);
            LogInfo(
                "[trace][eye-change]"
                + " frame=" + current.Frame
                + " t=" + current.Time.ToString("0.###")
                + " cha=" + current.ChaId
                + " window=" + previous.Stage + "->" + current.Stage
                + " diff=" + diff
                + " recentSignals=" + recentSignals
                + " result=detected");
        }

        private static void AppendTraceDiff(StringBuilder builder, string key, string value)
        {
            if (builder == null)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(",");
            }

            builder.Append(key).Append("=").Append(value);
        }

        private static string BoolTo01(bool value)
        {
            return value ? "1" : "0";
        }

        private static bool IsDifferent(float a, float b)
        {
            return Mathf.Abs(a - b) > 0.0005f;
        }

        private string BuildEyeTraceSignalSummary(float fromTime, float toTime)
        {
            if (_eyeTraceSignals.Count == 0)
            {
                return "none";
            }

            float min = Math.Min(fromTime, toTime) - EyeTraceSignalWindowPaddingSeconds;
            float max = Math.Max(fromTime, toTime) + EyeTraceSignalWindowPaddingSeconds;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < _eyeTraceSignals.Count; i++)
            {
                EyeTraceSignal signal = _eyeTraceSignals[i];
                if (signal == null || signal.Time < min || signal.Time > max)
                {
                    continue;
                }

                string key = (signal.Category ?? "unknown") + ":" + (signal.Point ?? "unknown");
                int count;
                counts.TryGetValue(key, out count);
                counts[key] = count + 1;
            }

            if (counts.Count == 0)
            {
                return "none";
            }

            var summary = new StringBuilder();
            int emitted = 0;
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (emitted >= 6)
                {
                    summary.Append(",...");
                    break;
                }

                if (summary.Length > 0)
                {
                    summary.Append(",");
                }

                summary.Append(pair.Key).Append("x").Append(pair.Value);
                emitted++;
            }

            return summary.ToString();
        }

        private void RecordEyeTraceSignal(string category, string point)
        {
            if (!IsEyeTraceEnabled())
            {
                return;
            }

            _eyeTraceSignals.Add(
                new EyeTraceSignal
                {
                    Frame = Time.frameCount,
                    Time = Time.unscaledTime,
                    Category = string.IsNullOrWhiteSpace(category) ? "unknown" : category,
                    Point = string.IsNullOrWhiteSpace(point) ? "unknown" : point
                });

            if (_eyeTraceSignals.Count > EyeTraceSignalCapacity)
            {
                _eyeTraceSignals.RemoveAt(0);
            }

            PruneEyeTraceSignals(Time.unscaledTime - EyeTraceSignalRetentionSeconds);
        }

        private void PruneEyeTraceSignals(float minTime)
        {
            int removeCount = 0;
            for (int i = 0; i < _eyeTraceSignals.Count; i++)
            {
                EyeTraceSignal signal = _eyeTraceSignals[i];
                if (signal == null || signal.Time < minTime)
                {
                    removeCount++;
                    continue;
                }

                break;
            }

            if (removeCount > 0)
            {
                _eyeTraceSignals.RemoveRange(0, removeCount);
            }
        }

        private void ResetEyeTraceState()
        {
            _eyeTraceLastByCha.Clear();
            _eyeTraceSignals.Clear();
            _eyeTraceLogCooldownByKey.Clear();
        }

        private bool IsEyeTraceEnabled()
        {
            return _settings != null
                && _settings.Enabled
                && _settings.DollModeEnabled
                && _settings.EyeMovementTraceLog;
        }

        private void LogInfo(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (_settings != null && !_settings.InfoLogEnabled)
            {
                return;
            }

            Logger?.LogInfo(message);
            AppendLogFile("Info", message);
        }

        private void LogWarn(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogWarning(message);
            AppendLogFile("Warn", message);
        }

        private void LogError(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogError(message);
            AppendLogFile("Error", message);
        }

        private void AppendLogFile(string level, string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (string.IsNullOrWhiteSpace(_logFilePath))
            {
                return;
            }

            try
            {
                lock (_logFileLock)
                {
                    File.AppendAllText(
                        _logFilePath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}",
                        Utf8NoBom);
                }
            }
            catch
            {
                // ignore file output failure
            }
        }
    }
}
