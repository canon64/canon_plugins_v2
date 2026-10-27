using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using CanonPlugins.Shared;
using MainGameTransformGizmo;
using UnityEngine;

namespace MainGameObjectComposer
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInDependency("com.kks.maingame.transformgizmo", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.kks.maingame.uiinputcapture", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(BetterPenetrationGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string Guid = "com.kks.main.gameobjectcomposer";
        public const string PluginName = "MainGameObjectComposer";
        public const string Version = "1.1.0";

        internal const string ParentKindRoot = "root";
        internal const string ParentKindManaged = "managed";
        internal const string ParentKindExternal = "external";

        private const int MainWindowId = 0x4D474F43;
        private const int StateWindowId = 0x4D474F53;

        private const string SettingsFileName = "ObjectComposerSettings.json";
        private const string LayoutFileName = "ObjectComposerLayout.json";

        private readonly RuntimeState _runtime = new RuntimeState();
        private readonly List<ManagedObjectData> _objects = new List<ManagedObjectData>();
        private readonly Dictionary<string, RuntimeObjectRef> _runtimeObjects = new Dictionary<string, RuntimeObjectRef>();
        private readonly List<ExternalParentTarget> _externalParentTargets = new List<ExternalParentTarget>();
        private readonly Dictionary<string, ExternalParentTarget> _externalParentTargetMap = new Dictionary<string, ExternalParentTarget>();

        private readonly Stack<string> _undoStack = new Stack<string>();
        private readonly Stack<string> _redoStack = new Stack<string>();

        private ComposerSettings _settings = new ComposerSettings();
        private SimpleFileLogger _fileLogger;

        private string _pluginDir = string.Empty;
        private string _settingsPath = string.Empty;
        private string _layoutPath = string.Empty;

        private Rect _mainWindowRect = new Rect(20f, 20f, 760f, 820f);
        private Rect _stateWindowRect = new Rect(800f, 20f, 520f, 620f);

        private Vector2 _mainScroll;
        private Vector2 _stateScroll;
        private Vector2 _externalTargetScroll;

        private string _selectedId;
        private string _parentCandidateKind = ParentKindRoot;
        private string _parentCandidateRefId;
        private bool _selectionDirty = true;
        private int _createPrimitiveIndex;

        private bool _mainWindowDragging;
        private bool _stateWindowDragging;

        private float _sliderPosX, _sliderPosY, _sliderPosZ;
        private float _sliderRotX, _sliderRotY, _sliderRotZ;
        private float _sliderScaleX = 1f, _sliderScaleY = 1f, _sliderScaleZ = 1f;

        private string _editName = string.Empty;
        private string _editPosX = "0";
        private string _editPosY = "0";
        private string _editPosZ = "0";
        private string _editRotX = "0";
        private string _editRotY = "0";
        private string _editRotZ = "0";
        private string _editScaleX = "1";
        private string _editScaleY = "1";
        private string _editScaleZ = "1";
        private string _editAxisX = "0";
        private string _editAxisY = "1";
        private string _editAxisZ = "0";
        private string _editAutoRotateSpeed = "45";
        private bool _editAutoRotateEnabled;
        private bool _editAutoRotateLocalSpace = true;

        // 動きモード（0=なし, 1=回転, 2=アングル, 3=ピストン）
        private int _editMotionMode;

        // Angle
        private string _editAngleAxisX = "0";
        private string _editAngleAxisY = "1";
        private string _editAngleAxisZ = "0";
        private string _editAngleAmplitudeDeg = "15";
        private string _editAngleSpeedHz = "1";
        private string _editAnglePhaseTurns = "0";
        private bool _editAngleLocalSpace = true;

        // Piston
        private string _editPistonAxisX = "0";
        private string _editPistonAxisY = "0";
        private string _editPistonAxisZ = "1";
        private string _editPistonAmplitude = "0.1";
        private string _editPistonSpeedHz = "1";
        private string _editPistonPhaseTurns = "0";
        private bool _editPistonLocalSpace = true;

        private string _lastResolveMissing = string.Empty;
        private float _nextResolveMissingLogTime;
        private float _nextExternalTargetRefreshTime;
        private int _lastExternalFemaleInstanceId;

        private TransformGizmo _selectedGizmo;
        private string _selectedGizmoOwnerId;
        private bool _selectedGizmoUndoCaptured;
        // ギズモ用 Proxy GameObject (scale=1 固定)。HipHijack 等と同じ作法で、
        // 本オブジェクトの scale に影響されないようにギズモはこの Proxy に付ける。
        private GameObject _selectedGizmoProxy;

        private ConfigEntry<bool> _cfgUiVisible;
        private ConfigEntry<bool> _cfgStateVisible;
        private ConfigEntry<bool> _cfgAutoSaveOnMutation;
        private ConfigEntry<bool> _cfgAutoLoadLayoutOnStart;
        private ConfigEntry<bool> _cfgAutoSpawnOnHSceneReady;
        private ConfigEntry<bool> _cfgVerboseLog;
        private ConfigEntry<bool> _cfgEnableSelectedGizmo;
        private ConfigEntry<string> _cfgToggleUiKey;
        private ConfigEntry<string> _cfgToggleStateKey;

        private void Awake()
        {
            _onBlankRoomLeft = OnBlankRoomLeft;
            _instance = this;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            _settingsPath = Path.Combine(_pluginDir, SettingsFileName);
            _layoutPath = Path.Combine(_pluginDir, LayoutFileName);
            InitMotionDiagnostics();

            _fileLogger = new SimpleFileLogger();
            _fileLogger.Initialize(Path.Combine(_pluginDir, PluginName + ".log"));

            LoadSettingsOrCreate();
            InitializeConfigEntries();
            LoadWindowRectsFromSettings();
            InitPresetsPath();
            LoadPresetsOrCreate();
            if (MotionTraceEnabled) TraceComposerEvent("CONFIG", "settingsPath=" + _settingsPath
                + " presetsPath=" + _presetsPath + " settings=" + JsonUtility.ToJson(_settings));

            if (_settings.AutoLoadLayoutOnStart)
            {
                LoadLayoutOrCreate(rebuildRuntime: false);
            }
            else
            {
                SaveLayout();
            }

            _createPrimitiveIndex = Mathf.Max(0, Array.IndexOf(CreatePrimitiveOptions, _settings.DefaultPrimitive));
            if (_createPrimitiveIndex < 0)
            {
                _createPrimitiveIndex = 0;
            }

            InitializeBetterPenetrationFollow();
            LogInfo("起動完了");
            LogInfo("settings=" + _settingsPath);
            LogInfo("layout=" + _layoutPath);
            LogInfo("log=" + (_fileLogger != null ? _fileLogger.LogPath : string.Empty));
        }

        // BlankMapAdd の追加マップ以外では一切発火させない。
        private bool _gateWasInRoom;
        private Action _onBlankRoomLeft;

        private void OnBlankRoomLeft()
        {
            if (MotionTraceEnabled) TraceComposerEvent("GATE_EXIT", "leaving_blank_room");
            RestoreFemaleGhost();
            _vibeParentWeights.Clear();
            RestoreBetterPenetrationTarget();
            DestroyAllRuntimeObjects();
        }

        private void Update()
        {
            TraceUpdateStage("gate_check");
            // 置いたオブジェクトを現実の部屋に持ち出さない。退室時に実体だけ消す
            // (配置データは残るので、部屋へ戻れば同じ構成で復元できる)。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, _onBlankRoomLeft))
            {
                RestoreFemaleGhost();
                TraceUpdateStage("outside_blank_room");
                return;
            }

            FlushMotionPresetSaveIfReleased();

            RegisterUnifiedTabIfAvailable();
            HandleGlobalHotkeys();

            TraceUpdateStage("resolve_runtime");
            bool runtimeReady = TryResolveRuntimeRefs();
            TickFemaleGhost();
            if (runtimeReady)
            {
                RefreshExternalParentTargetsIfNeeded(force: false);
            }
            else
            {
                ClearExternalParentTargets();
            }

            UpdateSelectedGizmoBinding();
            TickSelectedGizmoSync();
            DebugLogRightClickIfAny();
            UpdateUiCaptureState();
            TraceUpdateStage("preset_switch");
            TickAutoSwitchPreset();
            SyncBetterPenetrationTarget();
            TickMovementTransition();
            TraceUpdateStage("preset_apply");
            TickActivePreset();
            LogMovementAppliedIfReady();
            TickMotions(Time.unscaledDeltaTime);
            TraceUpdateStage("ring_orbits");
            TickRotationOrbits();
            TickLinearDrivers();
            TickAngleDrivers();
            TraceUpdateStage("insertion_align");
            AlignVibeRotationObjects();
            TraceUpdateStage("complete");
        }

        private void LateUpdate()
        {
            UpdateUiCaptureState();
            TickComposerDiagnostics();
        }

        private void OnDestroy()
        {
            DisposeFemaleGhost();
            if (_movementEditPreset != null) EndMovementEdit(false);
            DisposeBetterPenetrationFollow();
            try
            {
                RestoreBetterPenetrationTarget();
                StopUiCapture("終了処理");
                ReleaseExternalUiCapture("終了処理");
                DetachSelectedGizmo();
                SaveWindowRectsToSettings();
                SaveSettings();
                SaveLayout();
                DestroyAllRuntimeObjects();
            }
            catch (Exception ex)
            {
                LogError("OnDestroy cleanup failed: " + ex.Message);
            }

            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
            LogInfo("終了");
            DisposeMotionDiagnostics();
        }

        private void HandleGlobalHotkeys()
        {
            if (Bindings.Down("MainGameObjectComposer.objects", _settings.ToggleUiKey.ToString()))
            {
                bool next = !_settings.UiVisible;
                _settings.UiVisible = next;
                if (_cfgUiVisible != null && _cfgUiVisible.Value != next)
                {
                    _cfgUiVisible.Value = next;
                }

                SaveSettings();
                LogInfo("uiVisible=" + _settings.UiVisible);
            }

            if (Bindings.Down("MainGameObjectComposer.state", _settings.ToggleStateKey.ToString()))
            {
                bool next = !_settings.StateVisible;
                _settings.StateVisible = next;
                if (_cfgStateVisible != null && _cfgStateVisible.Value != next)
                {
                    _cfgStateVisible.Value = next;
                }

                SaveSettings();
                LogInfo("stateVisible=" + _settings.StateVisible);
            }
        }

        private void LoadWindowRectsFromSettings()
        {
            _mainWindowRect = new Rect(
                _settings.MainWindowX,
                _settings.MainWindowY,
                Mathf.Max(360f, _settings.MainWindowW),
                Mathf.Max(320f, _settings.MainWindowH));

            _stateWindowRect = new Rect(
                _settings.StateWindowX,
                _settings.StateWindowY,
                Mathf.Max(360f, _settings.StateWindowW),
                Mathf.Max(260f, _settings.StateWindowH));
        }

        private void SaveWindowRectsToSettings()
        {
            _settings.MainWindowX = _mainWindowRect.x;
            _settings.MainWindowY = _mainWindowRect.y;
            _settings.MainWindowW = _mainWindowRect.width;
            _settings.MainWindowH = _mainWindowRect.height;

            _settings.StateWindowX = _stateWindowRect.x;
            _settings.StateWindowY = _stateWindowRect.y;
            _settings.StateWindowW = _stateWindowRect.width;
            _settings.StateWindowH = _stateWindowRect.height;
        }

        private void InitializeConfigEntries()
        {
            InitializeMotionIntensityConfig();
            _cfgUiVisible = Config.Bind("表示", "オブジェクトUI表示", _settings.UiVisible, "オブジェクト管理ウインドウの表示状態");
            _cfgStateVisible = Config.Bind("表示", "状態UI表示", _settings.StateVisible, "体位/状態ウインドウの表示状態");

            _cfgAutoSaveOnMutation = Config.Bind("一般", "編集時に自動保存", _settings.AutoSaveOnMutation, "編集操作ごとにレイアウトを自動保存する");
            _cfgAutoLoadLayoutOnStart = Config.Bind("一般", "起動時にレイアウト読込", _settings.AutoLoadLayoutOnStart, "起動時に前回レイアウトを読み込む");
            _cfgAutoSpawnOnHSceneReady = Config.Bind("一般", "H開始時に配置復元", _settings.AutoSpawnOnHSceneReady, "HScene準備完了時にランタイムを自動再構築する");
            _cfgEnableSelectedGizmo = Config.Bind("一般", "選択オブジェクトのギズモ表示", _settings.EnableSelectedGizmo, "選択オブジェクトにギズモを表示する");
            _cfgVerboseLog = Config.Bind("一般", "詳細ログ", _settings.VerboseLog, "詳細ログを有効にする");

            _cfgToggleUiKey = Config.Bind("Shortcuts", "ToggleObjectUiKey", ToKeyName(_settings.ToggleUiKey, KeyCode.None), "オブジェクト管理UIの表示切替キー (例: F8 / None)");
            _cfgToggleStateKey = Config.Bind("Shortcuts", "ToggleStateUiKey", ToKeyName(_settings.ToggleStateKey, KeyCode.None), "体位/状態UIの表示切替キー (例: F7 / None)");

            _cfgUiVisible.SettingChanged += OnConfigSettingChanged;
            _cfgStateVisible.SettingChanged += OnConfigSettingChanged;
            _cfgAutoSaveOnMutation.SettingChanged += OnConfigSettingChanged;
            _cfgAutoLoadLayoutOnStart.SettingChanged += OnConfigSettingChanged;
            _cfgAutoSpawnOnHSceneReady.SettingChanged += OnConfigSettingChanged;
            _cfgEnableSelectedGizmo.SettingChanged += OnConfigSettingChanged;
            _cfgVerboseLog.SettingChanged += OnConfigSettingChanged;
            _cfgToggleUiKey.SettingChanged += OnConfigSettingChanged;
            _cfgToggleStateKey.SettingChanged += OnConfigSettingChanged;

            ApplyConfigEntriesToSettings(saveSettings: false);
        }

        private void OnConfigSettingChanged(object sender, EventArgs e)
        {
            ApplyConfigEntriesToSettings(saveSettings: true);
        }

        private void ApplyConfigEntriesToSettings(bool saveSettings)
        {
            bool prevEnableGizmo = _settings.EnableSelectedGizmo;

            _settings.UiVisible = _cfgUiVisible != null && _cfgUiVisible.Value;
            _settings.StateVisible = _cfgStateVisible != null && _cfgStateVisible.Value;

            _settings.AutoSaveOnMutation = _cfgAutoSaveOnMutation != null && _cfgAutoSaveOnMutation.Value;
            _settings.AutoLoadLayoutOnStart = _cfgAutoLoadLayoutOnStart != null && _cfgAutoLoadLayoutOnStart.Value;
            _settings.AutoSpawnOnHSceneReady = _cfgAutoSpawnOnHSceneReady != null && _cfgAutoSpawnOnHSceneReady.Value;
            _settings.EnableSelectedGizmo = _cfgEnableSelectedGizmo != null && _cfgEnableSelectedGizmo.Value;
            _settings.VerboseLog = _cfgVerboseLog != null && _cfgVerboseLog.Value;

            _settings.ToggleUiKey = ParseKeyCodeOrFallback(_cfgToggleUiKey != null ? _cfgToggleUiKey.Value : null, KeyCode.None);
            _settings.ToggleStateKey = ParseKeyCodeOrFallback(_cfgToggleStateKey != null ? _cfgToggleStateKey.Value : null, KeyCode.None);

            if (prevEnableGizmo && !_settings.EnableSelectedGizmo)
            {
                DetachSelectedGizmo();
            }

            if (saveSettings)
            {
                SaveSettings();
            }
        }

        private static string ToKeyName(KeyCode keyCode, KeyCode fallback)
        {
            KeyCode resolved = Enum.IsDefined(typeof(KeyCode), keyCode) ? keyCode : fallback;
            return resolved.ToString();
        }

        private static KeyCode ParseKeyCodeOrFallback(string value, KeyCode fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string trimmed = value.Trim();
            if (Enum.TryParse(trimmed, true, out KeyCode parsed) && Enum.IsDefined(typeof(KeyCode), parsed))
            {
                return parsed;
            }
            return fallback;
        }

        private void LogInfo(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (_cfgVerboseLog == null || !_cfgVerboseLog.Value) return;
            Logger.LogInfo("[" + PluginName + "] " + message);
            _fileLogger?.Write("INFO", message);
        }

        private void LogWarn(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger.LogWarning("[" + PluginName + "] " + message);
            _fileLogger?.Write("WARN", message);
        }

        private void LogError(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger.LogError("[" + PluginName + "] " + message);
            _fileLogger?.Write("ERROR", message);
        }

        private void LogDebug(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (!_settings.VerboseLog)
            {
                return;
            }

            Logger.LogInfo("[" + PluginName + "] " + message);
            _fileLogger?.Write("DEBUG", message);
        }

        private void LogResolveMissing(string missingKey)
        {
            float now = Time.unscaledTime;
            if (string.Equals(_lastResolveMissing, missingKey, StringComparison.Ordinal) && now < _nextResolveMissingLogTime)
            {
                return;
            }

            _lastResolveMissing = missingKey;
            _nextResolveMissingLogTime = now + 1f;
            LogDebug("resolve missing: " + missingKey);
        }
    }
}
