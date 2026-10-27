using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using HarmonyLib;
using MainGameLogRelay;
using MainGameTransformGizmo;
using MainGameUiInputCapture;
using MainGirlHipHijack.Services;
using MainGirlHipHijack.Runtime;
using MainGirlHipHijack.Adapters;
using RootMotion.FinalIK;
using UnityEngine;
using VRGIN.Controls;
using VRGIN.Core;
using Valve.VR;
using CanonPlugins.Shared;

namespace MainGirlHipHijack
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInDependency(MainGameLogRelay.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MainGameTransformGizmo.Plugin.GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MainGameUiInputCapture.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed partial class Plugin : BaseUnityPlugin, IFramePipelinePort
    {
        public const string Guid = "com.kks.main.girlbodyikgizmo";
        public const string PluginName = "MainGirlHipHijack";
        public const string Version = "1.0.0";
        private const string RelayOwner = Guid;
        private const string RelayLogKey = "main/" + PluginName;
        private const string InputCaptureRelayLogKey = "main/" + PluginName + ".input";
        private const string InputCaptureOwnerKey = Guid + ".input";
        private const string InputCaptureSourceGizmo = "gizmo-drag";
        private const string InputCaptureSourceWindow = "window-drag";
        private const string InputCaptureSourceSlider = "slider-drag";
        private const string InputCaptureSourceScroll = "scroll-drag";

        internal static Plugin Instance
        {
            get
            {
                return Chainloader.PluginInfos.TryGetValue(Guid, out PluginInfo info)
                    ? info.Instance as Plugin
                    : null;
            }
        }

        // 肩補正側など外部プラグイン向け: 腕IKの実行状態変化通知
        // idx は BIK_LH / BIK_RH（0/1）を想定。
        private event Action<int, bool> _armIkRunningChanged;
        public static event Action<int, bool> ArmIkRunningChanged
        {
            add
            {
                Plugin instance = Instance;
                if (instance != null) instance._armIkRunningChanged += value;
            }
            remove
            {
                Plugin instance = Instance;
                if (instance != null) instance._armIkRunningChanged -= value;
            }
        }

        internal const int BIK_LH = 0;
        internal const int BIK_RH = 1;
        internal const int BIK_LF = 2;
        internal const int BIK_RF = 3;
        internal const int BIK_LS = 4;
        internal const int BIK_RS = 5;
        internal const int BIK_LT = 6;
        internal const int BIK_RT = 7;
        internal const int BIK_LE = 8;
        internal const int BIK_RE = 9;
        internal const int BIK_LK = 10;
        internal const int BIK_RK = 11;
        internal const int BIK_BODY = 12;
        internal const int BIK_TOTAL = 13;
        private const int BIK_BEND_START = 8;
        private const int FemaleHandLeft = 0;
        private const int FemaleHandRight = 1;
        private static readonly bool MaleFeaturesTemporarilySealed = true;
        private const int WindowId = 0x4D47424A;

        private static readonly string[] BIK_Labels =
        {
            "左手 (L.Hand)",    "右手 (R.Hand)",
            "左足 (L.Foot)",    "右足 (R.Foot)",
            "左肩 (L.Shoulder)","右肩 (R.Shoulder)",
            "左腰 (L.Thigh)",   "右腰 (R.Thigh)",
            "左肘 (L.Elbow)",   "右肘 (R.Elbow)",
            "左膝 (L.Knee)",    "右膝 (R.Knee)",
            "腰中央 (Body)",
        };

        private readonly HSceneSessionTracker _hSceneSessionTracker = new HSceneSessionTracker();
        private HSceneSessionState _hSceneSession => _hSceneSessionTracker.Session;
        private FemaleCharacterSession _femaleSession => _hSceneSessionTracker.Female;
        private readonly MaleCharacterSession _maleSession = new MaleCharacterSession();
        private readonly HipSpeedRuntimeState _hipSpeedState = new HipSpeedRuntimeState();
        private readonly BodyControlLinkState _bodyControlState = new BodyControlLinkState();
        private readonly FollowHmdRuntimeState _followHmdState = new FollowHmdRuntimeState();
        private HipHijackLogger _hipHijackLogger;
        private readonly BodyIkStateStore _bodyIkState = new BodyIkStateStore(BIK_TOTAL);
        private BodyIkOperationPointState[] _bikEff => _bodyIkState.OperationPoints;
        private bool[] _bikWant => _bodyIkState.Wanted;
        private float[] _bikWeight => _bodyIkState.Weights;
        private readonly HipHijackUiState _uiState = new HipHijackUiState();
        private readonly InputCaptureState _inputCaptureState = new InputCaptureState();
        private string _femaleLeftHandPoseIndexText { get => _uiState.FemaleLeftHandPoseIndexText; set => _uiState.FemaleLeftHandPoseIndexText = value; }
        private string _femaleRightHandPoseIndexText { get => _uiState.FemaleRightHandPoseIndexText; set => _uiState.FemaleRightHandPoseIndexText = value; }

        private Harmony _harmony;
        private FrameCoordinator _frameCoordinator;
        private readonly SteamVrPoseProvider _steamVrPoseProvider =
            new SteamVrPoseProvider(new SteamVrCoordinateConverter());
        private readonly UnifiedUiGateway _unifiedUiGateway = new UnifiedUiGateway();
        private readonly ObjectComposerGateway _objectComposerGateway = new ObjectComposerGateway();
        private readonly ComposerFollowController _composerFollowController = new ComposerFollowController();
        private readonly HipSpeedController _hipSpeedController = new HipSpeedController();
        private readonly FemaleAnimationSpeedInterceptor _femaleAnimationSpeedInterceptor =
            new FemaleAnimationSpeedInterceptor();
        private readonly GameReflectionMap _gameReflection = new GameReflectionMap();
        private readonly AnimatorSynchronizationController _animatorSynchronizationController =
            new AnimatorSynchronizationController();
        private SettingsCoordinator _settingsCoordinator;
        private BodyIkGizmoSettings _settings => _settingsCoordinator != null ? _settingsCoordinator.Settings : null;
        private ConfigEntry<bool> _cfgPluginEnabled;
        private ConfigEntry<bool> _cfgUiVisible;
        private ConfigEntry<bool> _cfgRelayLogEnabled;
        private ConfigEntry<BepInEx.Configuration.KeyboardShortcut> _cfgHipQuickSetupKey;
        private bool _syncingDetailLogSwitch;
        private string _pluginDir;

        private Rect _windowRect { get => _uiState.WindowRect; set => _uiState.WindowRect = value; }
        private Vector2 _scroll { get => _uiState.Scroll; set => _uiState.Scroll = value; }
        private string _lastResolveMissing;
        private float _nextResolveMissingLogTime;
        private bool _windowDragging { get => _inputCaptureState.WindowDragging; set => _inputCaptureState.WindowDragging = value; }
        private bool _sliderDragging { get => _inputCaptureState.SliderDragging; set => _inputCaptureState.SliderDragging = value; }
        private int _sliderDraggingIndex { get => _inputCaptureState.SliderDraggingIndex; set => _inputCaptureState.SliderDraggingIndex = value; }
        private bool _scrollDragging { get => _inputCaptureState.ScrollDragging; set => _inputCaptureState.ScrollDragging = value; }
        private bool _gizmoDragging { get => _inputCaptureState.GizmoDragging; set => _inputCaptureState.GizmoDragging = value; }
        private readonly BodyIkLifecycleState _bodyIkLifecycle = new BodyIkLifecycleState(BIK_TOTAL);
        private float[] _nextOffLeakWarnTime => _bodyIkLifecycle.NextOffLeakWarningTimes;
        private float[] _nextOnRebindWarnTime => _bodyIkLifecycle.NextOnRebindWarningTimes;
        private float _nextFbbikReenableWarnTime { get => _bodyIkLifecycle.NextFbbikReenableWarningTime; set => _bodyIkLifecycle.NextFbbikReenableWarningTime = value; }
        private bool _abandonedByPostureChange { get => _bodyIkLifecycle.AbandonedByPostureChange; set => _bodyIkLifecycle.AbandonedByPostureChange = value; }
        private bool _pendingAbandonByPostureChange { get => _bodyIkLifecycle.PendingPostureAbandon; set => _bodyIkLifecycle.PendingPostureAbandon = value; }
        private string _pendingAbandonTrigger { get => _bodyIkLifecycle.PendingPostureAbandonTrigger; set => _bodyIkLifecycle.PendingPostureAbandonTrigger = value; }
        private float _pendingAbandonRequestTime { get => _bodyIkLifecycle.PendingPostureAbandonRequestTime; set => _bodyIkLifecycle.PendingPostureAbandonRequestTime = value; }
        private const float PendingAbandonFallbackDelaySeconds = 0.2f;
        private const int BodyIkPostDragHoldFrames = 2;
        private readonly VrInteractionState _vrInteractionState = new VrInteractionState();
        private bool _vrGrabMode { get => _vrInteractionState.GrabMode; set => _vrInteractionState.GrabMode = value; }
        private Controller.Lock _vrGrabLockLeft { get => _vrInteractionState.LeftGrabLock; set => _vrInteractionState.LeftGrabLock = value; }
        private Controller.Lock _vrGrabLockRight { get => _vrInteractionState.RightGrabLock; set => _vrInteractionState.RightGrabLock = value; }
        private int _vrLeftGrabIdx { get => _vrInteractionState.LeftGrabIndex; set => _vrInteractionState.LeftGrabIndex = value; }
        private int _vrRightGrabIdx { get => _vrInteractionState.RightGrabIndex; set => _vrInteractionState.RightGrabIndex = value; }
        private bool _bodyCtrlLinkEnabled { get => _vrInteractionState.BodyControlLinkEnabled; set => _vrInteractionState.BodyControlLinkEnabled = value; }
        private Controller.Lock _vrBodyCtrlLock { get => _vrInteractionState.BodyControlLock; set => _vrInteractionState.BodyControlLock = value; }

        // HipQuickSetup トグル状態
        private readonly HipQuickSetupState _hipQuickSetupState = new HipQuickSetupState();
        private bool _hipQuickSetupActive { get => _hipQuickSetupState.Active; set => _hipQuickSetupState.Active = value; }
        private bool[] _hipQuickSetupSavedWant { get => _hipQuickSetupState.SavedWanted; set => _hipQuickSetupState.SavedWanted = value; }
        private bool _hipQuickSetupTogglePending { get => _hipQuickSetupState.TogglePending; set => _hipQuickSetupState.TogglePending = value; }
        // 左コントローラー非表示
        private Renderer[] _leftCtrlRenderers { get => _vrInteractionState.HiddenControllerRenderers; set => _vrInteractionState.HiddenControllerRenderers = value; }
        private bool[] _leftCtrlRendererEnabled { get => _vrInteractionState.HiddenControllerRendererStates; set => _vrInteractionState.HiddenControllerRendererStates = value; }
        private Collider[] _leftCtrlColliders { get => _vrInteractionState.HiddenControllerColliders; set => _vrInteractionState.HiddenControllerColliders = value; }
        private bool[] _leftCtrlColliderEnabled { get => _vrInteractionState.HiddenControllerColliderStates; set => _vrInteractionState.HiddenControllerColliderStates = value; }
        private bool _leftCtrlHidden { get => _vrInteractionState.ControllerHidden; set => _vrInteractionState.ControllerHidden = value; }
        // GUI通知
        private string _guiNotifyText { get => _uiState.NotificationText; set => _uiState.NotificationText = value; }
        private float _guiNotifyEndTime { get => _uiState.NotificationEndTime; set => _uiState.NotificationEndTime = value; }
        private const float GuiNotifyDurationSeconds = 2.5f;

        private readonly ShoulderStabilizerGateway _shoulderStabilizerGateway =
            new ShoulderStabilizerGateway();
        private float _nextStateHeartbeatTime;
        private readonly BodyIkDiagnosticState _bodyIkDiagnosticState = new BodyIkDiagnosticState();
        private float _nextBodyIkDiagLogTime { get => _bodyIkDiagnosticState.NextLogTime; set => _bodyIkDiagnosticState.NextLogTime = value; }
        private int _lastBodyIkDiagFrame { get => _bodyIkDiagnosticState.LastFrame; set => _bodyIkDiagnosticState.LastFrame = value; }
        private int _lastBodyIkDiagIndex { get => _bodyIkDiagnosticState.LastIndex; set => _bodyIkDiagnosticState.LastIndex = value; }
        private Vector3 _lastBodyIkDiagPostBonePos { get => _bodyIkDiagnosticState.LastPostBonePosition; set => _bodyIkDiagnosticState.LastPostBonePosition = value; }
        private Vector3 _lastBodyIkDiagPostProxyPos { get => _bodyIkDiagnosticState.LastPostProxyPosition; set => _bodyIkDiagnosticState.LastPostProxyPosition = value; }
        private bool _lastBodyIkDiagHasPost { get => _bodyIkDiagnosticState.HasLastPost; set => _bodyIkDiagnosticState.HasLastPost = value; }
        private readonly FemalePosePresetState<PosePresetRuntime, PoseTransitionPoint> _femalePosePresetState =
            new FemalePosePresetState<PosePresetRuntime, PoseTransitionPoint>();
        private List<PosePresetRuntime> _posePresets => _femalePosePresetState.Presets;
        private Dictionary<string, Texture2D> _posePresetThumbCache => _femalePosePresetState.ThumbnailCache;
        private string _posePresetRootDir { get => _femalePosePresetState.RootDirectory; set => _femalePosePresetState.RootDirectory = value; }
        private string _posePresetShotsDir { get => _femalePosePresetState.ShotsDirectory; set => _femalePosePresetState.ShotsDirectory = value; }
        private string _posePresetIndexPath { get => _femalePosePresetState.IndexPath; set => _femalePosePresetState.IndexPath = value; }
        private string _posePresetNameDraft { get => _uiState.FemalePosePresetNameDraft; set => _uiState.FemalePosePresetNameDraft = value; }
        private bool _posePresetsLoaded { get => _femalePosePresetState.IsLoaded; set => _femalePosePresetState.IsLoaded = value; }
        private bool _posePresetThumbDirty { get => _femalePosePresetState.ThumbnailDirty; set => _femalePosePresetState.ThumbnailDirty = value; }
        private string _lastThumbClickedPresetId { get => _femalePosePresetState.LastClickedPresetId; set => _femalePosePresetState.LastClickedPresetId = value; }
        private float _lastThumbClickTime { get => _femalePosePresetState.LastClickTime; set => _femalePosePresetState.LastClickTime = value; }
        private readonly PoseAutomationState _poseAutomationState = new PoseAutomationState();
        private bool _autoPosePendingApply { get => _poseAutomationState.PendingApply; set => _poseAutomationState.PendingApply = value; }
        private int _autoPoseLastAnimatorStateHash { get => _poseAutomationState.LastAnimatorStateHash; set => _poseAutomationState.LastAnimatorStateHash = value; }
        private int _autoPoseLastAnimatorLoop { get => _poseAutomationState.LastAnimatorLoop; set => _poseAutomationState.LastAnimatorLoop = value; }
        private bool _autoPoseLoopReady { get => _poseAutomationState.LoopReady; set => _poseAutomationState.LoopReady = value; }
        private int _autoPoseLoopCountSinceSwitch { get => _poseAutomationState.LoopCountSinceSwitch; set => _poseAutomationState.LoopCountSinceSwitch = value; }
        private string _autoPoseLastAppliedPresetId { get => _poseAutomationState.LastFemalePresetId; set => _poseAutomationState.LastFemalePresetId = value; }
        private string _autoMalePoseLastAppliedPresetId { get => _poseAutomationState.LastMalePresetId; set => _poseAutomationState.LastMalePresetId = value; }
        private string _poseTransitionPresetId { get => _femalePosePresetState.TransitionPresetId; set => _femalePosePresetState.TransitionPresetId = value; }
        private Coroutine _poseTransitionCoroutine { get => _femalePosePresetState.TransitionCoroutine; set => _femalePosePresetState.TransitionCoroutine = value; }
        private List<PoseTransitionPoint> _activeTransitionPoints { get => _femalePosePresetState.ActiveTransitionPoints; set => _femalePosePresetState.ActiveTransitionPoints = value; }
        private const float PoseThumbnailDoubleClickWindow = 0.35f;
        private readonly MalePosePresetState<MalePosePresetItem> _malePosePresetState =
            new MalePosePresetState<MalePosePresetItem>();
        private List<MalePosePresetItem> _malePosePresets => _malePosePresetState.Presets;
        private Dictionary<string, Texture2D> _malePosePresetThumbCache => _malePosePresetState.ThumbnailCache;
        private string _malePosePresetRootDir { get => _malePosePresetState.RootDirectory; set => _malePosePresetState.RootDirectory = value; }
        private string _malePosePresetShotsDir { get => _malePosePresetState.ShotsDirectory; set => _malePosePresetState.ShotsDirectory = value; }
        private string _malePosePresetIndexPath { get => _malePosePresetState.IndexPath; set => _malePosePresetState.IndexPath = value; }
        private string _malePosePresetNameDraft { get => _uiState.MalePosePresetNameDraft; set => _uiState.MalePosePresetNameDraft = value; }
        private bool _malePosePresetsLoaded { get => _malePosePresetState.IsLoaded; set => _malePosePresetState.IsLoaded = value; }
        private bool _malePosePresetThumbDirty { get => _malePosePresetState.ThumbnailDirty; set => _malePosePresetState.ThumbnailDirty = value; }
        private string _lastMaleThumbClickedPresetId { get => _malePosePresetState.LastClickedPresetId; set => _malePosePresetState.LastClickedPresetId = value; }
        private float _lastMaleThumbClickTime { get => _malePosePresetState.LastClickTime; set => _malePosePresetState.LastClickTime = value; }

        private void Awake()
        {
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            _hipHijackLogger = new HipHijackLogger(
                _pluginDir,
                PluginName,
                RelayOwner,
                RelayLogKey,
                InputCaptureOwnerKey,
                InputCaptureRelayLogKey,
                Logger,
                () => _cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value,
                () => _settings != null && _settings.VerboseLog);
            PreconfigureRelayLogRoutingEarly();
            _settingsCoordinator = SettingsCoordinator.Load(
                _pluginDir,
                LogInfo,
                LogWarn,
                LogError,
                () => Time.unscaledTime);
            BindConfig();
            EnsureUiHiddenOnStartup();
            InitPosePresetStorage();
            InitMalePosePresetStorage();

            InitBodyIK();
            ApplySettingsToRuntime();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(PluginPatches));
            _frameCoordinator = new FrameCoordinator(this);

            ApplyPluginEnabledState(IsPluginEnabled(), "awake");
            LogInfo("loaded");
            LogInfo("settings=" + Path.Combine(_pluginDir, SettingsStore.FileName));
            LogInfo("uiVisible=" + _settings.UiVisible);
            LogStateSnapshot("awake-init", force: true);
        }

        public static bool IsArmIkRunning(int idx)
        {
            Plugin instance = Instance;
            if (instance == null)
                return false;
            if (idx < BIK_LH || idx > BIK_RH)
                return false;

            BodyIkOperationPointState state = instance._bikEff[idx];
            return state != null && state.Running;
        }

        private void NotifyArmIkRunningChanged(int idx, bool running)
        {
            if (idx != BIK_LH && idx != BIK_RH)
                return;

            try
            {
                _armIkRunningChanged?.Invoke(idx, running);
            }
            catch
            {
                // 通知先例外で本体処理を止めない
            }
        }

        private void SyncShoulderStabilizerEnabledFromArmState(string reason)
        {
            bool anyArmRunning =
                (_bikEff[BIK_LH] != null && _bikEff[BIK_LH].Running) ||
                (_bikEff[BIK_RH] != null && _bikEff[BIK_RH].Running);
            TrySetShoulderStabilizerEnabled(anyArmRunning, reason);
        }

        private void TrySetShoulderStabilizerEnabled(bool enabled, string reason)
        {
            _shoulderStabilizerGateway.SetEnabled(
                enabled,
                reason,
                Time.frameCount,
                _settings != null && _settings.DetailLogEnabled,
                _settings != null && _settings.BodyIkDiagnosticLog,
                LogInfo,
                LogWarn,
                LogBodyIkDiag,
                LogBodyIkDiagWarn);
        }

        private string BuildShoulderLinkDiagStateText()
        {
            return _shoulderStabilizerGateway.BuildDiagnosticStateText();
        }

        private void PreconfigureRelayLogRoutingEarly()
        {
            _hipHijackLogger?.PreconfigureRelayRouting();
        }

        private void TickSpeedHijack()
        {
            _hipSpeedController.Tick(
                _settings,
                _hipSpeedState,
                GetHFlags(),
                _femaleSession.Character,
                GetSpeedTrackingPosition(),
                Time.unscaledDeltaTime,
                Time.unscaledTime,
                LogDebug,
                LogInfo);
        }

        private Vector3 GetSpeedTrackingPosition()
        {
            if (_bikEff[BIK_BODY].Running && _bikEff[BIK_BODY].Proxy != null)
                return _bikEff[BIK_BODY].Proxy.position - _vibeBodyApplied;
            if (_femaleSession.FullBodyIk != null && _femaleSession.FullBodyIk.solver.bodyEffector.bone != null)
                return _femaleSession.FullBodyIk.solver.bodyEffector.bone.position;
            return Vector3.zero;
        }

        private HFlag GetHFlags()
        {
            if (_hSceneSession.Process == null)
                return null;
            return GetMemberValue(
                _hSceneSession.Process,
                _gameReflection.HSceneFlags,
                _gameReflection.HSceneFlagsProperty) as HFlag;
        }

        internal bool TryApplyFemaleAnimSpeedCut(
            HActionBase action,
            string parameter,
            float value,
            bool isMale,
            bool isFemale1)
        {
            return _femaleAnimationSpeedInterceptor.TryApply(
                action,
                parameter,
                value,
                isMale,
                isFemale1,
                _bodyCtrlLinkEnabled,
                _settings != null && _settings.CutFemaleAnimSpeedEnabled,
                _gameReflection);
        }

        internal bool TryApplyFemaleAnimSpeedCut(ChaControl female, string parameter, float value)
        {
            return _femaleAnimationSpeedInterceptor.TryApply(
                female,
                parameter,
                value,
                _bodyCtrlLinkEnabled,
                _settings != null && _settings.CutFemaleAnimSpeedEnabled);
        }

        private void SyncResetAnimators()
        {
            _animatorSynchronizationController.ResetLoopAnimators(
                _femaleSession.Character,
                _maleSession.TargetMaleCha,
                LogDebug);
        }

        private void OnDestroy()
        {
            DisableBodyCtrlLink();
            if (_vrGrabMode) ExitVRGrabMode();
            StopPoseTransitionIfRunning();
            DisableAllBodyIK(silent: true);
            ClearMaleRefs();
            ResetCachedBodyIkBindings();
            ReleaseAllInputCapture();
            _harmony?.UnpatchSelf();
            _harmony = null;
            DisposePosePresetThumbCache();
            DisposeMalePosePresetThumbCache();
            SaveSettings(immediate: true);
            LogInfo("destroyed");
        }

        private bool IsPluginEnabled()
        {
            return _cfgPluginEnabled == null || _cfgPluginEnabled.Value;
        }

        private void ApplyPluginEnabledState(bool enabled, string reason)
        {
            if (enabled)
            {
                LogInfo("plugin enabled via ConfigManager reason=" + reason);
                LogStateSnapshot("plugin-enabled", force: true);
                return;
            }

            StopPoseTransitionIfRunning();
            DisableAllBodyIK(silent: true);
            ReleaseAllInputCapture();

            _abandonedByPostureChange = false;
            _pendingAbandonByPostureChange = false;
            _pendingAbandonTrigger = null;
            _pendingAbandonRequestTime = 0f;
            ResetBodyIkDiagnosticsState();

            LogInfo("plugin disabled via ConfigManager reason=" + reason);
            LogStateSnapshot("plugin-disabled", force: true);
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            _frameCoordinator?.UpdateFrame();
        }

        private void RunFramePreamble()
        {
            if (_settings != null && _settings.VerboseLog && Time.unscaledTime >= _nextStateHeartbeatTime)
            {
                _nextStateHeartbeatTime = Time.unscaledTime + 1f;
                LogStateSnapshot("heartbeat");
            }

            // 字幕の入力欄で打っている最中のキーは向こうのもの。
            // 既定のショートカットが Enter なので、見ないと送信のたびに誤爆する。
            bool keyboardToggleDown = _cfgHipQuickSetupKey != null
                && !TextInputGate.IsTextInputActive
                && Bindings.Down("MainGirlHipHijack.quick", _cfgHipQuickSetupKey.Value.ToString());
            bool vrToggleDown = CheckVRHipQuickSetupInput();
            if (keyboardToggleDown || vrToggleDown)
                _hipQuickSetupTogglePending = true;

            UpdateUiDraggingStateByMouseRelease();
            UpdateInputCaptureApiState();
            if (VR.Active)
            {
                _steamVrPoseProvider.TickDeviceScan(Time.unscaledTime);
                _steamVrPoseProvider.FetchPoses();
            }
            UpdateFemaleHeadVRInput();
            UpdateFemaleHeadAngleGizmo();
        }

        private bool TryPrepareResolvedFrame()
        {
            if (!TryResolveRuntimeRefs())
                return false;

            if (_pendingAbandonByPostureChange)
            {
                TryFlushPendingAbandonByPostureChange("update-timeout", force: false);
                if (_pendingAbandonByPostureChange)
                    return false;
            }

            return true;
        }

        private void RunResolvedFrame()
        {
            ProcessAutoPoseRuntime();

            if (_hipQuickSetupTogglePending)
            {
                _hipQuickSetupTogglePending = false;
                ExecuteHipQuickSetup();
            }

            if (_vrGrabMode)
                HandleVRGrab();

            bool clearedStaleWant = false;
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                // Posture change直後は、UIで明示ONするまで暗黙の再有効化を許可しない。
                if (_abandonedByPostureChange && _bikWant[i])
                {
                    if (_settings != null && _settings.VerboseLog)
                        LogWarn("drop stale want after posture change idx=" + i);

                    _bikWant[i] = false;
                    if (_settings != null && _settings.Enabled != null && i < _settings.Enabled.Length)
                        _settings.Enabled[i] = false;
                    clearedStaleWant = true;
                    continue;
                }

                if (_bikWant[i] && !_bikEff[i].Running)
                    DoEnableBodyIK(i);
                else if (!_bikWant[i] && _bikEff[i].Running)
                    DoDisableBodyIK(i, preserveProxy: true);
            }

            if (clearedStaleWant)
            {
                SaveSettings();
                LogStateSnapshot("drop-stale-want", force: true);
            }
        }

        private bool CheckVRHipQuickSetupInput()
        {
            if (!VR.Active || VR.Mode == null)
                return false;

            // どちらかのコントローラーでトリガー+Bボタン(Axis0)同時押し
            return CheckVRComboDown(VR.Mode.Left) || CheckVRComboDown(VR.Mode.Right);
        }

        private static bool CheckVRComboDown(Controller ctrl)
        {
            if (ctrl == null || ctrl.Input == null)
                return false;

            var input = ctrl.Input;
            return Bindings.VrDown("MainGirlHipHijack.vrQuick", input, "Axis0 + Trigger");
        }

        private void ExecuteHipQuickSetup()
        {
            if (_settings == null)
                return;

            bool hipBodyActive = _bikWant[BIK_BODY] || (_bikEff[BIK_BODY] != null && _bikEff[BIK_BODY].Running);
            if (!_hipQuickSetupActive && hipBodyActive)
                _hipQuickSetupActive = true;

            if (!_hipQuickSetupActive || !hipBodyActive)
            {
                // ── 初回 or 再有効化: フルセットアップ ──
                _settings.SpeedHijackEnabled = true;
                _settings.CutFemaleAnimSpeedEnabled = true;
                _hipQuickSetupSavedWant = null;

                // 腰IKはON処理のたびに必ず有効化する
                ResetBodyIKPartToAnimationPose(BIK_BODY, stopPoseTransition: true, logSnapshot: false, applyImmediate: true);
                SetBodyIK(BIK_BODY, on: true, saveSettings: false, reason: "quick-setup");
                SetGizmoVisible(BIK_BODY, on: false, saveSettings: false);
                SetBodyIKWeight(BIK_BODY, weight: 1f, saveSettings: false);

                if (VR.Active && !_bodyCtrlLinkEnabled)
                    ToggleBodyCtrlLink();

                _hipQuickSetupActive = true;
                SaveSettings();
                ShowGuiNotify("HipSetup ON");
                LogInfo("hip quick setup ON");
            }
            else
            {
                // ── 2回目: 腰IK OFF + 腰リンクOFF + 左コン復帰 ──
                _hipQuickSetupSavedWant = null;

                // 腰中央IKのみOFF（他IKには触れない）
                ResetBodyIKPartToAnimationPose(BIK_BODY, stopPoseTransition: false, logSnapshot: false, applyImmediate: true);
                SetBodyIK(BIK_BODY, on: false, saveSettings: false, reason: "quick-setup-off");

                // SpeedHijack / CutFemaleAnimSpeed OFF
                _settings.SpeedHijackEnabled = false;
                _settings.CutFemaleAnimSpeedEnabled = false;

                // 男女アニメ同期リセット（先頭に巻き戻し）
                SyncResetAnimators();

                // 腰リンクOFF
                DisableBodyCtrlLink();
                // 左コン復帰
                RestoreLeftControllerVisuals();

                _hipQuickSetupActive = false;
                SaveSettings();
                ShowGuiNotify("HipSetup OFF");
                LogInfo("hip quick setup OFF");
            }
        }

        private void ShowGuiNotify(string text)
        {
            _guiNotifyText = text;
            _guiNotifyEndTime = Time.unscaledTime + GuiNotifyDurationSeconds;
        }

        internal void OnAfterHSceneLateUpdate(HSceneProc proc)
        {
            _frameCoordinator?.AfterHSceneLateUpdate(proc);
        }

        internal void OnAfterHSceneChangeAnimator(HSceneProc proc)
        {
            _frameCoordinator?.AfterHSceneAnimatorChanged(proc);
        }

        private void HandleHSceneAnimatorChanged(HSceneProc proc)
        {
            if (_hSceneSession.Process == null || !_hSceneSession.Process)
                _hSceneSession.Process = proc;

            if (_settings != null && _settings.VerboseLog)
            {
                int currentId = _hSceneSession.Process != null ? _hSceneSession.Process.GetInstanceID() : 0;
                LogInfo("hook ChangeAnimator pending=" + _pendingAbandonByPostureChange + " proc=" + proc.GetInstanceID() + " current=" + currentId);
            }

            TryFlushPendingAbandonByPostureChange("changeAnimator", force: true);
        }

        void IFramePipelinePort.RegisterUnifiedUi() => _unifiedUiGateway.Register(DrawUnifiedContent);
        void IFramePipelinePort.FlushSettings() => FlushPendingSettingsSave();
        bool IFramePipelinePort.IsEnabled() => IsPluginEnabled();
        void IFramePipelinePort.RunFramePreamble() => RunFramePreamble();
        bool IFramePipelinePort.TryPrepareResolvedFrame() => TryPrepareResolvedFrame();
        void IFramePipelinePort.RunResolvedFrame() => RunResolvedFrame();

        bool IFramePipelinePort.CanRunAfterHSceneLateUpdate(HSceneProc process)
        {
            return IsPluginEnabled()
                && process != null
                && _hSceneSession.Process
                && ReferenceEquals(process, _hSceneSession.Process);
        }

        void IFramePipelinePort.ApplyFemaleBodyIkAfterAnimation() => OnAfterHSceneLateUpdateBodyIK();
        void IFramePipelinePort.UpdateBodyControlLinkAfterAnimation() => UpdateBodyCtrlLink(Time.unscaledDeltaTime);
        void IFramePipelinePort.ApplyVibeReactionAfterAnimation() => ApplyVibeReaction();
        void IFramePipelinePort.UpdateHipSpeedAfterAnimation() => TickSpeedHijack();
        void IFramePipelinePort.UpdateMaleBodyAfterAnimation() => UpdateMaleHMD();
        void IFramePipelinePort.ApplyFemaleHeadAfterAnimation() => ApplyFemaleHeadAdditiveRot();
        void IFramePipelinePort.HandleHSceneAnimatorChanged(HSceneProc process) => HandleHSceneAnimatorChanged(process);

        private void SaveSettings(bool immediate = false)
        {
            _settingsCoordinator?.RequestSave(immediate);
        }

        private void EnsureUiHiddenOnStartup()
        {
            if (_settings == null)
                return;

            if (_cfgUiVisible != null)
            {
                if (_cfgUiVisible.Value)
                {
                    _cfgUiVisible.Value = false;
                    LogInfo("uiVisible forced OFF on startup (config)");
                    return;
                }
            }

            if (_settings.UiVisible)
            {
                _settings.UiVisible = false;
                SaveSettings();
                LogInfo("uiVisible forced OFF on startup (settings)");
            }
        }

        private void FlushPendingSettingsSave()
        {
            bool flushed = _settingsCoordinator != null && _settingsCoordinator.FlushIfDue();
            if (flushed && _settings != null && _settings.VerboseLog)
                LogStateSnapshot("settings-flush");
        }

        private void BindConfig()
        {
            _cfgPluginEnabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable this plugin runtime. false disables IK handling and UI.");

            _cfgPluginEnabled.SettingChanged += (_, __) =>
            {
                ApplyPluginEnabledState(_cfgPluginEnabled.Value, "config-changed");
            };

            _cfgUiVisible = Config.Bind(
                "UI",
                "Visible",
                _settings != null && _settings.UiVisible,
                "UI表示のON/OFF（ConfigManagerから変更可）");

            _cfgUiVisible.SettingChanged += (_, __) =>
            {
                _settings.UiVisible = _cfgUiVisible.Value;
                SaveSettings();
                LogInfo("uiVisible changed via ConfigManager: " + _settings.UiVisible);
                LogStateSnapshot("ui-visible-config", force: true);
            };

            _cfgRelayLogEnabled = Config.Bind(
                "Logging",
                "EnableLogs",
                _settings != null && _settings.DetailLogEnabled,
                "MainGameLogRelay経由ログのON/OFF（HipHijack本体＋input owner）");

            _cfgRelayLogEnabled.SettingChanged += (_, __) =>
            {
                SetDetailLoggingEnabled(_cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value, "config-changed");
            };

            _cfgHipQuickSetupKey = Config.Bind(
                "Shortcut",
                "HipQuickSetupKey",
                new BepInEx.Configuration.KeyboardShortcut(KeyCode.Return),
                "腰IK即時セットアップのショートカットキー（SpeedHijack ON + 腰IK有効化）");

            _settings.UiVisible = _cfgUiVisible.Value;
            SetDetailLoggingEnabled(_cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value, "bind-init");
        }

        private void ApplyRelayLoggingState()
        {
            bool enabled = _cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value;
            _hipHijackLogger?.ApplyRelayState(enabled);
        }

        private void SetDetailLoggingEnabled(bool enabled, string reason)
        {
            if (_syncingDetailLogSwitch)
                return;

            _syncingDetailLogSwitch = true;
            try
            {
                bool settingsChanged = _settings != null && _settings.DetailLogEnabled != enabled;
                if (_settings != null)
                    _settings.DetailLogEnabled = enabled;

                bool cfgChanged = _cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value != enabled;
                if (cfgChanged)
                    _cfgRelayLogEnabled.Value = enabled;

                ApplyRelayLoggingState();
                if (UiInputCaptureApi.IsAvailable)
                    UiInputCaptureApi.SetOwnerDebug(InputCaptureOwnerKey, enabled);

                if (settingsChanged)
                    SaveSettings();

                if (_settings != null && _settings.VerboseLog)
                    LogInfo("detail/relay logs " + (enabled ? "ON" : "OFF")
                        + " reason=" + reason
                        + " cfgChanged=" + cfgChanged
                        + " settingsChanged=" + settingsChanged);
            }
            finally
            {
                _syncingDetailLogSwitch = false;
            }
        }

        private void ApplySettingsToRuntime()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                _bikWant[i] = _settings.Enabled[i];
                _bikWeight[i] = Mathf.Clamp01(_settings.Weights[i]);
            }

            SyncFemaleHandPoseTextFromSettings();
        }

        private void InitBodyIK()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                _bikWeight[i] = 1f;
                if (i >= BIK_BEND_START && i != BIK_BODY)
                {
                    _bikEff[i].IsBendGoal = true;
                    _bikEff[i].BendChain = BIK_IndexToBendChain(i);
                }
            }
        }

        private void LogInfo(string message) => _hipHijackLogger?.Info(message);

        private void LogWarn(string message) => _hipHijackLogger?.Warn(message);

        private void LogError(string message) => _hipHijackLogger?.Error(message);

        private void LogDebug(string message) => _hipHijackLogger?.Debug(message);

        private void LogBodyIkDiag(string message)
        {
            if (_settings == null || !_settings.BodyIkDiagnosticLog)
                return;

            _hipHijackLogger?.DiagnosticInfo(message);
        }

        private void LogBodyIkDiagWarn(string message)
        {
            if (_settings == null || !_settings.BodyIkDiagnosticLog)
                return;

            _hipHijackLogger?.DiagnosticWarning(message);
        }

        private void LogMaleHmdDiag(string message)
        {
            // MaleHmdDiag 専用ログは封印: 生成・書き込みしない
        }

        private void LogStateSnapshot(string reason, bool force = false)
        {
            if (!force && (_settings == null || !_settings.VerboseLog))
                return;

            string want = BuildOnIndexList(_bikWant);
            string running = BuildRunningIndexList();
            string enabled = _settings != null ? BuildOnIndexList(_settings.Enabled) : "-";
            string text = "state[" + reason + "] want=" + want
                + " running=" + running
                + " enabled=" + enabled
                + " abandoned=" + _abandonedByPostureChange
                + " pendingAbandon=" + _pendingAbandonByPostureChange
                + " autoEnabledInH=" + _hSceneSession.AutoEnabledInCurrentScene;
            LogInfo(text);
        }

        private string BuildRunningIndexList()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikEff[i] == null || !_bikEff[i].Running)
                    continue;
                if (sb.Length > 0)
                    sb.Append(",");
                sb.Append(i);
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }

        private static string BuildOnIndexList(bool[] arr)
        {
            if (arr == null)
                return "-";

            var sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                if (!arr[i])
                    continue;
                if (sb.Length > 0)
                    sb.Append(",");
                sb.Append(i);
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }
    }
}
