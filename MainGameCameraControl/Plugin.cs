using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using MainGameUiInputCapture;
using MainGameTransformGizmo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameCameraControl
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency("com.kks.maingame.uiinputcapture", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.kks.maingame.transformgizmo", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.kks.maingame.beatsyncseed", BepInDependency.DependencyFlags.SoftDependency)]
    internal sealed partial class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string GUID = "com.kks.maingame.cameracontrol";
        public const string PluginName = "MainGameCameraControl";
        public const string Version = "0.1.0";

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static string PluginDir { get; private set; }
        internal static PluginSettings Settings { get; private set; }

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object FileLogLock = new object();

        private ConfigEntry<bool> _cfgUiVisible;
        private ConfigEntry<bool> _cfgDetailLogEnabled;
        private ConfigEntry<bool> _cfgVoiceFaceEventCameraTriggerEnabled;
        private ConfigEntry<bool> _cfgVoiceFaceEventReturnEnabled;
        private ConfigEntry<float> _cfgVoiceFaceEventReturnStaySeconds;
        private ConfigEntry<bool> _cfgBoneReleaseByRightClick;
        private ConfigEntry<float> _cfgBoneReleaseLongPressSeconds;
        private ConfigEntry<float> _cfgDefaultFov;
        private ConfigEntry<bool> _cfgApplyFov;
        private ConfigEntry<bool> _cfgHeightShiftEnabled;
        private ConfigEntry<float> _cfgTransitionSeconds;
        private ConfigEntry<bool> _cfgAutoCameraCycleEnabled;
        private ConfigEntry<float> _cfgAutoCameraStaySeconds;
        private ConfigEntry<int> _cfgTransitionEasing;
        private ConfigEntry<bool> _cfgGizmoVisible;
        private ConfigEntry<float> _cfgGizmoSize;
        private ConfigEntry<bool> _cfgBeatFovZoomEnabled;
        private ConfigEntry<float> _cfgBeatFovZoomMultiplier;
        private const string InputOwnerKey = GUID + ".input";
        private const string InputSourceWindow = "window";
        private const float WindowWidth = 420f;
        private const float WindowHeight = 680f;
        private const float WindowDragHandleHeight = 24f;
        private static readonly string[] EasingLabels = { "Linear", "EaseIn", "EaseOut", "EaseInOut" };
        private static readonly string[] BoneTargetLabels = { "頭", "胸", "腰" };
        private static readonly string[] SaveModeLabels = { "通常", "ボーン" };
        private enum BeatZone { Unknown, Low, Mid, High }
        private const int SaveModeNormal = 0;
        private const int SaveModeBoneLink = 1;
        private const int BoneTargetHead = 0;
        private const int BoneTargetChest = 1;
        private const int BoneTargetWaist = 2;
        private static readonly string[][] BoneTargetNameCandidates =
        {
            new[] { "cf_j_head", "cf_J_Head" },
            new[] { "cf_j_spine03", "cf_J_Spine03", "cf_j_spine02", "cf_J_Spine02" },
            new[] { "cf_j_waist01", "cf_J_Waist01", "cf_j_waist02", "cf_J_Waist02" }
        };

        private sealed class CameraTransitionState
        {
            public BaseCameraControl_Ver2.CameraData From;
            public BaseCameraControl_Ver2.CameraData InitialTarget;
            public CameraPreset Preset;
            public bool UseDynamicTarget;
            public string FovTraceReason;
        }

        private readonly List<CameraPreset> _presets = new List<CameraPreset>();
        private HFlag _cachedHFlag;
        private CameraControl_Ver2 _ctrlCamera;
        private float _nextControllerResolveAt;
        private BaseCameraControl_Ver2.CameraData _currentData;
        private BaseCameraControl_Ver2.CameraData _targetData;
        private bool _hasCameraController;
        private string _statusMessage;
        private string _presetNameBuffer = string.Empty;
        private bool _uiFoldoutCurrent = true;
        private int _selectedSaveMode;
        private int _selectedBoneTarget;
        private bool _saveCameraPoseOverrides;
        private bool _posePresetAutoApply;
        private string _activeCameraPresetName = string.Empty;
        private Rect _windowRect;
        private bool _windowPointerActive;
        private bool _windowDragging;
        private bool _liveFovSliderActive;
        private bool _transitionActive;
        private float _transitionStartTime;
        private float _transitionDuration;
        private BaseCameraControl_Ver2.CameraData _transitionFrom;
        private BaseCameraControl_Ver2.CameraData _transitionTo;
        private CameraTransitionState _transitionState;
        private string _pendingFovTraceReason = string.Empty;
        private string _transitionFovTraceReason = string.Empty;
        private bool _boneLinkActive;
        private string _activeBoneLinkPresetName = string.Empty;
        private int _activeBoneLinkTarget = -1;
        private string _activeBoneLinkBoneName = string.Empty;
        private Vector3 _activeBoneLinkLookAtOffsetLocal = Vector3.zero;
        private bool _activeLookAtWorldY;

        private Vector3 ResolveActiveLookAt(Transform bone)
        {
            return _activeLookAtWorldY
                ? bone.position + Vector3.up * _activeBoneLinkLookAtOffsetLocal.y
                : bone.TransformPoint(_activeBoneLinkLookAtOffsetLocal);
        }
        private Vector3 _activeBoneLinkCameraOffsetLocal = Vector3.zero;
        // 位置=OFF のボーンプリセット遷移中、カメラ実位置をこの値で固定する（注視点だけ動かす）
        private Vector3 _transitionHoldCamWorld = Vector3.zero;
        private bool _transitionHoldCamWorldActive;
        private bool _activeBoneLinkSaveCameraPosition;
        private float _activeBoneLinkFov = 0f;

        // 相対方向モードの実行時状態。
        private bool _activeBoneLinkUseRelativeDir;
        private Vector3 _activeBoneLinkRelDirLocal = Vector3.zero;
        private float _activeBoneLinkRelDistance;
        private bool _activeBoneLinkRollWithBone;
        private bool _activeBoneLinkRelFromLookAt;
        private string _activeBoneLinkFrameBoneName = DefaultFrameBoneName;
        private int _relDirFollowLogFrames;
        private float _relDirManualUntil = -1f;
        private float _relDirNextTraceLog;
        private Vector3 _relDirLastExpected;
        private bool _relDirHasLastExpected;
        private bool _relDirWasInPoseBlend;
        private bool _relDirEaseByPoseBlend;
        private FieldInfo _poseBlendProgressField;
        private float _poseBlendFieldRetryAt;

        // 相対方向の記録用。前回出した時の状態を持って、変化した時だけ出す。
        private string _relDirBranch = string.Empty;
        private string _relDirSnapPoseKey = string.Empty;
        private Vector3 _relDirSnapDirLocal;
        private float _relDirSnapDistance;
        private Vector3 _relDirSnapExpected;
        private float _relDirSnapNextLog;
        private Vector3 _relDirEaseFrom;
        private float _relDirEaseUntil = -1f;
        private float _relDirEaseSeconds = 1f;
        private float _relDirEaseNextLog;

        /// <summary>相対方向の基準ボーン既定値。体の向きを取りたいので腰。</summary>
        internal const string DefaultFrameBoneName = "cf_j_hips";

        /// <summary>床すれすれを許す余裕。ルート(足元)からこの高さより下へは置かない。</summary>
        private const float FloorMargin = 0.05f;

        /// <summary>手動操作とみなす猶予。ホイールを回し終えた直後の数フレームを拾う。</summary>
        private const float ManualGraceSeconds = 0.25f;

        /// <summary>相対方向の毎フレームログの間引き間隔。</summary>
        private const float RelDirTraceIntervalSeconds = 0.5f;

        /// <summary>
        /// 行き先が1フレームでこれ以上動いたら「体位が変わった」とみなして遷移する。
        /// これ未満の普段の動きは即座に張り付く（遅らせると被写体が画面内でズレる）。
        /// </summary>
        private const float PoseShiftMeters = 0.25f;

        /// <summary>
        /// ポーズ遷移の進行度が取れなかった時だけ使う保険の秒数。
        /// 通常は MainGameHScenePoseInjector の進行度に乗るので、この値は使われない。
        /// </summary>
        private const float PoseShiftSeconds = 0.5f;

        /// <summary>進行度の公開元を探し直す間隔。毎フレーム全アセンブリを走査しない。</summary>
        private const float PoseBlendFieldRetrySeconds = 5f;

        /// <summary>相対方向の記録の間引き間隔。体勢が変わった時はこれを無視して必ず出す。</summary>
        private const float RelDirSnapshotIntervalSeconds = 0.3f;

        // 保存タブのチェック状態。相対方向は既定ON、ロール追従は既定OFF。
        private bool _saveRelativeDirection = true;
        private bool _saveRollWithBone;

        // 相対方向の動作切替。3つとも独立して切れる（切り分け用）。
        private bool _relRebakeOnManual;
        private bool _relFloorGuard;
        private bool _relYawUpAxisFallback = true;
        private GameObject _lookAtAnchorGo;
        private Transform _lookAtAnchor;
        private TransformGizmo _lookAtAnchorGizmo;
        private bool _gizmoVisible;
        private string _pluginLogPath = string.Empty;
        private float _lastRequestedPresetFov = 35f;
        private float _lastBaseFov = -1f;
        private float _lastAppliedFov = -1f;
        private int _quickLookAtBoneCycleIndex = -1;
        private int _quickPresetCycleIndex = -1;
        private float _nextAutoCameraSwitchTime = -1f;
        private bool _beatFovSuspendedForNewController;
        private bool _voiceFaceReturnPending;
        private float _voiceFaceReturnTime = -1f;
        private BaseCameraControl_Ver2.CameraData _voiceFaceReturnData;
        private bool _voiceFaceReturnUsePreset;
        private string _voiceFaceReturnPresetName = string.Empty;

        // 外部FOV制御中の hold 期限（Time.unscaledTime ベース）。
        // TryApplyExternalFov / TryApplyExternalFovFast が成功するたびに 1.0 秒先まで延長される。
        // この期間中、EnforceLastAppliedFov と UpdateBeatFovRuntime は早期 return し、
        // MGCC の自動 FOV 書き戻し（5..120 clamp 経路）が外部演出値を潰さないようにする。
        // 外部 API 呼出が止まれば 1 秒で自然に通常動作復帰する。
        private float _externalFovHoldUntil;

        private bool IsExternalFovHoldActive()
        {
            return Time.unscaledTime <= _externalFovHoldUntil;
        }

        // ---- FOV 診断ログ用スロットル ----
        private const float DiagFovLogInterval = 0.15f;

        // Enforce / BeatRuntime はスパム削減のため、状態遷移時 + active 中の周期ログ（1秒に1回）だけ出す
        private const float DiagFovHeartbeatInterval = 1.0f;

        // allCameras 一覧ダンプの 1 サイクルあたり 1 回制御
        private bool _diagFovHoldWasActive;
        private bool _diagFovDumpedCamsThisCycle;
        private bool _diagFovDumpedOver120ThisCycle;

        private bool DiagFovThrottle(ref float lastLogTime)
        {
            float now = Time.unscaledTime;
            if (now - lastLogTime < DiagFovLogInterval) return false;
            lastLogTime = now;
            return true;
        }

        private string FormatCameraIdShort(Camera c)
        {
            if (c == null) return "null";
            try
            {
                string tag = "?";
                try { tag = c.gameObject != null ? c.gameObject.tag : "?"; } catch { }
                return c.gameObject.name + "(id=" + c.GetInstanceID()
                    + ", tag=" + tag
                    + ", depth=" + c.depth.ToString("0.#", CultureInfo.InvariantCulture)
                    + ", fov=" + c.fieldOfView.ToString("0.##", CultureInfo.InvariantCulture)
                    + ", enabled=" + c.enabled
                    + ", target=" + (c.targetTexture == null ? "Screen" : "RT")
                    + ")";
            }
            catch { return "?"; }
        }

        private void DumpAllScreenCameras(string tag)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (Camera c in Camera.allCameras)
                {
                    if (c == null) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(FormatCameraIdShort(c));
                }
                if (_cfgDetailLogEnabled != null && _cfgDetailLogEnabled.Value)
                    LogInfo("[DIAG_CAMS] tag=" + tag + " mainCam=" + FormatCameraIdShort(Camera.main) + " active=[" + sb + "]");
            }
            catch (Exception ex)
            {
                LogWarn("DumpAllScreenCameras failed: " + ex.Message);
            }
        }

        // DIAG_FAST 呼び出し時の 1 サイクル制御（cycle start で allCameras 一覧、input>120 初回で再ダンプ）
        private void DiagFovUpdateCycleAndDump(float input)
        {
            bool nowActive = IsExternalFovHoldActive();
            if (nowActive && !_diagFovHoldWasActive)
            {
                _diagFovDumpedCamsThisCycle = false;
                _diagFovDumpedOver120ThisCycle = false;
            }
            else if (!nowActive && _diagFovHoldWasActive)
            {
                _diagFovDumpedCamsThisCycle = false;
                _diagFovDumpedOver120ThisCycle = false;
            }
            _diagFovHoldWasActive = nowActive;

            if (nowActive && !_diagFovDumpedCamsThisCycle)
            {
                DumpAllScreenCameras("cycle-start");
                _diagFovDumpedCamsThisCycle = true;
            }
            if (nowActive && input > 120.001f && !_diagFovDumpedOver120ThisCycle)
            {
                DumpAllScreenCameras("over-120-first");
                _diagFovDumpedOver120ThisCycle = true;
            }
        }
        private float _beatFovBaseFov = -1f;
        private bool _cameraPreCullHooked;
        private float _applyTraceLastLogTime = -1f;
        private int _lastTransitionFovLogFrame = -1;
        private Type _beatPluginType;
        private FieldInfo _beatZoneRawTargetField;
        private FieldInfo _beatCfgBpmField;
        private FieldInfo _beatCfgLowIntensityField;
        private FieldInfo _beatCfgMidIntensityField;
        private FieldInfo _beatCfgHighIntensityField;
        private PropertyInfo _beatInstanceProperty;
        private bool _beatLookupDone;
        private bool _beatStopReturnActive;
        private bool _lastBeatLoopActive;
        private float _beatStopReturnStartTime;
        private float _beatStopReturnStartMultiplier = 1f;
        private float _lastBeatCurrentMultiplier = 1f;
        // 拍ズームの位相。脈ゾーン突入時に 0（＝base）から始め、毎フレーム hz×dt で積算する。
        // 絶対時刻ベースだとゾーン突入時に位相が途中（脈の山＝拡大側）から始まり、
        // base を無視していきなり拡大値へ飛んでしまうため。
        // _beatPhaseFrame で「1フレーム1回だけ積算」を保証（ResolveBeatZoomedFov は毎フレ複数回呼ばれる）。
        private float _beatPhaseAccum;
        private int _beatPhaseFrame = -1;

        private bool IsBeatFovEnabled()
        {
            return _cfgBeatFovZoomEnabled != null && _cfgBeatFovZoomEnabled.Value && !IsVrFovBlocked();
        }

        // VR判定とカメラ走査は毎フレーム複数回（precullはカメラ台数分）呼ばれるため、
        // 結果を1フレーム1回にキャッシュし、Camera.allCameras の都度allocを再利用バッファに置換する。
        private int _vrBlockedFrame = -1;
        private bool _vrBlockedCached;
        private Camera[] _cameraScanBuffer = new Camera[16];
        private int _cameraScanFrame = -1;
        private int _cameraScanCount;

        // 再利用バッファに現在の全カメラを詰めて件数を返す（Camera.allCameras の毎回allocを回避）。
        private int FillScreenCameras()
        {
            if (_cameraScanFrame == Time.frameCount)
                return _cameraScanCount;
            _cameraScanFrame = Time.frameCount;
            int count = Camera.allCamerasCount;
            if (count <= 0)
                return _cameraScanCount = 0;
            if (_cameraScanBuffer.Length < count)
                _cameraScanBuffer = new Camera[count];
            return _cameraScanCount = Camera.GetAllCameras(_cameraScanBuffer);
        }

        private bool IsVrFovBlocked()
        {
            if (_vrBlockedFrame == Time.frameCount)
                return _vrBlockedCached;

            _vrBlockedFrame = Time.frameCount;
            _vrBlockedCached = ComputeVrFovBlocked();
            return _vrBlockedCached;
        }

        private bool ComputeVrFovBlocked()
        {
            if (_ctrlCamera != null && IsVrEyeCamera(_ctrlCamera.thisCmaera))
                return true;

            if (IsVrEyeCamera(Camera.main))
                return true;

            int count = FillScreenCameras();
            for (int i = 0; i < count; i++)
            {
                if (IsVrEyeCamera(_cameraScanBuffer[i]))
                    return true;
            }

            return false;
        }

        private static bool CanWriteFov(Camera camera)
        {
            return camera != null && !IsVrEyeCamera(camera);
        }

        private static bool IsVrEyeCamera(Camera camera)
        {
            if (camera == null)
                return false;

            if (camera.stereoEnabled)
                return true;

            string name = camera.name ?? string.Empty;
            return name.IndexOf("VRGIN_Camera", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("(eye)", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            PluginDir = Path.GetDirectoryName(Info.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            _pluginLogPath = Path.Combine(PluginDir, "MainGameCameraControl.log");
            ResetPluginLog();

            Settings = SettingsStore.LoadOrCreate(Path.Combine(PluginDir, "CameraControlSettings.json"));
            _cfgUiVisible = Config.Bind("UI", "UiVisible", Settings.UiVisible, "Camera UI visibility.");
            _cfgDetailLogEnabled = Config.Bind("Logging", "DetailLogEnabled", Settings.DetailLogEnabled, "Enable detailed camera logs.");
            _cfgVoiceFaceEventCameraTriggerEnabled = Config.Bind("Bridge", "VoiceFaceEventCameraTriggerEnabled", Settings.VoiceFaceEventCameraTriggerEnabled, "Enable VoiceFaceEvent preset triggers.");
            _cfgVoiceFaceEventReturnEnabled = Config.Bind("Bridge", "VoiceFaceEventReturnEnabled", Settings.VoiceFaceEventReturnEnabled, "Return to previous camera after a VoiceFaceEvent camera trigger.");
            _cfgVoiceFaceEventReturnStaySeconds = Config.Bind("Bridge", "VoiceFaceEventReturnStaySeconds", Settings.VoiceFaceEventReturnStaySeconds, "Seconds to stay on a VoiceFaceEvent camera trigger before returning.");
            _cfgBoneReleaseByRightClick = Config.Bind("Input", "BoneReleaseByRightClick", Settings.BoneReleaseByRightClick, "Enable Ctrl+right-click quick bone target cycling.");
            _cfgBoneReleaseLongPressSeconds = Config.Bind("Input", "BoneReleaseLongPressSeconds", Settings.BoneReleaseLongPressSeconds, "Right-button hold time (seconds) to also move the look-at point to the camera position. A short tap only releases bone-link.");
            _cfgDefaultFov = Config.Bind("Camera", "DefaultFov", Settings.DefaultFov, "Default FOV.");
            _cfgApplyFov = Config.Bind("Camera", "ApplyFov", Settings.ApplyFov, "Apply saved FOV.");
            _cfgTransitionSeconds = Config.Bind("Camera", "TransitionSeconds", Settings.TransitionSeconds, "Transition seconds.");
            // 既定OFF。UpdateShapeBody を挟んで測る方式は、身長変化ではなく
            // 「シェイプ補正がアニメ姿勢へ加える補正量」を拾ってしまい、毎フレーム誤差が乗る。
            // 測り方を作り直すまで有効にしない。
            _cfgHeightShiftEnabled = Config.Bind("Camera", "HeightShiftEnabled", false, "EXPERIMENTAL / BROKEN: keep the framing when the character's height changes. The current measurement drifts the camera every frame. Leave off.");
            PatchHeightShift();
            _cfgAutoCameraCycleEnabled = Config.Bind("Camera", "AutoCameraCycleEnabled", Settings.AutoCameraCycleEnabled, "Automatically cycle checked camera presets.");
            _cfgAutoCameraStaySeconds = Config.Bind("Camera", "AutoCameraStaySeconds", Settings.AutoCameraStaySeconds, "Seconds to stay on each checked camera preset.");
            _cfgTransitionEasing = Config.Bind("Camera", "TransitionEasing", Settings.TransitionEasing, "Transition easing.");
            _cfgGizmoVisible = Config.Bind("Gizmo", "Visible", Settings.GizmoVisible, "Show look-at gizmo.");
            _cfgGizmoSize = Config.Bind("Gizmo", "Size", Settings.GizmoSize, "Look-at gizmo size.");
            _cfgBeatFovZoomEnabled = Config.Bind("BeatFov", "Enabled", Settings.BeatFovZoomEnabled, "Enable BPM beat-linked FOV zoom.");
            _cfgBeatFovZoomEnabled.SettingChanged += (_, __) => {
                if (_cfgBeatFovZoomEnabled.Value) CaptureBeatFovBase("setting-enable");
                else RestoreBeatFovBase("setting-disable");
            };
            _cfgBeatFovZoomMultiplier = Config.Bind("BeatFov", "ZoomMultiplier", Settings.BeatFovZoomMultiplier, "Beat-linked FOV zoom multiplier.");
            _windowRect = new Rect(Settings.WindowX, Settings.WindowY, WindowWidth, WindowHeight);
            _selectedSaveMode = Mathf.Clamp(
                Settings.SelectedSaveMode != 0 || !Settings.SaveWithBoneLink
                    ? Settings.SelectedSaveMode
                    : SaveModeBoneLink,
                0,
                SaveModeLabels.Length - 1);
            _selectedBoneTarget = Mathf.Clamp(Settings.SelectedBoneTarget, 0, BoneTargetLabels.Length - 1);
            _saveCameraPoseOverrides = Settings.SaveCameraPoseOverrides;
            _saveRelativeDirection = Settings.SaveRelativeDirection;
            _saveRollWithBone = Settings.SaveRollWithBone;
            _relRebakeOnManual = Settings.RelRebakeOnManual;
            _relFloorGuard = Settings.RelFloorGuard;
            _relYawUpAxisFallback = Settings.RelYawUpAxisFallback;
            _posePresetAutoApply = Settings.PosePresetAutoApply;
            _gizmoVisible = Settings.GizmoVisible;

            SyncFromConfig();
            LoadPresetsFromSettings();
            LogInfo(PluginName + " " + Version + " initialized");
            LogInfo("pluginDir=" + PluginDir);
            LogInfo("settingsPath=" + Path.Combine(PluginDir, "CameraControlSettings.json"));
            LogInfo("logPath=" + _pluginLogPath);
            HookCameraPreCull();
            InitializeFovInput();
        }

        private void OnDestroy()
        {
            Camera.onPreRender -= ObserveFovRender;
            _fovInputHarmony?.UnpatchSelf();
            if (_cameraPreCullHooked)
            {
                Camera.onPreCull -= OnCameraPreCull;
                _cameraPreCullHooked = false;
            }
            ReleaseInputCapture();
            DestroyLookAtAnchor();
            SaveSettings();
            if (Instance == this)
                Instance = null;
        }

        private bool _gateWasInRoom;

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            // 退室時は掴んだ入力占有を必ず返す（返さないとカメラが操作不能のまま残る）。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, ReleaseInputCapture)) return;
            RegisterUnifiedTabIfAvailable();
            UpdateWindowDragState();
            HandleBoneReleaseGesture();
            RefreshCameraControllerCache();
            HandleBoneLookAtHeightDrag();

            if (!_cfgUiVisible.Value)
            {
                _windowPointerActive = false;
                _windowDragging = false;
            }

            UpdatePosePresetTracking();
            UpdateInputCapture();
        }

        private void HandleBoneReleaseGesture()
        {
            // Ctrl + 右クリック単体:
            // 頭 → 胸 → 腰 の注視点ロックだけを切り替える。
            // 左クリック中は別ジェスチャに回す。
            if (_cfgBoneReleaseByRightClick != null && _cfgBoneReleaseByRightClick.Value
                && !_windowPointerActive && !_windowDragging
                && CanonPlugins.Shared.PluginInput.Down("MainGameCameraControl.boneCycle", "Mouse1 + Ctrl", true))
            {
                CycleQuickLookAtBoneLock();
                return;
            }

            // Ctrl + middle-button drag releases bone tracking. A click alone
            // does not release it; keep the native camera from panning while
            // the gesture is armed.
            if (!CanonPlugins.Shared.PluginInput.MainHeld("MainGameCameraControl.release", "Mouse2 + Ctrl"))
            {
                _boneReleaseMiddleHeld = false;
                return;
            }
            if (CanonPlugins.Shared.PluginInput.Down("MainGameCameraControl.release", "Mouse2 + Ctrl") && _boneLinkActive
                && UnityEngine.Application.isFocused
                && !_windowPointerActive && !_windowDragging)
                _boneReleaseMiddleHeld = true;
            if (!_boneReleaseMiddleHeld || !_boneLinkActive) return;
            if (Mathf.Abs(Input.GetAxisRaw("Mouse X")) < 0.01f
                && Mathf.Abs(Input.GetAxisRaw("Mouse Y")) < 0.01f) return;
            ClearBoneLinkActiveState();
            _activeCameraPresetName = string.Empty;
            _quickLookAtBoneCycleIndex = -1;
        }

        private bool IsBoneLookAtHeightDragActive()
        {
            return _boneLinkActive && !_windowPointerActive && !_windowDragging
                && CanonPlugins.Shared.PluginInput.Held("MainGameCameraControl.height", "Mouse1 + Shift", true);
        }

        private void HandleBoneLookAtHeightDrag()
        {
            if (!IsBoneLookAtHeightDragActive() || _lookAtAnchor == null)
                return;

            float deltaY = Input.GetAxisRaw("Mouse Y");
            if (Mathf.Abs(deltaY) < 0.0001f)
                return;

            ChaControl female = ResolveMainFemale();
            Transform bone = female != null ? ResolveActiveBoneTarget(female) : null;
            if (bone == null)
                return;

            float height = ResolveActiveLookAt(bone).y - bone.position.y;
            _activeLookAtWorldY = true;
            _activeBoneLinkLookAtOffsetLocal = new Vector3(0f, height + deltaY * 0.02f, 0f);
            Vector3 lookAt = ResolveActiveLookAt(bone);
            _lookAtAnchor.position = lookAt;
        }

        private void LateUpdate()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            RefreshCameraControllerCache();
            UpdateCameraTransition();
            UpdateBoneLinkedCamera();
            UpdateAutoCameraCycle();
            UpdateVoiceFaceEventReturn();
            UpdateBeatFovRuntime();
            EnforceLastAppliedFov("lateupdate-enforce");
            UpdateFovDiagnostics();
        }

        private void OnGUI()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            // 総合UIがタブ表示する間は自前窓を出さない。
            if (IsHostedByUnifiedUi())
                return;

            if (!_cfgUiVisible.Value)
                return;

            _windowRect.width = WindowWidth;
            _windowRect.height = WindowHeight;
            _windowRect = GUI.Window(
                958121,
                _windowRect,
                DrawUiWindowV2,
                "MainGameCameraControl");
            HandleWindowPointer(Event.current);
        }

        private bool UpdateLiveFovSliderActive(Rect rect)
        {
            Event evt = Event.current;
            if (evt == null)
                return _liveFovSliderActive;

            if (evt.type == EventType.MouseDown)
            {
                _liveFovSliderActive = rect.Contains(evt.mousePosition);
                return _liveFovSliderActive;
            }

            if (evt.type == EventType.MouseUp)
            {
                bool wasActive = _liveFovSliderActive;
                _liveFovSliderActive = false;
                return wasActive;
            }

            return _liveFovSliderActive;
        }

        private void SyncFromConfig()
        {
            Settings.UiVisible = _cfgUiVisible.Value;
            Settings.DetailLogEnabled = _cfgDetailLogEnabled.Value;
            Settings.VoiceFaceEventCameraTriggerEnabled = _cfgVoiceFaceEventCameraTriggerEnabled.Value;
            Settings.VoiceFaceEventReturnEnabled = _cfgVoiceFaceEventReturnEnabled.Value;
            Settings.VoiceFaceEventReturnStaySeconds = Mathf.Clamp(_cfgVoiceFaceEventReturnStaySeconds.Value, 0.5f, 60f);
            Settings.BoneReleaseByRightClick = _cfgBoneReleaseByRightClick.Value;
            Settings.BoneReleaseLongPressSeconds = Mathf.Max(0f, _cfgBoneReleaseLongPressSeconds.Value);
            Settings.DefaultFov = Mathf.Clamp(_cfgDefaultFov.Value, 5f, 120f);
            Settings.ApplyFov = _cfgApplyFov.Value;
            Settings.TransitionSeconds = Mathf.Clamp(_cfgTransitionSeconds.Value, 0f, 3f);
            Settings.AutoCameraCycleEnabled = _cfgAutoCameraCycleEnabled.Value;
            Settings.AutoCameraStaySeconds = Mathf.Clamp(_cfgAutoCameraStaySeconds.Value, 1f, 120f);
            Settings.TransitionEasing = Mathf.Clamp(_cfgTransitionEasing.Value, 0, EasingLabels.Length - 1);
            Settings.WindowX = _windowRect.x;
            Settings.WindowY = _windowRect.y;
            Settings.SaveWithBoneLink = _selectedSaveMode != SaveModeNormal;
            Settings.SelectedBoneTarget = Mathf.Clamp(_selectedBoneTarget, 0, BoneTargetLabels.Length - 1);
            Settings.SelectedSaveMode = Mathf.Clamp(_selectedSaveMode, 0, SaveModeLabels.Length - 1);
            Settings.SaveCameraPoseOverrides = _saveCameraPoseOverrides;
            Settings.PosePresetAutoApply = _posePresetAutoApply;
            Settings.GizmoVisible = _gizmoVisible;
            Settings.GizmoSize = Mathf.Clamp(_cfgGizmoSize.Value, 0.2f, 1.5f);
            Settings.BeatFovZoomEnabled = _cfgBeatFovZoomEnabled.Value;
            Settings.BeatFovZoomMultiplier = Mathf.Clamp(_cfgBeatFovZoomMultiplier.Value, 1f, 3f);
        }

        private void RefreshCameraControllerCache()
        {
            CameraControl_Ver2 ctrl = ResolveCtrlCamera();
            if (ctrl == null)
            {
                _hasCameraController = false;
                _ctrlCamera = null;
                return;
            }

            if (_ctrlCamera != ctrl)
            {
                _ctrlCamera = ctrl;
                _hasCameraController = true;
                _currentData = ctrl.GetCameraData();
                _targetData = _currentData;
                ResetFovStateForNewCameraController();
                SetStatus("CameraControl_Ver2 を取得");
                return;
            }

            _hasCameraController = true;
        }

        private void ResetFovStateForNewCameraController()
        {
            if (_ctrlCamera == null)
                return;

            float baseFov = Mathf.Clamp(_ctrlCamera.CameraFov, 5f, 120f);
            _fovResetBase = Mathf.Clamp(_ctrlCamera.CameraInitFov, 5f, 120f);
            _externalFovHoldUntil = -1f;
            _externalDisplayFov = 0f;
            _transitionActive = false;
            _transitionState = null;
            _transitionManualFov = false;
            _currentData.Fov = baseFov;
            _targetData.Fov = baseFov;
            _lastBaseFov = baseFov;
            _lastAppliedFov = baseFov;
            _beatFovBaseFov = baseFov;
            if (_boneLinkActive)
                _activeBoneLinkFov = baseFov;
            _beatFovSuspendedForNewController = true;
            ResetAutoCameraCycleTimer();
            ResetBeatFovRuntimeState();

            LogInfo("[fov-reset-new-controller]"
                + " base=" + baseFov.ToString("0.##", CultureInfo.InvariantCulture)
                + " live=" + (_ctrlCamera.thisCmaera != null ? _ctrlCamera.thisCmaera.fieldOfView.ToString("0.##", CultureInfo.InvariantCulture) : "none")
                + " data=" + _currentData.Fov.ToString("0.##", CultureInfo.InvariantCulture)
                + " beatEnabled=" + IsBeatFovEnabled()
                + " autoCycle=" + (_cfgAutoCameraCycleEnabled != null && _cfgAutoCameraCycleEnabled.Value)
                + " nextAuto=" + _nextAutoCameraSwitchTime.ToString("0.###", CultureInfo.InvariantCulture)
                + " now=" + Time.unscaledTime.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private CameraControl_Ver2 ResolveCtrlCamera()
        {
            if (_cachedHFlag != null && _cachedHFlag.ctrlCamera != null)
                return _cachedHFlag.ctrlCamera;

            // Update と LateUpdate から毎フレーム呼ばれる。HFlag がまだない間の
            // シーン全体検索を間引き、既存コントローラーはそのまま使う。
            if (Time.unscaledTime < _nextControllerResolveAt)
                return _ctrlCamera != null ? _ctrlCamera : null;
            _nextControllerResolveAt = Time.unscaledTime + 0.5f;

            if (_cachedHFlag == null)
                _cachedHFlag = UnityEngine.Object.FindObjectOfType<HFlag>();
            if (_cachedHFlag != null && _cachedHFlag.ctrlCamera != null)
                return _cachedHFlag.ctrlCamera;
            return UnityEngine.Object.FindObjectOfType<CameraControl_Ver2>();
        }

        private void ApplyCameraData(BaseCameraControl_Ver2.CameraData data, string fovTraceReason = null)
        {
            if (_ctrlCamera == null) return;
            SetBaseFov(_transitionActive && _transitionManualFov ? _transitionManualBaseFov : data.Fov);
            data.Fov = _lastBaseFov;
            _ctrlCamera.SetCameraData(data);
            RenderCurrentFov(fovTraceReason ?? "apply-camera-data");
        }

        private void EnforceLastAppliedFov(string reason)
        {
            if (_ctrlCamera == null || _transitionActive || _beatFovSuspendedForNewController
                || IsBeatFovEnabled() || !_cfgApplyFov.Value || IsVrFovBlocked()) return;
            if (!IsExternalFovHoldActive())
            {
                float baseFov = ResolveBeatBaseFov();
                if (_ctrlCamera.CameraFov != baseFov) _ctrlCamera.CameraFov = baseFov;
            }
            RenderCurrentFov(reason);
        }

        private bool IsNativeFovKeyActive()
        {
            return CanonPlugins.Shared.PluginInput.Held("MainGameCameraControl.fovReset", "Semicolon | Colon")
                || CanonPlugins.Shared.PluginInput.Held("MainGameCameraControl.fovWide", "RightBracket")
                || CanonPlugins.Shared.PluginInput.Held("MainGameCameraControl.fovNarrow", "Equals");
        }

        private void ApplyFovToScreenCameras(float requestedFov, string reason, bool writeLog)
        {
            if (IsVrFovBlocked())
                return;

            int count = FillScreenCameras();
            for (int i = 0; i < count; i++)
            {
                Camera camera = _cameraScanBuffer[i];
                if (camera == null || !camera.enabled || camera.targetTexture != null || !CanWriteFov(camera))
                    continue;

                float before = camera.fieldOfView;
                if (before != requestedFov) camera.fieldOfView = requestedFov;
                if (writeLog)
                {
                    LogInfo(
                        "[fov-force-camera]"
                        + " reason=" + reason
                        + " requested=" + requestedFov.ToString("0.##", CultureInfo.InvariantCulture)
                        + " before=" + before.ToString("0.##", CultureInfo.InvariantCulture)
                        + " after=" + camera.fieldOfView.ToString("0.##", CultureInfo.InvariantCulture)
                        + " camera=" + FormatCameraFovDetailed(camera));
                }
            }
        }

        private void SaveCurrentAsPreset(string explicitName = null)
        {
            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
            {
                SetStatus("CameraControl_Ver2 が見つからない");
                return;
            }

            BaseCameraControl_Ver2.CameraData data = _ctrlCamera.GetCameraData();
            data.Fov = ResolveCurrentBaseFov(data.Fov);
            string name = string.IsNullOrWhiteSpace(explicitName)
                ? (string.IsNullOrWhiteSpace(_presetNameBuffer)
                    ? $"Preset {(_presets.Count + 1)}"
                    : _presetNameBuffer.Trim())
                : explicitName.Trim();

            CameraPreset preset;
            switch (_selectedSaveMode)
            {
                case SaveModeBoneLink:
                    preset = BuildBoneLinkedPreset(name, data);
                    break;
                default:
                    preset = new CameraPreset
                    {
                        Name = name,
                        TargetPosition = ToArray(data.Pos),
                        CameraDirection = ToArray(data.Dir),
                        Rotation = ToArray(data.Rot),
                        Fov = data.Fov
                    };
                    break;
            }

            if (preset == null)
            {
                SetStatus("ボーン連携保存に失敗");
                return;
            }

            _presets.Add(preset);
            _activeCameraPresetName = name;
            _presetNameBuffer = name;
            if (_saveCameraPoseOverrides && TryGetCurrentPoseInfo(out string poseKey, out string poseDisplayName))
            {
                preset.UsePoseOverrides = true;
                CapturePoseOverrideToPreset(preset, poseKey, poseDisplayName);
            }
            CommitPresets("save-current");
            _currentData = data;
            _targetData = data;
            LogCameraData("save-preset", data, preset);
            SetStatus(preset.UsePoseOverrides ? "保存(体位対応): " + name : "保存: " + name);
        }

        private void LoadPreset(CameraPreset preset)
        {
            if (preset == null)
                return;

            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
            {
                SetStatus("CameraControl_Ver2 が見つからない");
                return;
            }

            _activeCameraPresetName = preset.Name ?? string.Empty;
            _presetNameBuffer = preset.Name ?? string.Empty;
            preset = ResolveCameraPresetForCurrentPose(preset);

            // 呼び出し時、保存タブのモード/対象ボーン/「位置を使う」を呼び出したプリセットに合わせる。
            // これで直後に ↻(上書き) や 体位更新 を押しても、見ている構図と同じモード/ボーンで作り直される。
            _selectedSaveMode = preset.UseBoneLink ? SaveModeBoneLink : SaveModeNormal;
            if (preset.UseBoneLink)
            {
                _selectedBoneTarget = Mathf.Clamp(preset.BoneTarget, 0, BoneTargetLabels.Length - 1);
                _saveRelativeDirection = preset.UseRelativeDirection;
                _saveRollWithBone = preset.RollWithBone;
            }

            _currentData = _ctrlCamera.GetCameraData();
            _currentData.Fov = ResolveCurrentBaseFov(_currentData.Fov);
            _targetData = BuildCameraDataFromPreset(preset, _currentData);
            _lastRequestedPresetFov = Mathf.Clamp(_targetData.Fov, 5f, 120f);
            if (!_cfgApplyFov.Value)
                _targetData.Fov = _currentData.Fov;

            bool useDynamicTarget = preset.UseBoneLink;
            if (useDynamicTarget)
            {
                _boneLinkActive = true;
                _activeBoneLinkPresetName = preset.Name ?? string.Empty;
                _activeBoneLinkTarget = preset.BoneTarget;
                _activeBoneLinkBoneName = preset.BoneName ?? string.Empty;
                _activeBoneLinkLookAtOffsetLocal = GetPresetBoneLinkOffsetLocal(preset);
                _activeLookAtWorldY = preset.LookAtWorldY;
                _activeBoneLinkSaveCameraPosition = HasPresetCameraPosition(preset);
                ApplyRelativeDirectionState(preset);
                _activeBoneLinkCameraOffsetLocal = _activeBoneLinkSaveCameraPosition
                    ? GetPresetCameraOffsetLocal(preset)
                    : Vector3.zero;
                _activeBoneLinkFov = _targetData.Fov;
                EnsureLookAtAnchor();
                SyncLookAtAnchorToActiveBoneLink(force: true);
            }
            else
            {
                ClearBoneLinkActiveState();
            }

            LogCameraData("load-current", _currentData, preset);
            LogCameraData("load-target", _targetData, preset);
            LogRecallPositionDiag(preset, _targetData);

            if (_cfgDetailLogEnabled != null && _cfgDetailLogEnabled.Value)
            {
                _pendingFovTraceReason =
                    "load"
                    + " preset=" + (preset.Name ?? "(null)")
                    + " presetFov=" + preset.Fov.ToString("0.##", CultureInfo.InvariantCulture)
                    + " targetFov=" + _targetData.Fov.ToString("0.##", CultureInfo.InvariantCulture)
                    + " currentFov=" + _currentData.Fov.ToString("0.##", CultureInfo.InvariantCulture)
                    + " applyFov=" + _cfgApplyFov.Value
                    + " boneLink=" + preset.UseBoneLink;
            }
            StartCameraTransition(_currentData, _targetData, preset, useDynamicTarget);
            _beatFovSuspendedForNewController = false;
            ResetAutoCameraCycleTimer();
            SetStatus("呼び出し: " + preset.Name);
        }

        private bool TryLoadPresetByNameInternal(string presetName, out string reason)
        {
            reason = string.Empty;
            string normalized = (presetName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                reason = "preset_name_empty";
                return false;
            }

            CameraPreset exact = _presets.FirstOrDefault(x =>
                x != null
                && !string.IsNullOrWhiteSpace(x.Name)
                && string.Equals(x.Name.Trim(), normalized, StringComparison.Ordinal));
            if (exact != null)
            {
                LoadPreset(exact);
                reason = "ok";
                return true;
            }

            reason = "preset_not_found";
            return false;
        }

        private bool TryLoadPresetByIndexInternal(int index, out string reason)
        {
            reason = string.Empty;
            if (index < 0 || index >= _presets.Count)
            {
                reason = "preset_index_out_of_range";
                return false;
            }

            CameraPreset preset = _presets[index];
            if (preset == null)
            {
                reason = "preset_null";
                return false;
            }

            LoadPreset(preset);
            reason = "ok";
            return true;
        }

        private bool TryGetPresetNamesInternal(out string[] names, out string reason)
        {
            reason = string.Empty;
            names = _presets
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => x.Name.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            reason = "ok";
            return true;
        }

        private void RemovePresetAt(int index)
        {
            if (index < 0 || index >= _presets.Count)
                return;

            string removed = _presets[index]?.Name;
            _presets.RemoveAt(index);
            CommitPresets("remove:" + removed);
            SetStatus("削除: " + removed);
        }

        private void LoadPresetsFromSettings()
        {
            _presets.Clear();
            if (Settings.Presets != null)
                _presets.AddRange(Settings.Presets.Where(x => x != null));

            // 旧ビルドはチェックOFFでも方向と距離を焼き、フラグだけ false で書き出していた。
            // その結果、遊んでいる間は効いているのに保存して起動し直すと相対方向が全部死ぬ。
            // 方向と距離が入っているものは「使うつもりで保存した」とみなして戻す。
            for (int i = 0; i < _presets.Count; i++)
            {
                CameraPreset p = _presets[i];
                if (p.UseRelativeDirection
                    || p.RelativeDirLocal == null || p.RelativeDirLocal.Length < 3
                    || p.RelativeDistance <= 0.0001f)
                    continue;

                p.UseRelativeDirection = true;
                LogInfo("[reldir] migrate preset=" + (p.Name ?? "")
                    + " dist=" + p.RelativeDistance.ToString("0.###")
                    + " frameBone=" + (p.FrameBoneName ?? "null"));
            }

            if (_presets.Count == 0)
                _presetNameBuffer = "Main";
        }

        private void CommitPresets(string reason)
        {
            Settings.Presets = _presets.ToList();
            SaveSettings();
            LogInfo("camera preset commit reason=" + reason + " count=" + _presets.Count);
        }

        private void SaveSettings()
        {
            try
            {
                Settings.UiVisible = _cfgUiVisible.Value;
                Settings.DetailLogEnabled = _cfgDetailLogEnabled.Value;
                Settings.VoiceFaceEventCameraTriggerEnabled = _cfgVoiceFaceEventCameraTriggerEnabled.Value;
                Settings.VoiceFaceEventReturnEnabled = _cfgVoiceFaceEventReturnEnabled.Value;
                Settings.VoiceFaceEventReturnStaySeconds = Mathf.Clamp(_cfgVoiceFaceEventReturnStaySeconds.Value, 0.5f, 60f);
                Settings.BoneReleaseByRightClick = _cfgBoneReleaseByRightClick.Value;
                Settings.BoneReleaseLongPressSeconds = _cfgBoneReleaseLongPressSeconds.Value;
                Settings.DefaultFov = _cfgDefaultFov.Value;
                Settings.ApplyFov = _cfgApplyFov.Value;
                Settings.TransitionSeconds = _cfgTransitionSeconds.Value;
                Settings.AutoCameraCycleEnabled = _cfgAutoCameraCycleEnabled.Value;
                Settings.AutoCameraStaySeconds = Mathf.Clamp(_cfgAutoCameraStaySeconds.Value, 1f, 120f);
                Settings.TransitionEasing = _cfgTransitionEasing.Value;
                Settings.WindowX = _windowRect.x;
                Settings.WindowY = _windowRect.y;
                Settings.SaveWithBoneLink = _selectedSaveMode != SaveModeNormal;
                Settings.SelectedBoneTarget = _selectedBoneTarget;
                Settings.SelectedSaveMode = _selectedSaveMode;
                Settings.SaveCameraPoseOverrides = _saveCameraPoseOverrides;
                Settings.SaveRelativeDirection = _saveRelativeDirection;
                Settings.SaveRollWithBone = _saveRollWithBone;
                Settings.RelRebakeOnManual = _relRebakeOnManual;
                Settings.RelFloorGuard = _relFloorGuard;
                Settings.RelYawUpAxisFallback = _relYawUpAxisFallback;
                Settings.PosePresetAutoApply = _posePresetAutoApply;
                Settings.GizmoVisible = _gizmoVisible;
                Settings.GizmoSize = _cfgGizmoSize.Value;
                Settings.BeatFovZoomEnabled = _cfgBeatFovZoomEnabled.Value;
                Settings.BeatFovZoomMultiplier = Mathf.Clamp(_cfgBeatFovZoomMultiplier.Value, 1f, 3f);
                Settings.Presets = _presets.ToList();
                SettingsStore.Save(Path.Combine(PluginDir, "CameraControlSettings.json"), Settings);
            }
            catch (Exception ex)
            {
                LogWarn("camera settings save failed: " + ex.Message);
            }
        }

        private void SetStatus(string text)
        {
            _statusMessage = text;
            LogInfo("camera: " + text);
        }

        private static Vector3 ToVector3(float[] values, Vector3 fallback)
        {
            if (values == null || values.Length < 3)
                return fallback;
            return new Vector3(values[0], values[1], values[2]);
        }

        private static Vector3 ToVector3(float[] values)
        {
            return ToVector3(values, Vector3.zero);
        }

        private static string FormatVector(string label, Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1:0.##}, {2:0.##}, {3:0.##}",
                label, value.x, value.y, value.z);
        }

        private static float[] ToArray(Vector3 value)
        {
            return new[] { value.x, value.y, value.z };
        }

        private Vector3 GetLiveCameraWorldPosition()
        {
            if (_ctrlCamera != null)
                return _ctrlCamera.transform.position;

            return Vector3.zero;
        }

        // カメラ位置オフセットを「ボーン回転に依存しない」安定フレームで扱うための基準回転。
        // transBase（シーン基準）の回転を使う。transBase が無ければワールド（identity）。
        private Quaternion GetTransBaseRotation()
        {
            return _ctrlCamera != null && _ctrlCamera.transBase != null
                ? _ctrlCamera.transBase.rotation
                : Quaternion.identity;
        }

        // ゲーム側 CameraUpdate と同じ式でカメラのワールド位置を再構成する。
        // transform.position = (transBase.rotation*Euler(Rot)) * Dir + transBase.TransformPoint(Pos)
        // 「CameraData が表す本来の着地位置」を、実 transform と突き合わせるために使う。
        private Vector3 CameraWorldFromData(BaseCameraControl_Ver2.CameraData d)
        {
            if (_ctrlCamera == null)
                return Vector3.zero;
            Quaternion worldRot = _ctrlCamera.transBase != null
                ? _ctrlCamera.transBase.rotation * Quaternion.Euler(d.Rot)
                : Quaternion.Euler(d.Rot);
            Vector3 pivot = _ctrlCamera.transBase != null
                ? _ctrlCamera.transBase.TransformPoint(d.Pos)
                : d.Pos;
            return worldRot * d.Dir + pivot;
        }

        // 呼び出し直後の位置診断。位置ONのボーンプリセットで、
        // 「保存オフセットが指すべき世界座標」と「組み立てた CameraData が示す世界座標」と
        // 「現在のライブ位置」を並べて出す。3 つが食い違うなら再構成式かボーン解決の問題。
        private void LogRecallPositionDiag(CameraPreset preset, BaseCameraControl_Ver2.CameraData target)
        {
            if (preset == null || !preset.UseBoneLink)
                return;

            ChaControl female = ResolveMainFemale();
            Transform bone = female != null
                ? ResolveBoneTargetStatic(GetFemaleBoneCache(female), preset.BoneTarget, preset.BoneName)
                : null;
            bool hasBonePos = preset.CameraOffsetBonePos != null && preset.CameraOffsetBonePos.Length >= 3;
            Vector3 offset = hasBonePos ? ToVector3(preset.CameraOffsetBonePos) : GetPresetCameraOffsetLocal(preset);
            Vector3 targetFromOffset = bone == null
                ? Vector3.zero
                : (hasBonePos
                    ? bone.position + GetTransBaseRotation() * ToVector3(preset.CameraOffsetBonePos)
                    : bone.TransformPoint(offset));
            Vector3 targetFromData = CameraWorldFromData(target);
            Vector3 liveNow = GetLiveCameraWorldPosition();
            LogInfo("[recall-pos] preset=" + (preset.Name ?? "?")
                + " usePos=" + HasPresetCameraPosition(preset)
                + " frame=" + (hasBonePos ? "bonePos" : "boneLocal")
                + " bone=" + (bone != null ? bone.name : "null")
                + " bonePos=" + (bone != null ? bone.position.ToString("0.###") : "-")
                + " storedOffset=" + offset.ToString("0.####")
                + " targetFromOffset=" + targetFromOffset.ToString("0.###")
                + " targetFromData=" + targetFromData.ToString("0.###")
                + " liveNow=" + liveNow.ToString("0.###"));
        }

        // カメラ実位置を _transitionHoldCamWorld に固定するよう Dir を作り直す。
        // 注視点(data.Pos)と Rot はそのまま＝カメラ本体は動かさず視線だけ動く。
        private BaseCameraControl_Ver2.CameraData HoldCameraWorldPosition(BaseCameraControl_Ver2.CameraData data)
        {
            if (_ctrlCamera == null)
                return data;

            Vector3 lookAtWorld = _ctrlCamera.transBase != null
                ? _ctrlCamera.transBase.TransformPoint(data.Pos)
                : data.Pos;
            Quaternion worldRot = _ctrlCamera.transBase != null
                ? _ctrlCamera.transBase.rotation * Quaternion.Euler(data.Rot)
                : Quaternion.Euler(data.Rot);
            data.Dir = Quaternion.Inverse(worldRot) * (_transitionHoldCamWorld - lookAtWorld);
            return data;
        }

        private BaseCameraControl_Ver2.CameraData BuildCameraDataFromPreset(
            CameraPreset preset,
            BaseCameraControl_Ver2.CameraData fallback)
        {
            if (preset != null && preset.UseBoneLink && TryBuildBoneLinkedCameraData(preset, fallback, out BaseCameraControl_Ver2.CameraData boneLinked))
                return boneLinked;

            BaseCameraControl_Ver2.CameraData data = fallback;
            Vector3 legacyTarget = ToVector3(preset.LegacyLookAt, fallback.Pos);
            data.Pos = ToVector3(preset.TargetPosition, legacyTarget);
            data.Rot = ToVector3(preset.Rotation, fallback.Rot);

            Vector3 legacyCameraPosition = ToVector3(preset.LegacyPosition, Vector3.zero);
            bool hasLegacyCameraPosition = preset.LegacyPosition != null && preset.LegacyPosition.Length >= 3;
            if (preset.CameraDirection != null && preset.CameraDirection.Length >= 3)
            {
                data.Dir = ToVector3(preset.CameraDirection, fallback.Dir);
            }
            else if (hasLegacyCameraPosition)
            {
                Quaternion rot = Quaternion.Euler(data.Rot);
                data.Dir = Quaternion.Inverse(rot) * (legacyCameraPosition - data.Pos);
            }
            else
            {
                data.Dir = fallback.Dir;
            }

            data.Fov = Mathf.Clamp(preset.Fov <= 0f ? fallback.Fov : preset.Fov, 5f, 120f);
            return data;
        }

        private CameraPreset BuildBoneLinkedPreset(string name, BaseCameraControl_Ver2.CameraData data)
        {
            ChaControl female = ResolveMainFemale();
            if (female == null)
                return null;

            Transform[] boneCache = GetFemaleBoneCache(female);
            Transform bone = ResolveBoneTarget(boneCache, _selectedBoneTarget);
            if (bone == null)
                return null;

            // カメラ位置(ボーン相対)は「使う」チェックに関係なく常に記録する（保存し忘れ防止）。
            // SaveCameraPosition フラグは「呼び出しで使うか（個別トグル）」を表す。
            // ・CameraOffsetLocal … 旧フォーマット（ボーン全変換相対）。互換のため引き続き保存。
            // ・CameraOffsetBonePos … ボーン位置基準＋transBase回転フレーム（回転非依存）。呼び出しはこちらを優先。
            //   頭などボーンが回転しても着地点が振り回されないようにするための主データ。
            float[] cameraOffsetLocal = _ctrlCamera.thisCmaera != null
                ? ToArray(bone.InverseTransformPoint(_ctrlCamera.thisCmaera.transform.position))
                : null;
            float[] cameraOffsetBonePos = _ctrlCamera.thisCmaera != null
                ? ToArray(Quaternion.Inverse(GetTransBaseRotation()) * (_ctrlCamera.thisCmaera.transform.position - bone.position))
                : null;
            bool useCameraPosition = true; // 新規ボーン保存は既定で「位置を使う」。以後は呼び出しタブの▷展開で切替。

            // 注視点は「今実際に見ている点」をボーン基準の相対座標で記録する。
            // 従来は GetCurrentBoneLinkOffsetLocal()（新規保存時はゼロ＝ボーン原点）を入れていたため、
            // 例:頭を見ながら腰ボーンで保存→呼び出すと腰の中心を向いてしまっていた。
            // data.Pos は transBase ローカルの注視点なので、ワールド化してからボーン相対へ変換する。
            Vector3 lookAtWorld = _boneLinkActive && _lookAtAnchor != null
                ? _lookAtAnchor.position
                : (_ctrlCamera.transBase != null
                    ? _ctrlCamera.transBase.TransformPoint(data.Pos)
                    : data.Pos);
            bool worldY = _boneLinkActive && _activeLookAtWorldY;
            float[] lookAtOffsetLocal = ToArray(worldY ? new Vector3(0f, lookAtWorld.y - bone.position.y, 0f) : bone.InverseTransformPoint(lookAtWorld));

            // 保存時に焼かれるカメラ位置(ボーン相対)を記録。呼び出し時の [recall-pos] storedOffsetLocal と
            // 突き合わせれば、上書きのたびにオフセットが流れていく（ラチェット）かどうかが分かる。
            LogInfo("[save-pos] preset=" + name + " bone=" + bone.name
                + " bonePos=" + bone.position.ToString("0.###")
                + " camWorld=" + (_ctrlCamera.thisCmaera != null ? _ctrlCamera.thisCmaera.transform.position.ToString("0.###") : "-")
                + " savedOffsetLocal=" + (cameraOffsetLocal != null ? ToVector3(cameraOffsetLocal).ToString("0.####") : "null")
                + " savedOffsetBonePos=" + (cameraOffsetBonePos != null ? ToVector3(cameraOffsetBonePos).ToString("0.####") : "null"));

            // 相対方向: 「注視点から見たカメラの方向と距離」を、体の向き(腰のヨー)基準で焼き込む。
            //
            // 基準点は注視点。腰の位置を基準にすると、焼いた高さ差を腰から絶対量で足すことになり、
            // 体位で腰が下がる(立ち1.1m→座り0.3m)だけカメラが同じ量だけ沈んで床下へ潜る。
            // 構図の中心から測れば、体が下がってもフレーミングごと付いていく。
            //
            // 使う時だけ焼く。チェックOFFでも焼くと「データはあるがフラグはfalse」という
            // 食い違った状態がファイルに残り、次の起動で相対方向が丸ごと死ぬ。
            // データの有無そのものを「使うつもりで保存した」の印にする。
            Transform frameBone = ResolveFrameBone(boneCache, DefaultFrameBoneName) ?? bone;
            float[] relDirLocal = null;
            float relDistance = 0f;
            if (_saveRelativeDirection && _ctrlCamera.thisCmaera != null)
            {
                Vector3 toCam = _ctrlCamera.thisCmaera.transform.position - lookAtWorld;
                relDistance = toCam.magnitude;
                if (relDistance > 0.0001f)
                    relDirLocal = ToArray(Quaternion.Inverse(GetFrameYaw(frameBone, _relYawUpAxisFallback)) * (toCam / relDistance));
                else
                    relDistance = 0f;
            }

            LogInfo("[save-reldir] preset=" + name
                + " frameBone=" + frameBone.name
                + " use=" + _saveRelativeDirection
                + " roll=" + _saveRollWithBone
                + " dist=" + relDistance.ToString("0.###")
                + " dirLocal=" + (relDirLocal != null ? ToVector3(relDirLocal).ToString("0.###") : "null"));

            return new CameraPreset
            {
                Name = name,
                UseBoneLink = true,
                BoneTarget = _selectedBoneTarget,
                BoneName = bone.name,
                UseRelativeDirection = relDirLocal != null,
                RelativeFromLookAt = relDirLocal != null,
                RelativeDirLocal = relDirLocal,
                RelativeDistance = relDirLocal != null ? relDistance : 0f,
                RollWithBone = relDirLocal != null && _saveRollWithBone,
                FrameBoneName = relDirLocal != null ? frameBone.name : null,
                LookAtOffsetLocal = lookAtOffsetLocal,
                LookAtWorldY = worldY,
                SaveCameraPosition = useCameraPosition,
                CameraOffsetLocal = cameraOffsetLocal,
                CameraOffsetBonePos = cameraOffsetBonePos,
                CameraOffsetWorld = null,
                RotationOffset = null,
                Fov = data.Fov,
                TargetPosition = ToArray(bone.position),
                CameraDirection = ToArray(data.Dir),
                Rotation = ToArray(data.Rot)
            };
        }

        private bool TryBuildBoneLinkedCameraData(
            CameraPreset preset,
            BaseCameraControl_Ver2.CameraData fallback,
            out BaseCameraControl_Ver2.CameraData result)
        {
            result = fallback;
            if (_ctrlCamera == null)
                return false;

            ChaControl female = ResolveMainFemale();
            if (female == null)
                return false;

            Transform[] boneCache = GetFemaleBoneCache(female);
            Transform bone = ResolveBoneTargetStatic(boneCache, preset.BoneTarget, preset.BoneName);

            // 相対方向の枝に入らなかった時だけ、その判定材料を出す。
            // 入った時は endpoint 側に出るので、両方出すと同じ内容で二重になる。
            if (!HasRelativeDirection(preset))
                LogRelDirTrace("[reldir] skip preset=" + (preset.Name ?? "")
                    + " useBoneLink=" + preset.UseBoneLink
                    + " bone=" + (bone != null ? bone.name : "null")
                    + " useRelDir=" + preset.UseRelativeDirection
                    + " dirLen=" + (preset.RelativeDirLocal != null ? preset.RelativeDirLocal.Length : -1)
                    + " dist=" + preset.RelativeDistance.ToString("0.000")
                    + " saveCamPos=" + preset.SaveCameraPosition
                    + " frameBone=" + (preset.FrameBoneName ?? "null"));

            if (bone == null)
            {
                InvalidateFemaleCache();
                return false;
            }

            Vector3 lookAtOffsetLocal = GetPresetBoneLinkOffsetLocal(preset);
            Vector3 lookAt = preset.LookAtWorldY ? bone.position + Vector3.up * lookAtOffsetLocal.y : bone.TransformPoint(lookAtOffsetLocal);

            bool hasCameraOffsetBonePos = preset.CameraOffsetBonePos != null && preset.CameraOffsetBonePos.Length >= 3;
            bool hasCameraOffsetLocal = preset.CameraOffsetLocal != null && preset.CameraOffsetLocal.Length >= 3;
            bool hasCameraOffsetWorld = preset.CameraOffsetWorld != null && preset.CameraOffsetWorld.Length >= 3;

            Vector3 presetCameraWorld = Vector3.zero;
            bool hasPresetCameraWorld = false;

            // 相対方向を使うプリセットは、遷移の終点も相対方向で出す。
            // ここで保存位置(CameraOffsetBonePos)を終点にすると、
            // 到達した瞬間に追従側が相対方向の位置へ置き直すため、
            // 最後の一瞬だけカメラが飛ぶ。終点と追従先を同じ式に揃える。
            if (HasRelativeDirection(preset))
            {
                Transform frameBone = ResolveFrameBone(boneCache, preset.FrameBoneName) ?? bone;
                // 基準点は注視点。旧プリセット(RelativeFromLookAt=false)は焼いた時の意味に合わせて腰基準のまま。
                Vector3 relAnchor = preset.RelativeFromLookAt ? lookAt : frameBone.position;
                presetCameraWorld = ResolveRelativeCameraWorld(relAnchor, GetFrameYaw(frameBone, _relYawUpAxisFallback),
                    ToVector3(preset.RelativeDirLocal).normalized, preset.RelativeDistance, female, _relFloorGuard);
                hasPresetCameraWorld = true;
                LogRelDirTrace("[reldir] endpoint preset=" + (preset.Name ?? "")
                    + " frameBone=" + frameBone.name
                    + " framePos=" + frameBone.position.ToString("0.000")
                    + " target=" + presetCameraWorld.ToString("0.000")
                    + " dist=" + preset.RelativeDistance.ToString("0.000"));
            }
            else if (hasCameraOffsetBonePos)
            {
                // 回転非依存：ボーンの位置 ＋ transBase回転フレームでのオフセット。
                // 頭などボーンが回ってもカメラ着地点は振り回されない。
                presetCameraWorld = bone.position + GetTransBaseRotation() * ToVector3(preset.CameraOffsetBonePos);
                hasPresetCameraWorld = true;
            }
            else if (hasCameraOffsetLocal)
            {
                // 旧フォーマット（ボーン全変換相対）。再保存するまでは従来挙動。
                presetCameraWorld = bone.TransformPoint(ToVector3(preset.CameraOffsetLocal));
                hasPresetCameraWorld = true;
            }
            else if (hasCameraOffsetWorld)
            {
                presetCameraWorld = ToVector3(preset.CameraOffsetWorld);
                hasPresetCameraWorld = true;
            }

            Vector3 cameraPosition;
            // 「位置を使う」(SaveCameraPosition) が ON のプリセットだけ保存カメラ位置へ移動させる。
            // 位置データ(CameraOffsetLocal)は常に保存されるので、データ有無ではなく使うフラグで判定する。
            // （データ有無で判定すると、位置=OFF でもトランジションでカメラを掴んでしまう）
            // 相対方向は「位置をどう決めるか」の指定そのものなので、
            // 「位置を使う」(SaveCameraPosition) のON/OFFに関係なく必ず使う。
            // ここを既存フラグ任せにすると、終点が現在位置のままになり、
            // 到達した瞬間に追従側が相対方向の位置へ置き直して飛ぶ。
            bool useRelDir = HasRelativeDirection(preset);

            if ((preset.SaveCameraPosition || useRelDir) && hasPresetCameraWorld)
            {
                cameraPosition = presetCameraWorld;
            }
            else
            {
                // 位置を使わないボーンプリセットは現在カメラ位置のまま（注視点だけ遷移）。
                cameraPosition = GetLiveCameraWorldPosition();
            }

            Quaternion finalRotation = BuildLookRotation(lookAt - cameraPosition, _ctrlCamera.transform.rotation);

            BaseCameraControl_Ver2.CameraData data = fallback;
            if (_ctrlCamera.transBase != null)
            {
                data.Pos = _ctrlCamera.transBase.InverseTransformPoint(lookAt);
                data.Rot = NormalizeEuler((Quaternion.Inverse(_ctrlCamera.transBase.rotation) * finalRotation).eulerAngles);
            }
            else
            {
                data.Pos = lookAt;
                data.Rot = NormalizeEuler(finalRotation.eulerAngles);
            }

            data.Dir = Quaternion.Inverse(finalRotation) * (cameraPosition - lookAt);
            data.Fov = Mathf.Clamp(preset.Fov <= 0f ? fallback.Fov : preset.Fov, 5f, 120f);

            result = data;
            return true;
        }

        private static Quaternion BuildLookRotation(Vector3 forward, Quaternion fallback)
        {
            return BuildLookRotation(forward, fallback, Vector3.up);
        }

        /// <summary>up を指定できる版。相対方向モードでロール追従を切り替えるために使う。</summary>
        private static Quaternion BuildLookRotation(Vector3 forward, Quaternion fallback, Vector3 up)
        {
            if (forward.sqrMagnitude <= 0.000001f)
                return fallback;

            Vector3 safeUp = up;
            if (safeUp.sqrMagnitude <= 0.000001f
                || Mathf.Abs(Vector3.Dot(safeUp.normalized, forward.normalized)) > 0.999f)
                safeUp = Vector3.up;

            return Quaternion.LookRotation(forward.normalized, safeUp.normalized);
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
        }

        private static float NormalizeAngle(float angle)
        {
            float normalized = angle;
            while (normalized > 180f) normalized -= 360f;
            while (normalized < -180f) normalized += 360f;
            return normalized;
        }

        private static System.Reflection.FieldInfo _lstFemaleField;
        private ChaControl _cachedFemale;
        private static Func<int> _getClothingTarget;
        private static Func<ChaControl> _getReadyIndependentActor;
        private ChaControl _boneCacheOwner;
        private Transform _boneCacheRoot;
        private Transform[] _cachedBoneCache;
        private Transform[] _activeBoneSourceCache;
        private Transform _activeBoneCached;
        private int _activeBoneCachedTarget = -1;
        private string _activeBoneCachedName;

        // 連動中は毎フレーム呼ばれるため、解決済みの女キャラとボーン配列をキャッシュする。
        // キャラ破棄（Unity null）時は自動で、対象ボーン消失時は InvalidateFemaleCache で再解決する。
        private ChaControl ResolveMainFemale()
        {
            ChaControl selectedActor = ResolveSelectedIndependentActor();
            if (selectedActor != null)
            {
                if (_cachedFemale != selectedActor)
                {
                    InvalidateFemaleCache();
                    _cachedFemale = selectedActor;
                }
                return _cachedFemale;
            }
            if (_cachedFemale != null && _getReadyIndependentActor != null
                && _cachedFemale == _getReadyIndependentActor())
                InvalidateFemaleCache();
            if (_cachedFemale != null)
                return _cachedFemale;

            _cachedFemale = ResolveMainFemaleStatic();
            _cachedBoneCache = null;
            _boneCacheOwner = null;
            _boneCacheRoot = null;
            return _cachedFemale;
        }

        private static ChaControl ResolveSelectedIndependentActor()
        {
            if (_getClothingTarget == null || _getReadyIndependentActor == null)
            {
                var clothingApi = HarmonyLib.AccessTools.TypeByName("MainGameVMDPlay.Plugin");
                var actorApi = HarmonyLib.AccessTools.TypeByName("MainGameVMDPlay.IndependentActorApi");
                var targetMethod = clothingApi != null ? HarmonyLib.AccessTools.Method(clothingApi, "GetClothingTarget") : null;
                var actorMethod = actorApi != null ? HarmonyLib.AccessTools.Method(actorApi, "GetReadyActor") : null;
                if (targetMethod != null && actorMethod != null)
                {
                    _getClothingTarget = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), targetMethod);
                    _getReadyIndependentActor = (Func<ChaControl>)Delegate.CreateDelegate(typeof(Func<ChaControl>), actorMethod);
                }
            }
            return _getClothingTarget != null && _getClothingTarget() == 0
                ? _getReadyIndependentActor() : null;
        }

        private void InvalidateFemaleCache()
        {
            _cachedFemale = null;
            _cachedBoneCache = null;
            _boneCacheOwner = null;
            _boneCacheRoot = null;
            _activeBoneCached = null;
            _activeBoneSourceCache = null;
            // 骨配列を捨てるので、そこから選んだ追従基準の骨も捨てる。
            InvalidateHeightShiftBone();
        }

        private static ChaControl ResolveMainFemaleStatic()
        {
            HSceneProc proc = UnityEngine.Object.FindObjectOfType<HSceneProc>();
            if (proc != null)
            {
                if (_lstFemaleField == null)
                    _lstFemaleField = HarmonyLib.AccessTools.Field(typeof(HSceneProc), "lstFemale");
                IList listObj = _lstFemaleField != null ? _lstFemaleField.GetValue(proc) as IList : null;
                if (listObj != null)
                {
                    for (int i = 0; i < listObj.Count; i++)
                    {
                        ChaControl cha = listObj[i] as ChaControl;
                        if (cha != null)
                            return cha;
                    }
                }
            }

            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha != null && cha.sex == 1)
                    return cha;
            }

            return null;
        }

        private Transform[] GetFemaleBoneCache(ChaControl female)
        {
            if (female == null)
                return null;
            Transform root = female.animBody != null ? female.animBody.transform : female.transform;
            if (_cachedBoneCache != null && _boneCacheOwner == female && _boneCacheRoot == root)
                return _cachedBoneCache;

            _cachedBoneCache = GetFemaleBoneCacheStatic(female);
            _boneCacheOwner = female;
            _boneCacheRoot = root;
            return _cachedBoneCache;
        }

        private Transform ResolveActiveBoneTarget(ChaControl female)
        {
            Transform[] bones = GetFemaleBoneCache(female);
            if (_activeBoneCached != null && _activeBoneSourceCache == bones
                && _activeBoneCachedTarget == _activeBoneLinkTarget
                && string.Equals(_activeBoneCachedName, _activeBoneLinkBoneName, StringComparison.Ordinal))
                return _activeBoneCached;

            _activeBoneCached = ResolveBoneTargetStatic(bones, _activeBoneLinkTarget, _activeBoneLinkBoneName);
            _activeBoneSourceCache = bones;
            _activeBoneCachedTarget = _activeBoneLinkTarget;
            _activeBoneCachedName = _activeBoneLinkBoneName;
            return _activeBoneCached;
        }

        private static Transform[] GetFemaleBoneCacheStatic(ChaControl female)
        {
            if (female == null)
                return null;

            Transform root = female.animBody != null
                ? female.animBody.transform
                : female.transform;
            return root != null ? root.GetComponentsInChildren<Transform>(true) : null;
        }

        private Transform ResolveBoneTarget(Transform[] boneCache, int boneTarget)
        {
            return ResolveBoneTargetStatic(boneCache, boneTarget, null);
        }

        private static Transform ResolveBoneTargetStatic(Transform[] boneCache, int boneTarget, string preferredName)
        {
            if (boneCache == null)
                return null;

            if (!string.IsNullOrWhiteSpace(preferredName))
            {
                Transform exact = FindBoneByName(boneCache, preferredName);
                if (exact != null)
                    return exact;
            }

            int normalizedTarget = Mathf.Clamp(boneTarget, 0, BoneTargetNameCandidates.Length - 1);
            string[] candidates = BoneTargetNameCandidates[normalizedTarget];
            for (int i = 0; i < candidates.Length; i++)
            {
                Transform found = FindBoneByName(boneCache, candidates[i]);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Transform FindBoneByName(Transform[] boneCache, string name)
        {
            if (boneCache == null || string.IsNullOrWhiteSpace(name))
                return null;

            for (int i = 0; i < boneCache.Length; i++)
            {
                Transform current = boneCache[i];
                if (current != null && string.Equals(current.name, name, StringComparison.Ordinal))
                    return current;
            }

            return null;
        }

        /// <summary>
        /// 基準ボーンの向きから水平回転(ヨー)だけを取り出す。
        /// ピッチ・ロールまで位置に乗せると、寝た体位で焼いた構図を立ち体位で呼んだ時に
        /// 腰の傾き分だけカメラが真下へ回り込み、床下に潜る。
        /// 欲しいのは「体の正面から見て何度の位置か」だけなので水平成分に落とす。
        /// </summary>
        private static Quaternion GetFrameYaw(Transform frameBone, bool useUpAxisFallback)
        {
            if (frameBone == null)
                return Quaternion.identity;

            Vector3 fwd = frameBone.forward;
            Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up);

            if (useUpAxisFallback)
            {
                // 前方向の軸が縦を向くと、地面に潰した後に残る長さが 0.1 程度まで落ちる
                // （実測 fwd=(0.13, 0.99, -0.09) で 0.16）。残りカスの向きで方角が決まるので当てにならない。
                // その時は上方向の軸から取る。前傾なら up、後傾なら -up が体の正面に対応する。
                float vertical = Vector3.Dot(fwd, Vector3.up);
                Vector3 fromUp = Vector3.ProjectOnPlane(
                    vertical < 0f ? frameBone.up : -frameBone.up, Vector3.up);

                // 急に切り替えると方角が飛ぶので、縦を向くほど up 側へ寄せる。
                float weight = Mathf.InverseLerp(0.5f, 0.9f, Mathf.Abs(vertical));
                if (weight > 0f && fromUp.sqrMagnitude > 0.000001f)
                {
                    flat = flat.sqrMagnitude > 0.000001f
                        ? Vector3.Slerp(flat.normalized, fromUp.normalized, weight)
                        : fromUp;
                }
            }
            else if (flat.sqrMagnitude <= 0.0001f)
            {
                flat = Vector3.ProjectOnPlane(frameBone.up, Vector3.up);
            }

            if (flat.sqrMagnitude <= 0.000001f)
                return Quaternion.identity;

            return Quaternion.LookRotation(flat.normalized, Vector3.up);
        }

        /// <summary>
        /// 相対方向からカメラのワールド位置を出す。
        /// 床より下に出る時は、ヨーと距離を保ったまま仰角だけ起こして床の高さに乗せる。
        /// Y だけ持ち上げると距離も向きも同時に狂って、寄り引きが勝手に変わる。
        /// </summary>
        private static Vector3 ResolveRelativeCameraWorld(
            Vector3 anchor, Quaternion frameYaw, Vector3 dirLocal, float distance, ChaControl female,
            bool floorGuard)
        {
            Vector3 dir = frameYaw * dirLocal;
            if (dir.sqrMagnitude <= 0.000001f)
                return anchor;
            dir = dir.normalized;

            Vector3 world = anchor + dir * distance;
            if (!floorGuard || female == null || female.transform == null)
                return world;

            float floor = female.transform.position.y + FloorMargin;
            if (world.y >= floor)
                return world;

            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude <= 0.000001f)
                return new Vector3(world.x, floor, world.z);

            // 床の高さに届く仰角を距離から逆算する。ヨーと距離はそのまま。
            float sin = Mathf.Clamp((floor - anchor.y) / Mathf.Max(distance, 0.0001f), -1f, 1f);
            float cos = Mathf.Sqrt(Mathf.Max(0f, 1f - sin * sin));
            return anchor + (flat.normalized * cos + Vector3.up * sin) * distance;
        }

        /// <summary>
        /// ポーズ遷移の進行度を MainGameHScenePoseInjector から読む。0..1、遷移中でなければ -1。
        ///
        /// ポーズ遷移は Unity のクロスフェードではなく、あちらが骨を直接補間している。
        /// そのため Animator.IsInTransition は常に false を返し、こちらからは遷移が見えない。
        /// DLL 参照は張らない。入っていない環境では -1 が返り、固定秒の寄せに落ちるだけ。
        /// </summary>
        private float GetExternalPoseBlendProgress()
        {
            if (_poseBlendProgressField == null)
            {
                if (Time.unscaledTime < _poseBlendFieldRetryAt)
                    return -1f;
                _poseBlendFieldRetryAt = Time.unscaledTime + PoseBlendFieldRetrySeconds;

                try
                {
                    Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < assemblies.Length; i++)
                    {
                        Type type = assemblies[i].GetType("MainGameHScenePoseInjector.Plugin", false);
                        if (type == null)
                            continue;

                        _poseBlendProgressField = type.GetField("PoseBlendProgress",
                            BindingFlags.Public | BindingFlags.Static);
                        LogInfo("[reldir] poseBlend source=" + (_poseBlendProgressField != null
                            ? "MainGameHScenePoseInjector.Plugin.PoseBlendProgress"
                            : "見つからない(型はあるがフィールドが無い)"));
                        break;
                    }
                }
                catch (Exception ex)
                {
                    LogWarn("poseBlend lookup failed: " + ex.Message);
                }

                if (_poseBlendProgressField == null)
                    return -1f;
            }

            try
            {
                object value = _poseBlendProgressField.GetValue(null);
                return value is float ? (float)value : -1f;
            }
            catch
            {
                _poseBlendProgressField = null;
                return -1f;
            }
        }

        /// <summary>
        /// 相対方向の追従が「今フレーム何をしたか」を一式で残す。
        /// 体勢・ボーン・カメラ・焼いた値・実行中の値・通った経路を1行にまとめる。
        /// これが無いと、どのログ行がどの体勢のものか手で対応させることになり、
        /// 「距離が勝手に3倍になった」のような事実の原因が追えない。
        ///
        /// 毎フレームは出さない。次のどれかが起きた時だけ:
        ///   ・体勢が変わった
        ///   ・実行中の相対値(方向/距離)が変わった＝誰かが焼き直した
        ///   ・行き先が大きく動いた
        /// </summary>
        private void LogRelDirSnapshot(Transform frameBone, Transform targetBone, Vector3 lookAt,
            Vector3 relAnchor, Quaternion frameYaw, Vector3 camBefore, Vector3 camWritten,
            Vector3 expected, Vector3 raw, ChaControl female)
        {
            bool poseChanged = !string.Equals(_lastPoseKey ?? string.Empty,
                _relDirSnapPoseKey ?? string.Empty, StringComparison.Ordinal);
            bool relChanged = Vector3.Distance(_activeBoneLinkRelDirLocal, _relDirSnapDirLocal) > 0.002f
                || Mathf.Abs(_activeBoneLinkRelDistance - _relDirSnapDistance) > 0.005f;
            bool targetMoved = Vector3.Distance(expected, _relDirSnapExpected) > PoseShiftMeters;

            if (!poseChanged && !relChanged && !targetMoved)
                return;

            // 連続で変わり続ける間（手動ドラッグ中など）に埋め尽くさないための間引き。
            // 体勢が変わった時だけは必ず出す。
            if (!poseChanged && Time.unscaledTime < _relDirSnapNextLog)
                return;
            _relDirSnapNextLog = Time.unscaledTime + RelDirSnapshotIntervalSeconds;

            _relDirSnapPoseKey = _lastPoseKey ?? string.Empty;
            _relDirSnapDirLocal = _activeBoneLinkRelDirLocal;
            _relDirSnapDistance = _activeBoneLinkRelDistance;
            _relDirSnapExpected = expected;

            CameraPreset saved = FindPresetByName(_activeBoneLinkPresetName);
            string savedText = "無し";
            if (saved != null && saved.RelativeDirLocal != null && saved.RelativeDirLocal.Length >= 3)
                savedText = ToVector3(saved.RelativeDirLocal).ToString("0.000")
                    + "/" + saved.RelativeDistance.ToString("0.000");

            LogInfo("[reldir] snap 理由=" + (poseChanged ? "体勢" : relChanged ? "相対値変化" : "行き先移動")
                + " 体勢=" + (string.IsNullOrEmpty(_lastPoseDisplayName) ? "?" : _lastPoseDisplayName)
                + "(" + (_lastPoseKey ?? "?") + ")"
                + " 経路=" + (_relDirBranch ?? "?")
                + " プリセット=" + (_activeBoneLinkPresetName ?? "")
                + " | 腰=" + frameBone.position.ToString("0.000")
                + " 腰euler=" + frameBone.rotation.eulerAngles.ToString("0.0")
                + " fwd=" + frameBone.forward.ToString("0.00")
                + " up=" + frameBone.up.ToString("0.00")
                + " yawY=" + frameYaw.eulerAngles.y.ToString("0.0")
                + " | 対象ボーン=" + (targetBone != null ? targetBone.name : "null")
                + "@" + (targetBone != null ? targetBone.position.ToString("0.000") : "-")
                + " 注視点=" + lookAt.ToString("0.000")
                + " 基準点=" + relAnchor.ToString("0.000")
                + " ルートY=" + (female != null && female.transform != null
                    ? female.transform.position.y.ToString("0.000") : "-")
                + " | 焼いた値=" + savedText
                + " 実行中=" + _activeBoneLinkRelDirLocal.ToString("0.000")
                + "/" + _activeBoneLinkRelDistance.ToString("0.000")
                + " | カメラ前=" + camBefore.ToString("0.000")
                + " 書込=" + camWritten.ToString("0.000")
                + " 計算=" + expected.ToString("0.000")
                + " ガード前=" + raw.ToString("0.000"));
        }

        /// <summary>相対方向の基準ボーンを名前で引く。見つからなければ null。</summary>
        private Transform ResolveFrameBone(Transform[] boneCache, string frameBoneName)
        {
            if (boneCache == null) return null;
            string want = string.IsNullOrEmpty(frameBoneName) ? DefaultFrameBoneName : frameBoneName;
            for (int i = 0; i < boneCache.Length; i++)
            {
                Transform t = boneCache[i];
                if (t != null && string.Equals(t.name, want, StringComparison.Ordinal))
                    return t;
            }
            return null;
        }

        /// <summary>プリセットの相対方向設定を実行時状態へ写す。null なら無効化。</summary>
        private void ApplyRelativeDirectionState(CameraPreset preset)
        {
            if (!HasRelativeDirection(preset))
            {
                bool wasActive = _activeBoneLinkUseRelativeDir;
                _activeBoneLinkUseRelativeDir = false;
                _activeBoneLinkRelDirLocal = Vector3.zero;
                _activeBoneLinkRelDistance = 0f;
                _activeBoneLinkRollWithBone = false;
                _activeBoneLinkRelFromLookAt = false;
                _activeBoneLinkFrameBoneName = DefaultFrameBoneName;

                if (preset != null)
                    LogInfo("[reldir] off preset=" + (preset.Name ?? "")
                        + " use=" + preset.UseRelativeDirection
                        + " dir=" + (preset.RelativeDirLocal != null ? preset.RelativeDirLocal.Length.ToString() : "null")
                        + " dist=" + preset.RelativeDistance.ToString("0.###"));
                else if (wasActive)
                    LogInfo("[reldir] off (cleared)");
                return;
            }

            _activeBoneLinkUseRelativeDir = true;
            _relDirFollowLogFrames = 0;
            // 前のプリセットの寄せが残っていると、切り替えた瞬間に古い起点へ引っ張られる。
            _relDirEaseUntil = -1f;
            _relDirManualUntil = -1f;
            _relDirHasLastExpected = false;
            _relDirEaseByPoseBlend = false;
            _relDirWasInPoseBlend = false;
            _activeBoneLinkRelDirLocal = ToVector3(preset.RelativeDirLocal).normalized;
            _activeBoneLinkRelDistance = preset.RelativeDistance;
            _activeBoneLinkRollWithBone = preset.RollWithBone;
            _activeBoneLinkRelFromLookAt = preset.RelativeFromLookAt;
            _activeBoneLinkFrameBoneName = string.IsNullOrEmpty(preset.FrameBoneName)
                ? DefaultFrameBoneName
                : preset.FrameBoneName;

            LogInfo("[reldir] active preset=" + (preset.Name ?? "")
                + " anchor=" + (_activeBoneLinkRelFromLookAt ? "lookAt" : "hips(旧)")
                + " frameBone=" + _activeBoneLinkFrameBoneName
                + " dist=" + _activeBoneLinkRelDistance.ToString("0.###")
                + " roll=" + _activeBoneLinkRollWithBone);
        }

        private void UpdateBoneLinkedCamera()
        {
            if (!_boneLinkActive || _transitionActive || _ctrlCamera == null)
                return;

            ChaControl female = ResolveMainFemale();
            if (female == null)
                return;

            Transform[] boneCache = GetFemaleBoneCache(female);
            Transform bone = ResolveActiveBoneTarget(female);
            if (bone == null)
            {
                InvalidateFemaleCache();
                return;
            }

            EnsureLookAtAnchor();
            if (_lookAtAnchor == null)
                return;

            bool isDraggingAnchor = _lookAtAnchorGizmo != null && _lookAtAnchorGizmo.IsDragging;
            _lookAtAnchor.rotation = bone.rotation;
            if (isDraggingAnchor)
            {
                _activeLookAtWorldY = false;
                _activeBoneLinkLookAtOffsetLocal = bone.InverseTransformPoint(_lookAtAnchor.position);
            }
            else
                _lookAtAnchor.position = ResolveActiveLookAt(bone);

            Vector3 lookAt = _lookAtAnchor.position;

            // 既定では注視点だけをボーンへ追従させ、カメラ本体は置いた場所に留める。
            Vector3 cameraPosition = GetLiveCameraWorldPosition();
            Vector3 upVector = Vector3.up;

            // 相対方向モード:
            // 保存時に焼いた「基準ボーンから見た方向と距離」を毎フレーム作り直す。
            // 体位が変わって基準ボーンが回れば、カメラも同じ相対角度を保って回り込む。
            if (_activeBoneLinkUseRelativeDir)
            {
                Transform frameBone = ResolveFrameBone(boneCache, _activeBoneLinkFrameBoneName) ?? bone;
                Vector3 before = cameraPosition;

                // 相対方向でも、カメラ操作は殺さない。
                // 元の作りは「位置は固定せず、注視点だけ追従」なので、
                // 毎フレーム位置を上書きすると一切動かせなくなる。
                //
                // 使用者がカメラを動かしたら、その新しい位置から
                // 角度と距離を取り直す。以後はその関係を保って追従する。
                Vector3 relAnchor = _activeBoneLinkRelFromLookAt ? lookAt : frameBone.position;
                Quaternion frameYaw = GetFrameYaw(frameBone, _relYawUpAxisFallback);
                Vector3 expected = ResolveRelativeCameraWorld(relAnchor, frameYaw,
                    _activeBoneLinkRelDirLocal, _activeBoneLinkRelDistance, female, _relFloorGuard);

                // 体位が変わると、その体位での相対角度位置＝行き先そのものが大きく変わる。
                // 行き先は正しいが、そこへ1フレームで着くとカメラだけワープしたように見える。
                // 大きく変わった時だけ、時間をかけて移す（＝体の遷移に合わせて動かす）。
                //
                // 毎フレーム寄せるのは駄目。普段の細かい動きまで遅れて、
                // 常に小さい遷移をしているように見える上、被写体が画面内でズレる。
                // 普段は即座に張り付き、体位が変わった時だけ遷移する。
                bool poseShifted = _relDirHasLastExpected
                    && Vector3.Distance(expected, _relDirLastExpected) > PoseShiftMeters;
                _relDirLastExpected = expected;
                _relDirHasLastExpected = true;

                // 床ガードをかける前の素の計算位置。ガードが効いたかの判定に使う。
                Vector3 raw = relAnchor + frameYaw * _activeBoneLinkRelDirLocal * _activeBoneLinkRelDistance;

                // 体のポーズ遷移そのものの進行度を取る。固定秒で寄せると、
                // 体とカメラが別の時計で動いて終わる時刻がずれる。同じ時計に乗る。
                float poseBlend = GetExternalPoseBlendProgress();
                bool inPoseBlend = poseBlend >= 0f;
                bool poseBlendStarted = inPoseBlend && !_relDirWasInPoseBlend;
                _relDirWasInPoseBlend = inPoseBlend;

                // 手動操作の判定。
                // ・プラグインの窓を触っている間は「カメラ操作」ではない。
                //   ここを見ないと、窓のドラッグやボタン押しで焼いた角度が上書きされる。
                // ・中ボタン(平行移動)とホイール(寄り引き)も操作に数える。
                //   ホイールは押しっぱなしにならないので、少しの猶予を持たせて
                //   ゲーム側が動かし終えた位置から角度と距離を取り直す。
                //   猶予を持たせないと、寄り引きが毎フレーム引き戻されて効かない。
                bool pointerOnUi = _windowPointerActive || _windowDragging;
                bool mouseDragging = !pointerOnUi
                    && (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2));
                bool wheelMoved = !pointerOnUi
                    && Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f;

                if (mouseDragging || wheelMoved)
                    _relDirManualUntil = Time.unscaledTime + ManualGraceSeconds;

                // 書き込んだ位置は次のフレームの実測に必ずしも反映されないため、
                // 位置の差では判定しない。自分の書き込みを手動操作と誤認して
                // その場に居座り、一切動かなくなる。
                bool manualNow = Time.unscaledTime < _relDirManualUntil;

                // 体位が変わったフレームで遷移を仕込む。今いる場所を起点にする。
                // 遷移中にさらに体位が変わったら、その時点の場所から引き直す。
                if ((poseShifted || poseBlendStarted) && !manualNow)
                {
                    _relDirEaseFrom = before;
                    // 体が遷移中なら、その進行度に乗る（＝終わる時刻まで体と一致する）。
                    // 遷移として認識できない場合だけ、固定秒で逃がす。
                    _relDirEaseByPoseBlend = inPoseBlend;
                    _relDirEaseSeconds = PoseShiftSeconds;
                    _relDirEaseUntil = Time.unscaledTime + PoseShiftSeconds;
                    _relDirEaseNextLog = 0f;
                    // 行き先が動いた原因を切り分けるため、内訳を全部出す。
                    // ・anchor が動いた → 被写体が動いただけ（正しい追従）
                    // ・yaw が飛んだ → 体の向きの計算が不連続（GetFrameYaw のフォールバック）
                    // ・raw と to が違う → 床ガードが効いて仰角が起こされている
                    LogInfo("[reldir] pose-shift from=" + before.ToString("0.000")
                        + " to=" + expected.ToString("0.000")
                        + " gap=" + Vector3.Distance(before, expected).ToString("0.000") + "m"
                        + " | anchor=" + relAnchor.ToString("0.000")
                        + " yawY=" + frameYaw.eulerAngles.y.ToString("0.0")
                        + " boneFwd=" + frameBone.forward.ToString("0.00")
                        + " boneUp=" + frameBone.up.ToString("0.00")
                        + " dirLocal=" + _activeBoneLinkRelDirLocal.ToString("0.000")
                        + " dist=" + _activeBoneLinkRelDistance.ToString("0.000")
                        + " raw=" + raw.ToString("0.000")
                        + " floorGuard=" + (Vector3.Distance(raw, expected) > 0.001f)
                        + " | poseBlend=" + inPoseBlend
                        + " t=" + poseBlend.ToString("0.00"));
                }

                if (manualNow)
                {
                    // 操作中はカメラを譲る（触っている間は書き込まない）。
                    // 焼き直しはチェックが入っている時だけ。切ってあれば、手を離した瞬間に
                    // 焼いた構図へ戻る。入れてあると、その位置で角度と距離を取り直して以後それを使う。
                    if (_relRebakeOnManual)
                    {
                        Vector3 toCam = before - relAnchor;
                        float d = toCam.magnitude;
                        if (d > 0.0001f)
                        {
                            _activeBoneLinkRelDirLocal = Quaternion.Inverse(frameYaw) * (toCam / d);
                            _activeBoneLinkRelDistance = d;
                        }
                    }
                    cameraPosition = before;
                    _relDirEaseUntil = -1f;
                    _relDirEaseByPoseBlend = false;
                    _relDirBranch = "manual";
                }
                else if (_relDirEaseByPoseBlend)
                {
                    if (inPoseBlend)
                    {
                        // 体の遷移と同じ進行度で動かす。カメラの着地は体の着地と同時になる。
                        // 進行度は向こうで既に端を滑らかにしてあるので、ここで重ねてかけない。
                        cameraPosition = Vector3.Lerp(_relDirEaseFrom, expected, poseBlend);
                        _relDirBranch = "blend-ease";

                        if (Time.unscaledTime >= _relDirEaseNextLog)
                        {
                            _relDirEaseNextLog = Time.unscaledTime + 0.2f;
                            LogDebug("[reldir] blend-ease t=" + poseBlend.ToString("0.00")
                                + " write=" + cameraPosition.ToString("0.000")
                                + " expected=" + expected.ToString("0.000")
                                + " remain=" + Vector3.Distance(before, expected).ToString("0.000") + "m");
                        }
                    }
                    else
                    {
                        // 体の遷移が終わった。カメラも同時に着地させる。
                        LogDebug("[reldir] blend-ease done write=" + expected.ToString("0.000")
                            + " gap=" + Vector3.Distance(before, expected).ToString("0.000") + "m");
                        _relDirEaseByPoseBlend = false;
                        _relDirEaseUntil = -1f;
                        cameraPosition = expected;
                        _relDirBranch = "blend-done";
                    }
                }
                else if (_relDirEaseUntil > 0f && Time.unscaledTime < _relDirEaseUntil)
                {
                    // 切り替え直後は、今いる場所から計算位置へ秒数をかけて寄せる。
                    // 一気に置き直すと、遷移が位置を運んでいない分がそのまま飛びになる。
                    float total = Mathf.Max(0.01f, _relDirEaseSeconds);
                    float t = 1f - Mathf.Clamp01((_relDirEaseUntil - Time.unscaledTime) / total);
                    float k = t * t * (3f - 2f * t);
                    cameraPosition = Vector3.Lerp(_relDirEaseFrom, expected, k);
                    _relDirBranch = "timed-ease";

                    // 寄せの全区間を記録する。着いているか、途中で止まっているかを見る。
                    if (Time.unscaledTime >= _relDirEaseNextLog)
                    {
                        _relDirEaseNextLog = Time.unscaledTime + 0.5f;
                        LogDebug("[reldir] ease t=" + t.ToString("0.00")
                            + " live=" + before.ToString("0.000")
                            + " write=" + cameraPosition.ToString("0.000")
                            + " expected=" + expected.ToString("0.000")
                            + " remain=" + Vector3.Distance(before, expected).ToString("0.000") + "m");
                    }
                }
                else if (_relDirEaseUntil > 0f)
                {
                    // 寄せ終了。着地点と計算位置の差を残す。
                    LogDebug("[reldir] ease done live=" + before.ToString("0.000")
                        + " expected=" + expected.ToString("0.000")
                        + " gap=" + Vector3.Distance(before, expected).ToString("0.000") + "m");
                    _relDirEaseUntil = -1f;
                    cameraPosition = expected;
                    _relDirBranch = "timed-done";
                }
                else
                {
                    // 通常追従。被写体に即座に張り付く。
                    cameraPosition = expected;
                    _relDirEaseUntil = -1f;
                    _relDirBranch = "normal";
                }

                LogRelDirSnapshot(frameBone, bone, lookAt, relAnchor, frameYaw,
                    before, cameraPosition, expected, raw, female);

                // 追従に切り替わった直後の数フレームだけ、飛んだ量を数字で残す。
                // 遷移の終点と追従先がずれていれば、ここに大きな差が出る。
                if (_relDirFollowLogFrames == 0)
                {
                    // 追従に切り替わった最初のフレーム。今の位置を起点に寄せを始める。
                    _relDirEaseFrom = before;
                    _relDirEaseSeconds = Mathf.Max(0.01f, _cfgTransitionSeconds != null ? _cfgTransitionSeconds.Value : 1f);
                    _relDirEaseUntil = Time.unscaledTime + _relDirEaseSeconds;
                    _relDirEaseNextLog = 0f;
                    // プリセット呼び出し由来の寄せなので、体の遷移には乗せない。
                    _relDirEaseByPoseBlend = false;
                    cameraPosition = before;
                }

                if (_relDirFollowLogFrames < 3)
                {
                    _relDirFollowLogFrames++;
                    LogDebug("[reldir] follow#" + _relDirFollowLogFrames
                        + " frameBone=" + frameBone.name
                        + " framePos=" + frameBone.position.ToString("0.000")
                        + " before=" + before.ToString("0.000")
                        + " after=" + cameraPosition.ToString("0.000")
                        + " jump=" + Vector3.Distance(before, cameraPosition).ToString("0.000") + "m"
                        + " transitionActive=" + _transitionActive);
                }

                // ロール追従が切ってあれば水平を保つ。
                // 入れてあると寝転がった時に画面ごと倒れる。
                if (_activeBoneLinkRollWithBone)
                    upVector = frameBone.up;
            }

            Quaternion finalRotation = BuildLookRotation(lookAt - cameraPosition, _ctrlCamera.transform.rotation, upVector);

            BaseCameraControl_Ver2.CameraData data = _ctrlCamera.GetCameraData();
            if (_ctrlCamera.transBase != null)
            {
                data.Pos = _ctrlCamera.transBase.InverseTransformPoint(lookAt);
                data.Rot = NormalizeEuler((Quaternion.Inverse(_ctrlCamera.transBase.rotation) * finalRotation).eulerAngles);
            }
            else
            {
                data.Pos = lookAt;
                data.Rot = NormalizeEuler(finalRotation.eulerAngles);
            }

            data.Dir = Quaternion.Inverse(finalRotation) * (cameraPosition - lookAt);
            if (_cfgApplyFov.Value)
                data.Fov = ResolveBeatBaseFov();
            else
                data.Fov = ResolveCurrentBaseFov(data.Fov);

            ApplyCameraData(data);
            _currentData = data;
            _targetData = data;
        }

        private void ResetCameraFollowState()
        {
            _transitionActive = false;
            _transitionState = null;
            ClearBoneLinkActiveState();
            _activeCameraPresetName = string.Empty;
            if (_ctrlCamera != null)
            {
                _currentData = _ctrlCamera.GetCameraData();
                _targetData = _currentData;
            }
            SetStatus("Reset");
        }

        private bool IsSelectedPreset(CameraPreset preset)
        {
            if (preset == null || string.IsNullOrEmpty(_activeCameraPresetName))
                return false;
            return string.Equals(preset.Name ?? string.Empty, _activeCameraPresetName, StringComparison.Ordinal);
        }

        private void SaveOrOverwritePreset()
        {
            string targetName = string.IsNullOrWhiteSpace(_presetNameBuffer)
                ? string.Empty
                : _presetNameBuffer.Trim();

            if (string.IsNullOrWhiteSpace(targetName))
            {
                SetStatus("保存できません（名前を入力してください）");
                return;
            }

            CameraPreset existing = FindPresetByName(targetName);
            if (existing != null)
                OverwritePreset(existing);
            else
                SaveCurrentAsPreset(targetName);
        }

        // 呼び出しタブの行の ↻：体位を更新する（現体位のオーバーライドを更新／無ければ追加）。
        // 体位情報が取れない時だけ基底を更新する。
        private void OverwritePresetPoseAware(CameraPreset preset)
        {
            if (preset == null)
                return;

            if (TryGetCurrentPoseInfo(out string key, out string displayName))
            {
                preset.UsePoseOverrides = true;
                if (CapturePoseOverrideToPreset(preset, key, displayName))
                {
                    CommitPresets("overwrite-pose:" + (preset.Name ?? string.Empty) + ":" + key);
                    SetStatus("体位更新: " + (preset.Name ?? string.Empty) + " / " + displayName);
                }
                else
                {
                    SetStatus("体位更新に失敗");
                }
                return;
            }

            // 体位が取れない → 基底を更新
            OverwritePreset(preset);
        }

        private void OverwritePreset(CameraPreset preset)
        {
            if (preset == null)
                return;

            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
            {
                SetStatus("CameraControl_Ver2 が見つからない");
                return;
            }

            BaseCameraControl_Ver2.CameraData data = _ctrlCamera.GetCameraData();
            data.Fov = ResolveCurrentBaseFov(data.Fov);
            string name = preset.Name ?? string.Empty;

            CameraPreset updated;
            switch (_selectedSaveMode)
            {
                case SaveModeBoneLink:
                    updated = BuildBoneLinkedPreset(name, data);
                    break;
                default:
                    updated = new CameraPreset
                    {
                        Name = name,
                        TargetPosition = ToArray(data.Pos),
                        CameraDirection = ToArray(data.Dir),
                        Rotation = ToArray(data.Rot),
                        Fov = data.Fov
                    };
                    break;
            }

            if (updated == null)
            {
                SetStatus("上書き失敗");
                return;
            }

            updated.UsePoseOverrides = preset.UsePoseOverrides;
            updated.PoseOverrides = preset.PoseOverrides;

            int index = _presets.IndexOf(preset);
            if (index >= 0)
                _presets[index] = updated;

            _activeCameraPresetName = name;
            _presetNameBuffer = name;
            // UI_REDESIGN §3: 追従中の同名プリセットを上書きしたときは、ライブ追従状態を
            // LoadPreset と同じ考え方で全項目同期する。Target/BoneName/Fov だけ更新して
            // SaveCameraPosition/CameraOffsetLocal/LookAtOffsetLocal を取り残さない。
            // 通常モードへ上書きした場合は古い追従状態を残さず解除する。
            if (_boneLinkActive && string.Equals(_activeBoneLinkPresetName, name, StringComparison.Ordinal))
            {
                if (updated.UseBoneLink)
                {
                    _activeBoneLinkPresetName = name;
                    _activeBoneLinkTarget = updated.BoneTarget;
                    _activeBoneLinkBoneName = updated.BoneName ?? string.Empty;
                    _activeBoneLinkLookAtOffsetLocal = GetPresetBoneLinkOffsetLocal(updated);
                _activeLookAtWorldY = updated.LookAtWorldY;
                    _activeBoneLinkSaveCameraPosition = HasPresetCameraPosition(updated);
                    ApplyRelativeDirectionState(updated);
                    _activeBoneLinkCameraOffsetLocal = _activeBoneLinkSaveCameraPosition
                        ? GetPresetCameraOffsetLocal(updated)
                        : Vector3.zero;
                    _activeBoneLinkFov = updated.Fov;
                    EnsureLookAtAnchor();
                    SyncLookAtAnchorToActiveBoneLink(force: true);
                }
                else
                {
                    // 追従中プリセットが通常モードへ変わった → ライブ追従を解除
                    ClearBoneLinkActiveState();
                }
            }

            CommitPresets("overwrite:" + name);
            _currentData = data;
            _targetData = data;
            LogCameraData("overwrite-preset", data, updated);
            SetStatus("保存: " + name);
        }

        private bool IsActiveBoneLinkedPreset(CameraPreset preset)
        {
            if (!_boneLinkActive || preset == null || !preset.UseBoneLink)
                return false;

            return string.Equals(preset.Name ?? string.Empty, _activeBoneLinkPresetName ?? string.Empty, StringComparison.Ordinal)
                && preset.BoneTarget == _activeBoneLinkTarget
                && string.Equals(preset.BoneName ?? string.Empty, _activeBoneLinkBoneName ?? string.Empty, StringComparison.Ordinal);
        }

        private void ClearBoneLinkActiveState()
        {
            _boneLinkActive = false;
            _activeBoneLinkPresetName = string.Empty;
            _activeBoneLinkTarget = -1;
            _activeBoneLinkBoneName = string.Empty;
            _activeBoneLinkLookAtOffsetLocal = Vector3.zero;
            _activeLookAtWorldY = false;
            _activeBoneLinkCameraOffsetLocal = Vector3.zero;
            _activeBoneLinkSaveCameraPosition = false;
            _activeBoneLinkFov = 0f;
            ApplyRelativeDirectionState(null);
            _selectedSaveMode = SaveModeNormal;
            DestroyLookAtAnchor();
            SetStatus("ボーン連動解除");
        }

        private void ActivatePresetNormalLive(CameraPreset preset)
        {
            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
            {
                SetStatus("CameraControl_Ver2 が見つからない");
                return;
            }

            _transitionActive = false;
            _transitionState = null;
            ClearBoneLinkActiveState();
            _selectedSaveMode = SaveModeNormal;
            _activeCameraPresetName = preset != null ? (preset.Name ?? string.Empty) : string.Empty;
            _presetNameBuffer = _activeCameraPresetName;
            _currentData = _ctrlCamera.GetCameraData();
            _currentData.Fov = ResolveCurrentBaseFov(_currentData.Fov);
            _targetData = _currentData;
            SetStatus("通常カメラ編集: " + _activeCameraPresetName);
        }

        private void ActivatePresetBoneLinkLive(CameraPreset preset, int boneTarget)
        {
            boneTarget = Mathf.Clamp(boneTarget, 0, BoneTargetLabels.Length - 1);
            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
            {
                SetStatus("CameraControl_Ver2 が見つからない");
                return;
            }

            ChaControl female = ResolveMainFemale();
            Transform bone = female != null ? ResolveBoneTarget(GetFemaleBoneCache(female), boneTarget) : null;
            if (bone == null)
            {
                InvalidateFemaleCache();
                _selectedSaveMode = SaveModeBoneLink;
                _selectedBoneTarget = boneTarget;
                SetStatus("対象ボーンが見つからない: " + BoneTargetLabels[boneTarget]);
                return;
            }

            BaseCameraControl_Ver2.CameraData live = _ctrlCamera.GetCameraData();
            live.Fov = ResolveCurrentBaseFov(live.Fov);
            Vector3 lookAtWorld = _lookAtAnchor != null
                ? _lookAtAnchor.position
                : (_ctrlCamera.transBase != null ? _ctrlCamera.transBase.TransformPoint(live.Pos) : live.Pos);

            _transitionActive = false;
            _transitionState = null;
            _boneLinkActive = true;
            _beatFovSuspendedForNewController = false;
            _selectedSaveMode = SaveModeBoneLink;
            _selectedBoneTarget = boneTarget;
            _activeCameraPresetName = preset != null ? (preset.Name ?? string.Empty) : string.Empty;
            _presetNameBuffer = _activeCameraPresetName;
            _activeBoneLinkPresetName = _activeCameraPresetName;
            _activeBoneLinkTarget = boneTarget;
            _activeBoneLinkBoneName = bone.name;
            _activeLookAtWorldY = false;
            _activeBoneLinkLookAtOffsetLocal = bone.InverseTransformPoint(lookAtWorld);
            _activeBoneLinkCameraOffsetLocal = _ctrlCamera.thisCmaera != null
                ? bone.InverseTransformPoint(_ctrlCamera.thisCmaera.transform.position)
                : Vector3.zero;
            _activeBoneLinkSaveCameraPosition = false;
            _activeBoneLinkFov = ResolveCurrentBaseFov(live.Fov);
            live.Fov = _activeBoneLinkFov;
            // 呼び出しの入口が複数あるので、ここでもプリセットの相対方向を反映する。
            // 繋ぎ忘れると、保存はされているのに呼び出しても効かない状態になる。
            ApplyRelativeDirectionState(preset);

            EnsureLookAtAnchor();
            SyncLookAtAnchorToActiveBoneLink(force: true);
            _currentData = live;
            _targetData = live;
            SetStatus("ボーンカメラ編集: " + BoneTargetLabels[boneTarget]);
        }

        private void CycleQuickLookAtBoneLock()
        {
            // Ctrl+right click never loads a preset.
            int[] cycle =
            {
                BoneTargetHead,
                BoneTargetChest,
                BoneTargetWaist,
            };

            _quickLookAtBoneCycleIndex = (_quickLookAtBoneCycleIndex + 1) % cycle.Length;
            ActivateQuickLookAtBoneLock(cycle[_quickLookAtBoneCycleIndex]);
        }

        private bool TryCycleQuickPreset(List<CameraPreset> quickPresets = null)
        {
            if (quickPresets == null)
                quickPresets = GetQuickCyclePresets();
            if (quickPresets.Count == 0)
                return false;

            _quickPresetCycleIndex = (_quickPresetCycleIndex + 1) % quickPresets.Count;
            LoadPreset(quickPresets[_quickPresetCycleIndex]);
            return true;
        }

        private List<CameraPreset> GetQuickCyclePresets()
        {
            return _presets
                .Where(x => x != null && x.QuickCycleEnabled)
                .ToList();
        }

        private bool TryGetAutoCycleReturnPreset(out CameraPreset preset)
        {
            preset = null;
            if (_cfgAutoCameraCycleEnabled == null || !_cfgAutoCameraCycleEnabled.Value)
                return false;

            List<CameraPreset> quickPresets = GetQuickCyclePresets();
            if (quickPresets.Count == 0)
                return false;

            preset = quickPresets.FirstOrDefault(x => IsSelectedPreset(x));
            if (preset != null)
                return true;

            if (_quickPresetCycleIndex >= 0 && _quickPresetCycleIndex < quickPresets.Count)
            {
                preset = quickPresets[_quickPresetCycleIndex];
                return preset != null;
            }

            return false;
        }

        private void ResetAutoCameraCycleTimer()
        {
            float staySeconds = _cfgAutoCameraStaySeconds != null ? _cfgAutoCameraStaySeconds.Value : Settings.AutoCameraStaySeconds;
            _nextAutoCameraSwitchTime = Time.unscaledTime + Mathf.Clamp(staySeconds, 1f, 120f);
        }

        private void UpdateAutoCameraCycle()
        {
            if (_cfgAutoCameraCycleEnabled == null || !_cfgAutoCameraCycleEnabled.Value)
            {
                _nextAutoCameraSwitchTime = -1f;
                return;
            }

            if (_transitionActive)
                return;

            if (_nextAutoCameraSwitchTime >= 0f && Time.unscaledTime < _nextAutoCameraSwitchTime)
                return;

            List<CameraPreset> quickPresets = GetQuickCyclePresets();
            if (quickPresets.Count == 0)
                return;

            if (_nextAutoCameraSwitchTime < 0f)
            {
                ResetAutoCameraCycleTimer();
                return;
            }

            if (_quickPresetCycleIndex < 0)
            {
                int activeIndex = quickPresets.FindIndex(x => IsSelectedPreset(x));
                _quickPresetCycleIndex = activeIndex >= 0 ? activeIndex : -1;
            }

            LogInfo("[auto-camera-cycle]"
                + " count=" + quickPresets.Count
                + " nextIndex=" + ((_quickPresetCycleIndex + 1) % quickPresets.Count)
                + " now=" + Time.unscaledTime.ToString("0.###", CultureInfo.InvariantCulture)
                + " due=" + _nextAutoCameraSwitchTime.ToString("0.###", CultureInfo.InvariantCulture)
                + " transition=" + _transitionActive);
            TryCycleQuickPreset(quickPresets);
        }

        private void CaptureVoiceFaceEventReturnState()
        {
            if (_cfgVoiceFaceEventReturnEnabled == null || !_cfgVoiceFaceEventReturnEnabled.Value)
                return;

            if (_voiceFaceReturnPending)
            {
                float existingStaySeconds = _cfgVoiceFaceEventReturnStaySeconds != null
                    ? _cfgVoiceFaceEventReturnStaySeconds.Value
                    : Settings.VoiceFaceEventReturnStaySeconds;
                _voiceFaceReturnTime = Time.unscaledTime + Mathf.Clamp(existingStaySeconds, 0.5f, 60f);
                return;
            }

            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
                return;

            _voiceFaceReturnData = _ctrlCamera.GetCameraData();
            _voiceFaceReturnData.Fov = ResolveCurrentBaseFov(_voiceFaceReturnData.Fov);
            if (TryGetAutoCycleReturnPreset(out CameraPreset returnPreset))
            {
                _voiceFaceReturnUsePreset = true;
                _voiceFaceReturnPresetName = returnPreset.Name ?? string.Empty;
            }
            else
            {
                _voiceFaceReturnUsePreset = false;
                _voiceFaceReturnPresetName = string.Empty;
            }

            float staySeconds = _cfgVoiceFaceEventReturnStaySeconds != null
                ? _cfgVoiceFaceEventReturnStaySeconds.Value
                : Settings.VoiceFaceEventReturnStaySeconds;
            _voiceFaceReturnTime = Time.unscaledTime + Mathf.Clamp(staySeconds, 0.5f, 60f);
            _voiceFaceReturnPending = true;
            LogInfo("[voiceface-return-capture]"
                + " mode=" + (_voiceFaceReturnUsePreset ? "preset" : "camera")
                + " preset=" + _voiceFaceReturnPresetName);
        }

        private void UpdateVoiceFaceEventReturn()
        {
            if (!_voiceFaceReturnPending)
                return;

            if (_cfgVoiceFaceEventReturnEnabled == null || !_cfgVoiceFaceEventReturnEnabled.Value)
            {
                ClearVoiceFaceEventReturnState();
                return;
            }

            if (_transitionActive || Time.unscaledTime < _voiceFaceReturnTime)
                return;

            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
            {
                ClearVoiceFaceEventReturnState();
                return;
            }

            if (_voiceFaceReturnUsePreset && !string.IsNullOrEmpty(_voiceFaceReturnPresetName))
            {
                CameraPreset returnPreset = FindPresetByName(_voiceFaceReturnPresetName);
                if (returnPreset != null)
                {
                    string presetName = _voiceFaceReturnPresetName;
                    ClearVoiceFaceEventReturnState();
                    LoadPreset(returnPreset);
                    SetStatus("VoiceFaceEventカメラ復帰: " + presetName);
                    return;
                }

                LogWarn("[voiceface-return] preset missing, fallback to camera state: " + _voiceFaceReturnPresetName);
            }

            BaseCameraControl_Ver2.CameraData from = _ctrlCamera.GetCameraData();
            from.Fov = ResolveCurrentBaseFov(from.Fov);
            BaseCameraControl_Ver2.CameraData to = _voiceFaceReturnData;
            if (!_cfgApplyFov.Value)
                to.Fov = from.Fov;

            ClearVoiceFaceEventReturnState();
            ClearBoneLinkActiveState();
            _activeCameraPresetName = string.Empty;
            StartCameraTransition(from, to, null, false);
            ResetAutoCameraCycleTimer();
            SetStatus("VoiceFaceEventカメラ復帰");
        }

        private void ClearVoiceFaceEventReturnState()
        {
            _voiceFaceReturnPending = false;
            _voiceFaceReturnUsePreset = false;
            _voiceFaceReturnPresetName = string.Empty;
        }

        private void ActivateQuickLookAtBoneLock(int boneTarget)
        {
            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
            {
                SetStatus("CameraControl_Ver2 が見つからない");
                return;
            }

            ChaControl female = ResolveMainFemale();
            Transform bone = female != null ? ResolveBoneTarget(GetFemaleBoneCache(female), boneTarget) : null;
            if (bone == null)
            {
                InvalidateFemaleCache();
                SetStatus("対象ボーンが見つからない: " + BoneTargetLabels[Mathf.Clamp(boneTarget, 0, BoneTargetLabels.Length - 1)]);
                return;
            }

            BaseCameraControl_Ver2.CameraData live = _ctrlCamera.GetCameraData();
            live.Fov = ResolveCurrentBaseFov(live.Fov);
            // A manual target switch supersedes the old preset interpolation.
            _transitionActive = false;
            _transitionState = null;
            _transitionManualFov = false;
            ClearVoiceFaceEventReturnState();
            ResetAutoCameraCycleTimer();
            // Resolve before enabling bone ownership: afterward the resolver can
            // select an old _lastBaseFov. Publish the captured base once so both
            // bone following and subsequent frames retain the current FOV.
            SetBaseFov(live.Fov);

            _boneLinkActive = true;
            _activeBoneLinkPresetName = "QuickLookAt:" + BoneTargetLabels[Mathf.Clamp(boneTarget, 0, BoneTargetLabels.Length - 1)];
            _activeBoneLinkTarget = Mathf.Clamp(boneTarget, 0, BoneTargetLabels.Length - 1);
            _selectedBoneTarget = _activeBoneLinkTarget;
            _selectedSaveMode = SaveModeBoneLink;
            _activeBoneLinkBoneName = bone.name;
            _activeBoneLinkLookAtOffsetLocal = Vector3.zero;
            _activeLookAtWorldY = false;
            _activeBoneLinkCameraOffsetLocal = Vector3.zero;
            // 素早い注視点ロックはプリセット由来ではないため相対方向は使わない。
            ApplyRelativeDirectionState(null);
            _activeBoneLinkSaveCameraPosition = false;
            _activeBoneLinkFov = live.Fov;
            live.Fov = _activeBoneLinkFov;

            EnsureLookAtAnchor();
            SyncLookAtAnchorToActiveBoneLink(force: true);

            _currentData = live;
            _targetData = live;

            SetStatus("注視点ロック: " + BoneTargetLabels[_activeBoneLinkTarget]);
        }

        // ボーン連動中に対象ボーンを変更する。今のワールド注視点・カメラ位置は動かさず、
        // 新ボーン基準の相対オフセットへ変換して追従対象を乗り換える（カメラはジャンプしない）。
        // 以後そのまま保存すれば、注視点もカメラ位置も新ボーン基準の相対で記録される。
        private void RetargetActiveBoneLink(int newBoneTarget)
        {
            newBoneTarget = Mathf.Clamp(newBoneTarget, 0, BoneTargetLabels.Length - 1);
            if (!_boneLinkActive)
            {
                _selectedBoneTarget = newBoneTarget;
                return;
            }

            RefreshCameraControllerCache();
            ChaControl female = ResolveMainFemale();
            Transform newBone = female != null ? ResolveBoneTarget(GetFemaleBoneCache(female), newBoneTarget) : null;
            if (_ctrlCamera == null || newBone == null)
            {
                // ボーンを解決できない時は選択だけ反映（次回保存/呼び出しで整合）
                _selectedBoneTarget = newBoneTarget;
                return;
            }

            // 現在のワールド注視点（アンカー優先、無ければ現在データから）
            Vector3 lookAtWorld = _lookAtAnchor != null
                ? _lookAtAnchor.position
                : (_ctrlCamera.transBase != null ? _ctrlCamera.transBase.TransformPoint(_currentData.Pos) : _currentData.Pos);

            _activeLookAtWorldY = false;
            _activeBoneLinkLookAtOffsetLocal = newBone.InverseTransformPoint(lookAtWorld);
            if (_ctrlCamera.thisCmaera != null)
                _activeBoneLinkCameraOffsetLocal = newBone.InverseTransformPoint(_ctrlCamera.thisCmaera.transform.position);

            _activeBoneLinkTarget = newBoneTarget;
            _activeBoneLinkBoneName = newBone.name;
            _selectedBoneTarget = newBoneTarget;

            EnsureLookAtAnchor();
            SyncLookAtAnchorToActiveBoneLink(force: true);
            SetStatus("対象ボーン変更: " + BoneTargetLabels[newBoneTarget]);
        }

        private void OverwriteActiveBoneLinkedPreset()
        {
            if (!_boneLinkActive)
            {
                SetStatus("ボーン連動中ではない");
                return;
            }

            CameraPreset preset = _presets.FirstOrDefault(IsActiveBoneLinkedPreset);
            if (preset == null)
            {
                SetStatus("上書き対象が見つからない");
                return;
            }

            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
            {
                SetStatus("CameraControl_Ver2 が見つからない");
                return;
            }

            BaseCameraControl_Ver2.CameraData data = _ctrlCamera.GetCameraData();
            data.Fov = ResolveCurrentBaseFov(data.Fov);
            preset.UseBoneLink = true;
            preset.BoneTarget = _activeBoneLinkTarget;
            preset.BoneName = _activeBoneLinkBoneName ?? string.Empty;
            preset.LookAtOffsetLocal = ToArray(_activeBoneLinkLookAtOffsetLocal);
            preset.LookAtWorldY = _activeLookAtWorldY;
            preset.SaveCameraPosition = _activeBoneLinkSaveCameraPosition;
            preset.CameraOffsetLocal = _activeBoneLinkSaveCameraPosition
                ? ToArray(_activeBoneLinkCameraOffsetLocal)
                : null;
            preset.TargetPosition = ToArray(data.Pos);
            preset.CameraDirection = ToArray(data.Dir);
            preset.Rotation = ToArray(data.Rot);
            preset.Fov = data.Fov;
            _activeBoneLinkFov = data.Fov;

            CommitPresets("overwrite:" + preset.Name);
            LogCameraData("overwrite-preset", data, preset);
            SetStatus("上書き保存: " + preset.Name);
        }

        private float ResolveLiveCameraFov(float fallback)
        {
            if (_ctrlCamera != null && _ctrlCamera.thisCmaera != null)
            {
                float live = _ctrlCamera.thisCmaera.fieldOfView;
                if (!float.IsNaN(live) && !float.IsInfinity(live) && live > 0f)
                    return Mathf.Clamp(live, 5f, 120f);
            }

            return Mathf.Clamp(fallback <= 0f ? Settings.DefaultFov : fallback, 5f, 120f);
        }

        private float ResolveCurrentBaseFov(float fallback)
        {
            if (_lastBaseFov > 0f && (IsBeatFovEnabled() || _cfgApplyFov.Value || _boneLinkActive || _transitionActive))
                return Mathf.Clamp(_lastBaseFov, 5f, 120f);
            float value = _ctrlCamera != null ? _ctrlCamera.CameraFov : fallback;
            return Mathf.Clamp(value <= 0f ? Settings.DefaultFov : value, 5f, 120f);
        }

        private void EnsureBeatLookup()
        {
            if (_beatLookupDone)
                return;

            _beatLookupDone = true;
            _beatPluginType = Type.GetType("MainGameBeatSyncSpeed.Plugin, MainGameBeatSyncSpeed");
            if (_beatPluginType == null)
                return;

            _beatInstanceProperty = _beatPluginType.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            _beatZoneRawTargetField = _beatPluginType.GetField("_currentZoneRawTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            _beatCfgBpmField = _beatPluginType.GetField("_cfgBpm", BindingFlags.Instance | BindingFlags.NonPublic);
            _beatCfgLowIntensityField = _beatPluginType.GetField("_cfgLowIntensity", BindingFlags.Instance | BindingFlags.NonPublic);
            _beatCfgMidIntensityField = _beatPluginType.GetField("_cfgMidIntensity", BindingFlags.Instance | BindingFlags.NonPublic);
            _beatCfgHighIntensityField = _beatPluginType.GetField("_cfgHighIntensity", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private object GetBeatInstance()
        {
            EnsureBeatLookup();
            if (_beatInstanceProperty == null)
                return null;

            try { return _beatInstanceProperty.GetValue(null, null); }
            catch { return null; }
        }

        private static bool TryReadConfigEntryInt(object owner, FieldInfo field, out int value)
        {
            value = 0;
            if (owner == null || field == null)
                return false;

            try
            {
                object entry = field.GetValue(owner);
                if (entry == null)
                    return false;

                PropertyInfo valueProp = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                if (valueProp == null)
                    return false;

                object raw = valueProp.GetValue(entry, null);
                if (raw is int i)
                {
                    value = i;
                    return true;
                }

                if (raw is float f)
                {
                    value = Mathf.RoundToInt(f);
                    return true;
                }

                if (raw is double d)
                {
                    value = Mathf.RoundToInt((float)d);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryReadConfigEntryFloat(object owner, FieldInfo field, out float value)
        {
            value = 0f;
            if (owner == null || field == null)
                return false;

            try
            {
                object entry = field.GetValue(owner);
                if (entry == null)
                    return false;

                PropertyInfo valueProp = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                if (valueProp == null)
                    return false;

                object raw = valueProp.GetValue(entry, null);
                if (raw is float f)
                {
                    value = f;
                    return true;
                }

                if (raw is double d)
                {
                    value = (float)d;
                    return true;
                }

                if (raw is int i)
                {
                    value = i;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetBeatZoneFromBeatSync(out BeatZone zone, out float lowIntensity, out float midIntensity, out float highIntensity)
        {
            zone = BeatZone.Unknown;
            lowIntensity = 0f;
            midIntensity = 0.5f;
            highIntensity = 1f;

            object inst = GetBeatInstance();
            if (inst == null || _beatZoneRawTargetField == null)
                return false;

            float rawTarget;
            try
            {
                object raw = _beatZoneRawTargetField.GetValue(inst);
                if (!(raw is float f) || f < 0f)
                    return false;
                rawTarget = f;
            }
            catch
            {
                return false;
            }

            bool hasLow = TryReadConfigEntryFloat(inst, _beatCfgLowIntensityField, out float low);
            bool hasMid = TryReadConfigEntryFloat(inst, _beatCfgMidIntensityField, out float mid);
            bool hasHigh = TryReadConfigEntryFloat(inst, _beatCfgHighIntensityField, out float high);
            if (!(hasLow && hasMid && hasHigh))
                return false;

            lowIntensity = low;
            midIntensity = mid;
            highIntensity = high;

            float dLow = Mathf.Abs(rawTarget - low);
            float dMid = Mathf.Abs(rawTarget - mid);
            float dHigh = Mathf.Abs(rawTarget - high);
            if (dLow <= dMid && dLow <= dHigh)
                zone = BeatZone.Low;
            else if (dHigh <= dLow && dHigh <= dMid)
                zone = BeatZone.High;
            else
                zone = BeatZone.Mid;

            return true;
        }

        private bool TryGetBeatZoneSpeed01(out float zoneSpeed01)
        {
            zoneSpeed01 = 0f;
            if (!TryGetBeatZoneFromBeatSync(out BeatZone zone, out float lowIntensity, out float midIntensity, out float highIntensity))
                return false;

            float value = zone == BeatZone.Low ? lowIntensity
                : zone == BeatZone.Mid ? midIntensity
                : zone == BeatZone.High ? highIntensity
                : -1f;
            if (value < 0f)
                return false;

            zoneSpeed01 = Mathf.Clamp01(value);
            return true;
        }

        private bool TryGetBeatLoopHz(out float beatLoopHz)
        {
            beatLoopHz = 0f;
            object inst = GetBeatInstance();
            if (inst == null)
                return false;

            if (!TryReadConfigEntryInt(inst, _beatCfgBpmField, out int bpm))
                return false;

            if (!TryGetBeatZoneSpeed01(out float zoneSpeed01))
                return false;

            beatLoopHz = (Mathf.Max(1, bpm) / 60f) * Mathf.Max(0f, zoneSpeed01);
            return beatLoopHz > 0f;
        }

        private float ResolveBeatCaptureFov()
        {
            // Enabling the effect must use the view before the toggle, not a
            // stale bone/preset base. External cinematic FOV remains separate.
            return IsExternalFovHoldActive()
                ? ResolveBeatBaseFov() : ResolveLiveCameraFov(_currentData.Fov);
        }

        private void CaptureBeatFovBase(string reason)
        {
            if (_ctrlCamera == null || IsVrFovBlocked()) return;
            float current = ResolveBeatCaptureFov();
            ResetBeatFovRuntimeState();
            ApplyManualBaseFov(current);
            TraceFovState(reason, true);
        }

        private void RestoreBeatFovBase(string reason)
        {
            if (_ctrlCamera == null) return;
            SetBaseFov(ResolveBeatBaseFov());
            _ctrlCamera.CameraFov = _lastBaseFov;
            ResetBeatFovRuntimeState();
            if (IsExternalFovHoldActive()) RenderCurrentFov(reason);
            else ApplyRawFov(_lastBaseFov, reason, false);
        }

        private void ResetBeatFovRuntimeState()
        {
            _beatStopReturnActive = false;
            _lastBeatLoopActive = false;
            _beatStopReturnStartTime = 0f;
            _beatStopReturnStartMultiplier = 1f;
            _lastBeatCurrentMultiplier = 1f;
            _beatPhaseAccum = 0f;
        }

        private float ResolveBeatCurrentMultiplier()
        {
            if (!IsBeatFovEnabled())
            {
                ResetBeatFovRuntimeState();
                return 1f;
            }

            if (TryGetBeatLoopHz(out float beatLoopHz) && beatLoopHz > 0f)
            {
                float zoomMultiplier = Mathf.Clamp(_cfgBeatFovZoomMultiplier.Value, 1f, 3f);
                // 脈ゾーンに入った瞬間は位相を 0（base）から開始。以降フレーム積算で進めるので、
                // 中→強で hz が変わっても位相は連続（突然ジャンプしない）。
                if (!_lastBeatLoopActive)
                {
                    _beatPhaseAccum = 0f;
                    _beatPhaseFrame = Time.frameCount;
                }
                else if (Time.frameCount != _beatPhaseFrame)
                {
                    // 1フレーム1回だけ進める（同一フレーム内の複数呼び出しで進めすぎない）。
                    _beatPhaseAccum = Mathf.Repeat(_beatPhaseAccum + beatLoopHz * Time.unscaledDeltaTime, 1f);
                    _beatPhaseFrame = Time.frameCount;
                }
                float pulse01 = Mathf.Sin(_beatPhaseAccum * Mathf.PI);
                float currentMultiplier = Mathf.Lerp(1f, zoomMultiplier, pulse01);
                _beatStopReturnActive = false;
                _lastBeatLoopActive = true;
                _lastBeatCurrentMultiplier = Mathf.Max(1f, currentMultiplier);
                return _lastBeatCurrentMultiplier;
            }

            if (_lastBeatLoopActive && !_beatStopReturnActive)
            {
                _beatStopReturnActive = true;
                _beatStopReturnStartTime = Time.unscaledTime;
                _beatStopReturnStartMultiplier = Mathf.Max(1f, _lastBeatCurrentMultiplier);
            }

            _lastBeatLoopActive = false;

            if (_beatStopReturnActive)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - _beatStopReturnStartTime) / 0.5f);
                float currentMultiplier = Mathf.Lerp(_beatStopReturnStartMultiplier, 1f, t);
                _lastBeatCurrentMultiplier = Mathf.Max(1f, currentMultiplier);
                if (t >= 1f)
                    _beatStopReturnActive = false;
                return _lastBeatCurrentMultiplier;
            }

            _lastBeatCurrentMultiplier = 1f;
            return 1f;
        }

        private void ApplyRawFov(float fov, string reason, bool writeLog)
        {
            if (_ctrlCamera == null || IsVrFovBlocked()) return;
            float display = Mathf.Clamp(fov, 5f, 120f);
            _lastAppliedFov = display;
            if (CanWriteFov(_ctrlCamera.thisCmaera)) _ctrlCamera.thisCmaera.fieldOfView = display;
            ApplyFovToScreenCameras(display, reason, writeLog);
        }

        // 外部演出（AstralMode 等）専用。
        // 既存 ApplyRawFov とロジックは同一だが、clamp 範囲を 5..179 に広げる。
        // 通常UI/プリセット/ボーン連動/BeatFov は ApplyRawFov(5..120) を使い続け、
        // ここは外部演出のみが叩く。
        private void ApplyRawFovExternal(float fov, string reason, bool writeLog)
        {
            AcceptExternalDisplayFov(fov, reason);
        }

        // 外部演出から FOV を適用するための公開API。
        // AstralMode 等が反射経由で呼ぶ。
        // 成功時は _lastBaseFov / _lastAppliedFov を同じ clamped 値で更新するので、
        // 直後の EnforceLastAppliedFov("lateupdate-enforce") も同値を維持し戻されない。
        public static bool TryApplyExternalFov(float fov, string reason)
        {
            if (Instance == null) return false;
            if (Instance._ctrlCamera == null) Instance.RefreshCameraControllerCache();
            if (Instance._ctrlCamera == null || Instance.IsVrFovBlocked()) return false;
            Instance.AcceptExternalDisplayFov(fov, reason);
            return true;
        }

        // 毎フレーム呼ばれる外部演出（AstralMode の Drive 系）専用の軽量経路。
        // SetCameraData を呼ばず、FOV 即時反映と _lastBaseFov / _lastAppliedFov の更新だけ行う。
        // SetCameraData が内部状態（位置/回転含む CameraData 全体）を毎フレ再投入することによる
        // カクつきを避けるため。
        public static bool TryApplyExternalFovFast(float fov, string reason)
        {
            if (Instance == null) return false;
            if (Instance._ctrlCamera == null) Instance.RefreshCameraControllerCache();
            if (Instance._ctrlCamera == null || Instance.IsVrFovBlocked()) return false;
            Instance.AcceptExternalDisplayFov(fov, reason);
            return true;
        }

        private float ResolveBeatBaseFov()
        {
            if (_lastBaseFov > 0f) return Mathf.Clamp(_lastBaseFov, 5f, 120f);
            float value = _ctrlCamera != null ? _ctrlCamera.CameraFov : Settings.DefaultFov;
            return Mathf.Clamp(value <= 0f ? Settings.DefaultFov : value, 5f, 120f);
        }

        private float ResolveBeatZoomedFov(float baseFov)
        {
            float clampedBaseFov = Mathf.Clamp(baseFov <= 0f ? Settings.DefaultFov : baseFov, 5f, 120f);
            if (!IsBeatFovEnabled())
                return clampedBaseFov;

            float currentMultiplier = ResolveBeatCurrentMultiplier();
            float zoomedFov = clampedBaseFov / currentMultiplier;

            return Mathf.Clamp(zoomedFov, 5f, 120f);
        }

        private void UpdateBeatFovRuntime()
        {
            if (_ctrlCamera == null || _transitionActive || !IsBeatFovEnabled()
                || IsVrFovBlocked() || _beatFovSuspendedForNewController) return;
            if (!IsExternalFovHoldActive()) _ctrlCamera.CameraFov = ResolveBeatBaseFov();
            RenderCurrentFov("beat-runtime");
        }

        private float GetBoneLinkOffsetAxis(int axis)
        {
            switch (axis)
            {
                case 0:
                    return _activeBoneLinkLookAtOffsetLocal.x;
                case 1:
                    return _activeBoneLinkLookAtOffsetLocal.y;
                case 2:
                    return _activeBoneLinkLookAtOffsetLocal.z;
                default:
                    return 0f;
            }
        }

        private void SetBoneLinkOffsetAxis(int axis, float value)
        {
            // Explicit X/Z editing opts back into the existing bone-local editor.
            // Preserve the current point while changing coordinate space.
            if (_activeLookAtWorldY && (axis == 0 || axis == 2))
            {
                ChaControl target = ResolveMainFemale();
                Transform targetBone = target != null ? ResolveActiveBoneTarget(target) : null;
                if (targetBone == null) return;
                _activeBoneLinkLookAtOffsetLocal = targetBone.InverseTransformPoint(ResolveActiveLookAt(targetBone));
                _activeLookAtWorldY = false;
            }
            switch (axis)
            {
                case 0:
                    _activeBoneLinkLookAtOffsetLocal.x = value;
                    break;
                case 1:
                    _activeBoneLinkLookAtOffsetLocal.y = value;
                    break;
                case 2:
                    _activeBoneLinkLookAtOffsetLocal.z = value;
                    break;
            }

            if (_lookAtAnchor != null)
            {
                ChaControl female = ResolveMainFemale();
                if (female == null)
                    return;

                Transform bone = ResolveActiveBoneTarget(female);
                if (bone != null)
                {
                    _lookAtAnchor.position = ResolveActiveLookAt(bone);
                    _lookAtAnchor.rotation = bone.rotation;
                }
            }
        }

        private string GetGizmoAxisSpaceLabel()
        {
            if (_lookAtAnchorGizmo == null)
                return "未生成";

            return _lookAtAnchorGizmo.AxisSpace == GizmoAxisSpace.Local ? "Local" : "World";
        }

        private Vector3 GetPresetBoneLinkOffsetLocal(CameraPreset preset)
        {
            if (preset == null)
                return Vector3.zero;

            return ToVector3(preset.LookAtOffsetLocal, Vector3.zero);
        }

        private Vector3 GetPresetCameraOffsetLocal(CameraPreset preset)
        {
            if (preset == null)
                return Vector3.zero;

            return ToVector3(preset.CameraOffsetLocal, ToVector3(preset.CameraOffsetWorld, Vector3.zero));
        }

        /// <summary>
        /// 相対方向でカメラ位置が決まるプリセットか。
        /// 「位置を使う」(SaveCameraPosition) とは別系統の位置指定なので、
        /// 位置の有無を見るところは必ずこちらも見る。片方だけ見ると、
        /// 遷移がカメラを開始位置に固定したまま終点だけ相対方向で出す食い違いになり、
        /// 呼んでも3秒動かず、その後で勝手に動き出す二段モーションになる。
        /// </summary>
        private static bool HasRelativeDirection(CameraPreset preset)
        {
            return preset != null
                && preset.UseRelativeDirection
                && preset.RelativeDirLocal != null && preset.RelativeDirLocal.Length >= 3
                && preset.RelativeDistance > 0.0001f;
        }

        private static bool HasPresetCameraPosition(CameraPreset preset)
        {
            if (preset == null)
                return false;

            return preset.SaveCameraPosition
                && ((preset.CameraOffsetBonePos != null && preset.CameraOffsetBonePos.Length >= 3)
                    || (preset.CameraOffsetLocal != null && preset.CameraOffsetLocal.Length >= 3)
                    || (preset.CameraOffsetWorld != null && preset.CameraOffsetWorld.Length >= 3));
        }

        private void SyncLookAtAnchorToActiveBoneLink(bool force)
        {
            if (!_boneLinkActive)
                return;

            EnsureLookAtAnchor();
            if (_lookAtAnchor == null)
                return;

            ChaControl female = ResolveMainFemale();
            if (female == null)
                return;

            Transform bone = ResolveActiveBoneTarget(female);
            if (bone == null)
            {
                InvalidateFemaleCache();
                return;
            }

            bool isDraggingAnchor = !force && _lookAtAnchorGizmo != null && _lookAtAnchorGizmo.IsDragging;
            _lookAtAnchor.rotation = bone.rotation;
            if (isDraggingAnchor)
            {
                _activeLookAtWorldY = false;
                _activeBoneLinkLookAtOffsetLocal = bone.InverseTransformPoint(_lookAtAnchor.position);
            }
            else
                _lookAtAnchor.position = ResolveActiveLookAt(bone);
        }

        private void EnsureLookAtAnchor()
        {
            if (_lookAtAnchorGo != null && _lookAtAnchor != null)
            {
                ApplyLookAtAnchorGizmoState();
                return;
            }

            _lookAtAnchorGo = new GameObject("MainGameCameraControl_LookAtAnchor");
            _lookAtAnchorGo.hideFlags = HideFlags.HideAndDontSave;
            _lookAtAnchor = _lookAtAnchorGo.transform;

            if (TransformGizmoApi.IsAvailable)
            {
                _lookAtAnchorGizmo = TransformGizmoApi.Attach(_lookAtAnchorGo);
                if (_lookAtAnchorGizmo != null)
                {
                    _lookAtAnchorGizmo.SetMode(GizmoMode.Move);
                    _lookAtAnchorGizmo.SetAxisSpace(GizmoAxisSpace.Local);
                }
            }

            ApplyLookAtAnchorGizmoState();
        }

        private void ApplyLookAtAnchorGizmoState()
        {
            if (_lookAtAnchorGizmo == null)
                return;

            _lookAtAnchorGizmo.SetFollowActive(_boneLinkActive);
            _lookAtAnchorGizmo.SetVisible(_boneLinkActive && _gizmoVisible);
            TransformGizmoApi.SetSizeMultiplier(_lookAtAnchorGizmo, Mathf.Clamp(_cfgGizmoSize.Value, 0.2f, 1.5f));
        }

        private void DestroyLookAtAnchor()
        {
            _lookAtAnchorGizmo = null;
            _lookAtAnchor = null;
            if (_lookAtAnchorGo == null)
                return;

            UnityEngine.Object.Destroy(_lookAtAnchorGo);
            _lookAtAnchorGo = null;
        }

        private void LogInfo(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (_cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value)
                return;
            Logger?.LogInfo(message);
            WritePluginLog("INFO", message);
        }

        private void LogWarn(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogWarning(message);
            WritePluginLog("WARN", message);
        }

        /// <summary>
        /// 相対方向の毎フレームログ。「詳細ログ」がONの時だけ、さらに間引いて出す。
        /// 素の LogInfo で出すと切る手段が無く、遷移中は毎フレーム流れてログが埋まる。
        /// </summary>
        private void LogRelDirTrace(string message)
        {
            if (_cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value)
                return;
            if (Time.unscaledTime < _relDirNextTraceLog)
                return;

            _relDirNextTraceLog = Time.unscaledTime + RelDirTraceIntervalSeconds;
            LogDebug(message);
        }

        private void LogDebug(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (_cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value)
                return;
            Logger?.LogInfo("[detail] " + message);
            WritePluginLog("DEBUG", message);
        }

        private void ResetPluginLog()
        {
            if (!_runtimeLoggingEnabled) return;
            try
            {
                string dir = Path.GetDirectoryName(_pluginLogPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(_pluginLogPath, string.Empty, Utf8NoBom);
                WritePluginLog("INFO", "=== session start ===");
            }
            catch
            {
            }
        }

        private void WritePluginLog(string level, string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (string.IsNullOrWhiteSpace(_pluginLogPath))
                return;

            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    + " [" + level + "] " + message + Environment.NewLine;
                lock (FileLogLock)
                    File.AppendAllText(_pluginLogPath, line, Utf8NoBom);
            }
            catch
            {
            }
        }

        private void LogCameraData(string label, BaseCameraControl_Ver2.CameraData data, CameraPreset preset)
        {
            if (_cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value)
                return;

            string presetName = preset != null ? (preset.Name ?? "(null)") : "(none)";
            string boneName = preset != null ? (preset.BoneName ?? "-") : "-";
            string transBaseInfo = _ctrlCamera != null && _ctrlCamera.transBase != null
                ? FormatVector("transBasePos", _ctrlCamera.transBase.position) + " / " + FormatVector("transBaseRot", _ctrlCamera.transBase.rotation.eulerAngles)
                : "transBase:none";

            LogDebug(
                label
                + " preset=" + presetName
                + " bone=" + boneName
                + " " + FormatVector("Pos", data.Pos)
                + " " + FormatVector("Dir", data.Dir)
                + " " + FormatVector("Rot", data.Rot)
                + " Fov=" + data.Fov.ToString("0.##", CultureInfo.InvariantCulture)
                + " camWorld=" + (_ctrlCamera != null ? FormatVector("cam", _ctrlCamera.transform.position) : "cam:none")
                + " " + transBaseInfo);
        }

        private void LogApplyCameraDataTrace(string phase, BaseCameraControl_Ver2.CameraData data, string reason)
        {
            if (_cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value)
                return;

            // 毎フレームの定常 apply（reason 無し＝ボーン連動の追従更新など）は 1 秒に 1 回へ間引く。
            // 明示理由付き（呼び出し/遷移）はそのまま全部出す。
            if (string.IsNullOrEmpty(reason))
            {
                float nowTrace = Time.unscaledTime;
                if (nowTrace - _applyTraceLastLogTime < 1.0f)
                    return;
                _applyTraceLastLogTime = nowTrace;
            }

            BaseCameraControl_Ver2.CameraData liveData = default(BaseCameraControl_Ver2.CameraData);
            bool liveOk = false;
            Vector3 cameraWorld = Vector3.zero;
            bool cameraOk = false;

            if (_ctrlCamera != null)
            {
                try
                {
                    liveData = _ctrlCamera.GetCameraData();
                    liveOk = true;
                }
                catch
                {
                }

                if (_ctrlCamera.thisCmaera != null)
                {
                    cameraWorld = _ctrlCamera.thisCmaera.transform.position;
                    cameraOk = true;
                }
                else if (_ctrlCamera.transform != null)
                {
                    cameraWorld = _ctrlCamera.transform.position;
                    cameraOk = true;
                }
            }

            LogDebug(
                "MGCC_APPLY_CAMERA_DATA " + phase
                + " frame=" + Time.frameCount
                + " reason=" + (string.IsNullOrEmpty(reason) ? "-" : reason)
                + " " + FormatVector("data.Pos", data.Pos)
                + " " + FormatVector("data.Dir", data.Dir)
                + " " + FormatVector("data.Rot", data.Rot)
                + " data.Fov=" + data.Fov.ToString("0.###", CultureInfo.InvariantCulture)
                + " liveOk=" + liveOk
                + " " + (liveOk ? FormatVector("live.Pos", liveData.Pos) : "live.Pos:none")
                + " cameraOk=" + cameraOk
                + " " + (cameraOk ? FormatVector("camera", cameraWorld) : "camera:none")
                + " transitionActive=" + _transitionActive
                + " boneLink=" + _boneLinkActive);
        }

        private IEnumerator TraceFovAfterApply(int serial, string reason, float requestedFov)
        {
            LogFovTrace(serial, "after-apply", reason, requestedFov);
            yield return null;
            LogFovTrace(serial, "after-1f", reason, requestedFov);
            for (int i = 1; i < 5; i++)
                yield return null;
            LogFovTrace(serial, "after-5f", reason, requestedFov);
            for (int i = 5; i < 30; i++)
                yield return null;
            LogFovTrace(serial, "after-30f", reason, requestedFov);
        }

        private void LogFovTrace(int serial, string stage, string reason, float requestedFov)
        {
            if (_cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value)
                return;

            Camera liveCamera = _ctrlCamera != null ? _ctrlCamera.thisCmaera : null;
            Camera mainCamera = Camera.main;
            Camera topScreenCamera = FindTopScreenCamera();
            bool sameMain = liveCamera != null && mainCamera != null && ReferenceEquals(liveCamera, mainCamera);
            bool sameTop = liveCamera != null && topScreenCamera != null && ReferenceEquals(liveCamera, topScreenCamera);
            float dataFov = _ctrlCamera != null ? _ctrlCamera.GetCameraData().Fov : -1f;
            float controlFov = _ctrlCamera != null ? _ctrlCamera.CameraFov : -1f;
            float initFov = _ctrlCamera != null ? _ctrlCamera.CameraInitFov : -1f;

            LogInfo(
                "[fov-trace #" + serial + "]"
                + " stage=" + stage
                + " frame=" + Time.frameCount
                + " reason=" + reason
                + " requested=" + requestedFov.ToString("0.##", CultureInfo.InvariantCulture)
                + " data=" + dataFov.ToString("0.##", CultureInfo.InvariantCulture)
                + " control=" + controlFov.ToString("0.##", CultureInfo.InvariantCulture)
                + " init=" + initFov.ToString("0.##", CultureInfo.InvariantCulture)
                + " live=" + FormatCameraFovDetailed(liveCamera)
                + " main=" + FormatCameraFovDetailed(mainCamera)
                + " topScreen=" + FormatCameraFovDetailed(topScreenCamera)
                + " sameMain=" + sameMain
                + " sameTop=" + sameTop);
            // cameras= dump removed from routine trace (too heavy for normal use, even with detail log)
        }

        private static string FormatCameraFov(Camera camera)
        {
            if (camera == null)
                return "none";

            return (camera.name ?? "(unnamed)")
                + "#" + camera.GetInstanceID()
                + ":" + camera.fieldOfView.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static Camera FindTopScreenCamera()
        {
            Camera[] cameras = Camera.allCameras;
            Camera best = null;
            float bestDepth = float.NegativeInfinity;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || !camera.enabled || camera.targetTexture != null)
                    continue;
                if (best == null || camera.depth >= bestDepth)
                {
                    best = camera;
                    bestDepth = camera.depth;
                }
            }
            return best;
        }

        private static string FormatAllCamerasFov()
        {
            Camera[] cameras = Camera.allCameras;
            if (cameras == null || cameras.Length == 0)
                return "none";

            StringBuilder sb = new StringBuilder(256);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (i > 0)
                    sb.Append(" | ");
                sb.Append(FormatCameraFovDetailed(cameras[i]));
            }
            return sb.ToString();
        }

        private static string FormatCameraFovDetailed(Camera camera)
        {
            if (camera == null)
                return "none";

            return (camera.name ?? "(unnamed)")
                + "#" + camera.GetInstanceID()
                + ":enabled=" + camera.enabled
                + ":active=" + camera.gameObject.activeInHierarchy
                + ":depth=" + camera.depth.ToString("0.##", CultureInfo.InvariantCulture)
                + ":targetTexture=" + (camera.targetTexture != null ? camera.targetTexture.name : "null")
                + ":fov=" + camera.fieldOfView.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void HookCameraPreCull()
        {
            if (_cameraPreCullHooked)
                return;

            Camera.onPreCull += OnCameraPreCull;
            _cameraPreCullHooked = true;
            LogInfo("camera onPreCull hook installed");
        }

        private void OnCameraPreCull(Camera camera)
        {
            if (!BlankRoomFovInputAllowed() || _ctrlCamera == null || camera == null
                || _beatFovSuspendedForNewController || IsVrFovBlocked()) return;
            if (!_cfgApplyFov.Value && !IsBeatFovEnabled() && !IsExternalFovHoldActive()) return;
            if (!camera.enabled || (camera.targetTexture != null && camera != _ctrlCamera.thisCmaera) || !CanWriteFov(camera)) return;
            // Rendering must never change CameraData or the native reset value.
            _lastAppliedFov = ResolveDisplayFov();
            camera.fieldOfView = _lastAppliedFov;
        }

        private static bool HasPoseCameraPosition(CameraPoseOverride pose)
        {
            return pose != null
                && pose.SaveCameraPosition
                && ((pose.CameraOffsetBonePos != null && pose.CameraOffsetBonePos.Length >= 3)
                    || (pose.CameraOffsetLocal != null && pose.CameraOffsetLocal.Length >= 3)
                    || (pose.CameraOffsetWorld != null && pose.CameraOffsetWorld.Length >= 3));
        }

        // 一覧表示名。体位オーバーライドを持つなら [体位N] を出す。
        // [注視]/[位置] タグは「今の体位で実際に読み込まれる値」を反映する（基底だけ見て嘘タグを出さない）。
        private string GetPresetDisplayName(CameraPreset preset, int index)
        {
            string name = string.IsNullOrWhiteSpace(preset?.Name) ? $"Preset {index + 1}" : preset.Name;
            if (preset == null)
                return name;

            // 現在体位に一致するオーバーライドがあれば、タグはその実態を反映する。
            // _lastPoseKey は UpdatePosePresetTracking が毎フレーム更新するキャッシュ（FindObjectOfType を毎描画呼ばない）。
            CameraPoseOverride pose = (preset.UsePoseOverrides && !string.IsNullOrEmpty(_lastPoseKey))
                ? FindCameraPoseOverride(preset, _lastPoseKey)
                : null;

            bool useBone = pose != null ? pose.UseBoneLink : preset.UseBoneLink;
            float[] lookAtOffset = pose != null ? pose.LookAtOffsetLocal : preset.LookAtOffsetLocal;
            bool hasCamPos = pose != null ? HasPoseCameraPosition(pose) : HasPresetCameraPosition(preset);

            string flags = string.Empty;
            int poseCount = preset.PoseOverrides != null ? preset.PoseOverrides.Length : 0;
            if (preset.UsePoseOverrides && poseCount > 0)
                flags += "[体位" + poseCount + "]";

            if (hasCamPos)
                flags += "[位置]";

            if (useBone)
                return "ボーン" + flags + ": " + name;

            float[] targetPos = pose != null ? pose.TargetPosition : preset.TargetPosition;
            float[] camDir = pose != null ? pose.CameraDirection : preset.CameraDirection;
            if (targetPos != null && targetPos.Length >= 3)
                flags += "[注視]";
            if ((camDir != null && camDir.Length >= 3)
                || (preset.LegacyPosition != null && preset.LegacyPosition.Length >= 3))
                flags += "[位置]";
            return flags + name;
        }

        public static bool TryGetUiVisible(out bool visible)
        {
            visible = false;
            if (Instance == null)
                return false;
            visible = Instance._cfgUiVisible != null && Instance._cfgUiVisible.Value;
            return true;
        }

        public static bool TrySetUiVisible(bool visible)
        {
            if (Instance == null || Instance._cfgUiVisible == null)
                return false;
            Instance._cfgUiVisible.Value = visible;
            Plugin.Settings.UiVisible = visible;
            Instance.SaveSettings();
            return true;
        }

        public static bool TryLoadPresetByName(string presetName, out string reason)
        {
            reason = string.Empty;
            if (Instance == null)
            {
                reason = "instance_null";
                return false;
            }

            if (Instance._cfgVoiceFaceEventCameraTriggerEnabled != null && !Instance._cfgVoiceFaceEventCameraTriggerEnabled.Value)
            {
                reason = "voiceface_camera_trigger_disabled";
                Instance.SetStatus("VoiceFaceEventカメラトリガー無効: " + (presetName ?? string.Empty));
                return false;
            }

            try
            {
                bool hadReturnPending = Instance._voiceFaceReturnPending;
                Instance.CaptureVoiceFaceEventReturnState();
                bool ok = Instance.TryLoadPresetByNameInternal(presetName, out reason);
                if (!ok && !hadReturnPending)
                    Instance.ClearVoiceFaceEventReturnState();
                return ok;
            }
            catch (Exception ex)
            {
                Instance.ClearVoiceFaceEventReturnState();
                reason = "exception:" + ex.Message;
                Instance.LogWarn("camera preset load by name failed: " + reason);
                return false;
            }
        }

        public static bool TryLoadPresetByIndex(int index, out string reason)
        {
            reason = string.Empty;
            if (Instance == null)
            {
                reason = "instance_null";
                return false;
            }

            if (Instance._cfgVoiceFaceEventCameraTriggerEnabled != null && !Instance._cfgVoiceFaceEventCameraTriggerEnabled.Value)
            {
                reason = "voiceface_camera_trigger_disabled";
                Instance.SetStatus("VoiceFaceEventカメラトリガー無効: index=" + index);
                return false;
            }

            try
            {
                bool hadReturnPending = Instance._voiceFaceReturnPending;
                Instance.CaptureVoiceFaceEventReturnState();
                bool ok = Instance.TryLoadPresetByIndexInternal(index, out reason);
                if (!ok && !hadReturnPending)
                    Instance.ClearVoiceFaceEventReturnState();
                return ok;
            }
            catch (Exception ex)
            {
                Instance.ClearVoiceFaceEventReturnState();
                reason = "exception:" + ex.Message;
                Instance.LogWarn("camera preset load by index failed: " + reason);
                return false;
            }
        }

        public static bool TryGetPresetNames(out string[] names, out string reason)
        {
            names = new string[0];
            reason = string.Empty;
            if (Instance == null)
            {
                reason = "instance_null";
                return false;
            }

            try
            {
                return Instance.TryGetPresetNamesInternal(out names, out reason);
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                Instance.LogWarn("camera preset names get failed: " + reason);
                names = new string[0];
                return false;
            }
        }

        private void HandleWindowPointer(Event evt)
        {
            if (evt == null)
                return;

            bool insideWindow = _windowRect.Contains(evt.mousePosition);
            _windowPointerActive = insideWindow;

            if (!insideWindow)
                return;

            if (evt.isMouse || evt.type == EventType.ScrollWheel)
                evt.Use();
        }

        private void UpdateWindowDragState()
        {
            bool standaloneVisible = _cfgUiVisible.Value && !IsHostedByUnifiedUi();
            if (!standaloneVisible)
            {
                _windowDragging = false;
                _windowPointerActive = false;
                return;
            }

            Vector2 mouseGui = GetMousePositionInGuiSpace();
            _windowPointerActive = _windowRect.Contains(mouseGui);

            if (Input.GetMouseButtonDown(0) && GetTitleBarRect().Contains(mouseGui))
            {
                _windowDragging = true;
                return;
            }

            if (!Input.GetMouseButton(0))
                _windowDragging = false;
        }

        private Rect GetTitleBarRect()
        {
            return new Rect(_windowRect.x, _windowRect.y, _windowRect.width, WindowDragHandleHeight);
        }

        private static Vector2 GetMousePositionInGuiSpace()
        {
            Vector3 mouse = Input.mousePosition;
            return new Vector2(mouse.x, Screen.height - mouse.y);
        }

        private void UpdateInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            bool standaloneVisible = _cfgUiVisible.Value && !IsHostedByUnifiedUi();
            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, standaloneVisible);
            UiInputCaptureApi.Sync(
                InputOwnerKey,
                InputSourceWindow,
                standaloneVisible && (_windowPointerActive || _windowDragging));
            UiInputCaptureApi.Sync(InputOwnerKey, "bone-look-height-drag", IsBoneLookAtHeightDragActive());
        }

        private void ReleaseInputCapture()
        {
            _boneReleaseMiddleHeld = false;
            _quickBoneSwitchMouseHeld = false;
            if (!UiInputCaptureApi.IsAvailable)
                return;

            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, false);
            UiInputCaptureApi.EndOwner(InputOwnerKey);
            _windowPointerActive = false;
            _windowDragging = false;
        }

        private void StartCameraTransition(
            BaseCameraControl_Ver2.CameraData from,
            BaseCameraControl_Ver2.CameraData to,
            CameraPreset preset,
            bool useDynamicTarget)
        {
            float duration = Mathf.Clamp(_cfgTransitionSeconds.Value, 0f, 3f);
            _transitionManualFov = false;
            string fovTraceReason = _pendingFovTraceReason;
            _pendingFovTraceReason = string.Empty;
            _transitionFovTraceReason = string.Empty;
            _transitionState = null;
            BaseCameraControl_Ver2.CameraData transitionFrom = from;

            // 位置=OFF（保存カメラ位置を使わない）ボーンプリセットは、遷移中もカメラ実位置を
            // 開始位置で固定し、注視点だけ動かす。HasPresetCameraPosition は SaveCameraPosition＋offset有。
            bool holdCamPos = preset != null && preset.UseBoneLink
                && !HasPresetCameraPosition(preset)
                && !HasRelativeDirection(preset);
            _transitionHoldCamWorldActive = holdCamPos;
            if (holdCamPos)
                _transitionHoldCamWorld = GetLiveCameraWorldPosition();

            LogInfo("[transition-hold] hold=" + holdCamPos
                + " preset=" + (preset != null ? (preset.Name ?? "?") : "null")
                + " bone=" + (preset != null && preset.UseBoneLink)
                + " saveCamPos=" + (preset != null && preset.SaveCameraPosition)
                + " hasPresetCamPos=" + (preset != null && HasPresetCameraPosition(preset))
                + " p0=" + _transitionHoldCamWorld.ToString("0.##"));

            LogInfo("[fov-transition-start] applyFov=" + _cfgApplyFov.Value
                + " fromFov=" + transitionFrom.Fov.ToString("0.##", CultureInfo.InvariantCulture)
                + " toFov=" + to.Fov.ToString("0.##", CultureInfo.InvariantCulture)
                + " duration=" + duration.ToString("0.##", CultureInfo.InvariantCulture)
                + " dynamicTarget=" + useDynamicTarget);
            if (duration <= 0.0001f)
            {
                _transitionActive = false;
                if (_transitionHoldCamWorldActive)
                    to = HoldCameraWorldPosition(to);
                _transitionHoldCamWorldActive = false;
                ApplyCameraData(to, fovTraceReason);
                _currentData = to;
                _targetData = to;
                return;
            }

            _transitionFrom = transitionFrom;
            _transitionTo = to;
            _transitionDuration = duration;
            _transitionStartTime = Time.unscaledTime;
            _transitionFovTraceReason = fovTraceReason;
            _lastTransitionFovLogFrame = -1;
            _transitionState = new CameraTransitionState
            {
                From = transitionFrom,
                InitialTarget = to,
                Preset = preset,
                UseDynamicTarget = useDynamicTarget,
                FovTraceReason = fovTraceReason
            };
            _transitionActive = true;
            ApplyCameraData(transitionFrom, fovTraceReason);
        }

        private void UpdateCameraTransition()
        {
            if (!_transitionActive || _ctrlCamera == null)
                return;

            float rawT = Mathf.Clamp01((Time.unscaledTime - _transitionStartTime) / Mathf.Max(0.0001f, _transitionDuration));
            float easedT = EvaluateEasing(rawT, _cfgTransitionEasing.Value);
            BaseCameraControl_Ver2.CameraData liveTarget = ResolveTransitionTargetData();
            _transitionTo = liveTarget;
            if (_transitionState != null && _transitionState.UseDynamicTarget)
                SyncLookAtAnchorToActiveBoneLink(force: false);
            BaseCameraControl_Ver2.CameraData step = LerpCameraData(_transitionFrom, liveTarget, easedT);
            if (_cfgApplyFov.Value)
            {
                step.Fov = _transitionManualFov ? _transitionManualBaseFov : Mathf.Lerp(_transitionFrom.Fov, liveTarget.Fov, easedT);
                if (_boneLinkActive)
                    _activeBoneLinkFov = step.Fov;
            }
            // 位置=OFF：注視点・Rotは補間しつつ、カメラ実位置は「現在位置」に固定（Dirを作り直す）。
            // 毎フレーム現在位置を取り直すので、触らなければ開始位置に留まり、
            // 手で動かせばその新しい位置に追従する（遷移中もカメラを動かせる）。
            if (_transitionHoldCamWorldActive)
            {
                _transitionHoldCamWorld = GetLiveCameraWorldPosition();
                step = HoldCameraWorldPosition(step);
            }
            ApplyCameraData(step);

            _currentData = step;
            _targetData = liveTarget;
            LogFovTransitionStep(rawT, easedT, _transitionFrom.Fov, liveTarget.Fov, step.Fov);

            if (rawT >= 1f)
            {
                _transitionActive = false;
                if (_transitionHoldCamWorldActive)
                {
                    _transitionHoldCamWorld = GetLiveCameraWorldPosition();
                    liveTarget = HoldCameraWorldPosition(liveTarget);
                }
                _currentData = liveTarget;
                _targetData = liveTarget;
                if (_cfgApplyFov.Value && _boneLinkActive)
                    _activeBoneLinkFov = liveTarget.Fov;
                ApplyCameraData(liveTarget, _transitionFovTraceReason);
                LogInfo("[transition-hold-end] active=" + _transitionHoldCamWorldActive
                    + " camWorld=" + GetLiveCameraWorldPosition().ToString("0.##")
                    + " p0=" + _transitionHoldCamWorld.ToString("0.##"));
                _transitionHoldCamWorldActive = false;
                _transitionFovTraceReason = string.Empty;
                _transitionState = null;
            }
        }

        private BaseCameraControl_Ver2.CameraData ResolveTransitionTargetData()
        {
            if (_transitionState == null)
                return _transitionTo;

            BaseCameraControl_Ver2.CameraData fallback = _ctrlCamera != null
                ? _ctrlCamera.GetCameraData()
                : _transitionState.InitialTarget;
            BaseCameraControl_Ver2.CameraData target = _transitionState.InitialTarget;

            if (_transitionState.UseDynamicTarget && _transitionState.Preset != null)
            {
                target = BuildCameraDataFromPreset(_transitionState.Preset, fallback);
            }

            if (_transitionManualFov)
                target.Fov = _transitionManualBaseFov;
            else if (!_cfgApplyFov.Value)
                target.Fov = _transitionState.From.Fov;

            return target;
        }

        private float GetCurrentDataFov()
        {
            if (_ctrlCamera != null)
            {
                BaseCameraControl_Ver2.CameraData data = _ctrlCamera.GetCameraData();
                return Mathf.Clamp(data.Fov <= 0f ? Settings.DefaultFov : data.Fov, 5f, 120f);
            }

            return Mathf.Clamp(_currentData.Fov <= 0f ? Settings.DefaultFov : _currentData.Fov, 5f, 120f);
        }

        private float GetLiveCurrentFov()
        {
            return ResolveLiveCameraFov(GetCurrentDataFov());
        }

        private float GetTargetPreviewFov()
        {
            float source = _lastRequestedPresetFov;
            if (source <= 0f)
                source = _targetData.Fov;

            return Mathf.Clamp(source <= 0f ? Settings.DefaultFov : source, 5f, 120f);
        }

        private void ApplyLiveFovFromUi(float requestedFov)
        {
            if (_ctrlCamera == null) return;
            ApplyManualBaseFov(requestedFov);
        }

        // 拍子ズームON時の「現在FOV」編集用。
        // ApplyLiveFovFromUi はライブ（ズーム後）値を CameraData ごと書き込むため、
        // 拍子ズーム中に呼ぶと「ズーム出力をベースに焼く」自己ループになり FOV が下限へ縮む。
        // ここはベース値だけを更新し、実FOVへの反映は UpdateBeatFovRuntime のズーム計算に任せる。
        private void ApplyBaseFovFromUi(float requestedBaseFov)
        {
            if (_ctrlCamera == null) return;
            ApplyManualBaseFov(requestedBaseFov);
        }

        private static BaseCameraControl_Ver2.CameraData LerpCameraData(
            BaseCameraControl_Ver2.CameraData from,
            BaseCameraControl_Ver2.CameraData to,
            float t)
        {
            BaseCameraControl_Ver2.CameraData data = from;
            data.Pos = Vector3.Lerp(from.Pos, to.Pos, t);
            data.Dir = Vector3.Lerp(from.Dir, to.Dir, t);
            data.Rot = new Vector3(
                Mathf.LerpAngle(from.Rot.x, to.Rot.x, t),
                Mathf.LerpAngle(from.Rot.y, to.Rot.y, t),
                Mathf.LerpAngle(from.Rot.z, to.Rot.z, t));
            data.Fov = Mathf.Lerp(from.Fov, to.Fov, t);
            return data;
        }

        private void LogFovTransitionStep(float rawT, float easedT, float fromFov, float toFov, float stepFov)
        {
            if (_cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value)
                return;

            if (Time.frameCount == _lastTransitionFovLogFrame)
                return;

            bool shouldLog = rawT <= 0.001f || rawT >= 0.999f || Time.frameCount % 15 == 0;
            if (!shouldLog)
                return;

            _lastTransitionFovLogFrame = Time.frameCount;
            LogInfo("[fov-transition-step]"
                + " easing=" + EasingLabels[Mathf.Clamp(_cfgTransitionEasing.Value, 0, EasingLabels.Length - 1)]
                + " rawT=" + rawT.ToString("0.000", CultureInfo.InvariantCulture)
                + " easedT=" + easedT.ToString("0.000", CultureInfo.InvariantCulture)
                + " fromFov=" + fromFov.ToString("0.##", CultureInfo.InvariantCulture)
                + " toFov=" + toFov.ToString("0.##", CultureInfo.InvariantCulture)
                + " stepFov=" + stepFov.ToString("0.##", CultureInfo.InvariantCulture));
        }

        private static float EvaluateEasing(float t, int easing)
        {
            switch (Mathf.Clamp(easing, 0, EasingLabels.Length - 1))
            {
                case 1:
                    return t * t;
                case 2:
                    return 1f - ((1f - t) * (1f - t));
                case 3:
                    return t < 0.5f
                        ? 2f * t * t
                        : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
                default:
                    return t;
            }
        }
    }

    public static class MainGameCameraControlApi
    {
        public static bool TryLoadPresetByName(string presetName, out string reason)
        {
            return Plugin.TryLoadPresetByName(presetName, out reason);
        }

        public static bool TryLoadPresetByIndex(int index, out string reason)
        {
            return Plugin.TryLoadPresetByIndex(index, out reason);
        }

        public static bool TryGetPresetNames(out string[] names, out string reason)
        {
            return Plugin.TryGetPresetNames(out names, out reason);
        }

        public static bool TryGetUiVisible(out bool visible)
        {
            return Plugin.TryGetUiVisible(out visible);
        }

        public static bool TrySetUiVisible(bool visible)
        {
            return Plugin.TrySetUiVisible(visible);
        }
    }
}
