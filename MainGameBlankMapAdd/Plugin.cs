using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using MainGameBlankMapAdd.Application;
using MainGameBlankMapAdd.Bootstrap;
using MainGameBlankMapAdd.Diagnostics;
using MainGameBlankMapAdd.Settings;
using MainGameBlankMapAdd.Map;
using MainGameBlankMapAdd.Room;
using MainGameBlankMapAdd.Media;
using MainGameBlankMapAdd.Media.Playlist;
using MainGameBlankMapAdd.Integrations;
using MainGameBlankMapAdd.UI;
using MainGameBlankMapAdd.Compatibility;
using MainGameBlankMapAdd.Media.Video;
using BeatSyncSnapshot = MainGameBlankMapAdd.Integrations.IntegrationRegistry.BeatSyncSnapshot;
using SpeedLimitBreakSnapshot = MainGameBlankMapAdd.Integrations.IntegrationRegistry.SpeedLimitBreakSnapshot;
using MainGameTransformGizmo;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.SceneManagement;

namespace MainGameBlankMapAdd
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency(MainGameTransformGizmo.Plugin.GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MainGameUiInputCapture.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        internal const string GUID = "com.kks.maingameblankmapadd";
        internal const string PluginName = "MainGameBlankMapAdd";
        internal const string Version = "0.1.0";

        private Harmony _harmony;
        private PluginSettings _settings;
        private PluginPaths _paths;
        private PluginLogger _pluginLog;
        private SettingsRepository _settingsRepository;
        private MainThreadDispatcher _mainThreadDispatcher;
        private Action<Exception> _mainThreadDrainErrorHandler;
        private MapCatalogInjector _mapCatalogInjector;
        private MapSessionController _mapSession;
        private IntegrationRegistry _integrations;
        private AstralRoomController _astralController;
        private UiResourceScope _uiResources;
        private readonly VideoPlaybackRuntimeState _videoState = new VideoPlaybackRuntimeState();
        private readonly PlaybackUiState _playbackUi = new PlaybackUiState();
        private string _pluginDir;
        private GameObject _videoRoomRoot;
        private MediaRoomResourceScope _roomResourceScope;
        private Dictionary<string, VideoBinding> _videoBindings => _videoState.Bindings;
        private List<PendingVideoStart> _pendingVideoStarts => _videoState.PendingStarts;
        private VideoPlayer _mainVideoPlayer { get => _videoState.MainPlayer; set => _videoState.MainPlayer = value; }
        private AudioSource _videoRoomAudioSource { get => _videoState.RoomAudioSource; set => _videoState.RoomAudioSource = value; }
        private ChaControl _femaleChara;
        private float _nextFemaleCharaSearchTime;
        private bool _hasFemaleBaseY;
        private float _femaleBaseY;
        private float _nextPositionLogTime = 0f;
        private float _nextVoiceSyncLogTime = 0f;
        private bool _voiceSnapDone = false;
        private float _nextReverbBypassEnforceTime = 0f;
        private bool _editMode = false;
        private TransformGizmo _gizmo;
        private AudioReverbZone _voiceReverbZone;
        private GameObject _reverbZoneObject;
        private float _nextPendingVideoLogTime { get => _videoState.NextPendingLogTime; set => _videoState.NextPendingLogTime = value; }
        private float _nextBridgeSnapshotLogTime { get => _videoState.NextBridgeSnapshotLogTime; set => _videoState.NextBridgeSnapshotLogTime = value; }
        private float _nextBridgeSnapshotMissingLogTime { get => _videoState.NextBridgeSnapshotMissingLogTime; set => _videoState.NextBridgeSnapshotMissingLogTime = value; }
        private bool _playbackSeekDragging { get => _playbackUi.SeekDragging; set => _playbackUi.SeekDragging = value; }
        private bool _playbackVolumeDragging { get => _playbackUi.VolumeDragging; set => _playbackUi.VolumeDragging = value; }
        private bool _playbackGainDragging { get => _playbackUi.GainDragging; set => _playbackUi.GainDragging = value; }
        private bool _reverbDropdownOpen { get => _playbackUi.ReverbDropdownOpen; set => _playbackUi.ReverbDropdownOpen = value; }
        private Vector2 _reverbDropdownScroll { get => _playbackUi.ReverbDropdownScroll; set => _playbackUi.ReverbDropdownScroll = value; }
        private Rect _reverbDropdownButtonRect { get => _playbackUi.ReverbDropdownButtonRect; set => _playbackUi.ReverbDropdownButtonRect = value; }
        private Rect _reverbDropdownListRect { get => _playbackUi.ReverbDropdownListRect; set => _playbackUi.ReverbDropdownListRect = value; }
        private bool _playbackBarHiddenByUser { get => _playbackUi.HiddenByUser; set => _playbackUi.HiddenByUser = value; }
        private bool _playbackBarCtrlHidden;
        private float _playbackSeekNormalized { get => _playbackUi.SeekNormalized; set => _playbackUi.SeekNormalized = value; }
        private float _playbackRoomScale { get => _playbackUi.RoomScale; set => _playbackUi.RoomScale = value; }
        private bool _playbackRoomControlsExpanded { get => _playbackUi.RoomControlsExpanded; set => _playbackUi.RoomControlsExpanded = value; }
        private string _roomScaleInput = "";
        private string _videoGainInput = "";
        private string _beatSyncBpmInput = "";
        private string _beatSyncLowPassInput = "";
        private string _beatSyncLowThresholdInput = "";
        private string _beatSyncHighThresholdInput = "";
        private string _beatSyncLowIntensityInput = "";
        private string _beatSyncMidIntensityInput = "";
        private string _beatSyncHighIntensityInput = "";
        private string _beatSyncSmoothTimeInput = "";
        private string _beatSyncStrongBeatsInput = "";
        private string _beatSyncWeakBeatsInput = "";
        private readonly string[] _roomPosInputs = { "", "", "" };
        private readonly string[] _roomRotInputs = { "", "", "" };
        private bool _folderDropdownOpen { get => _playbackUi.FolderDropdownOpen; set => _playbackUi.FolderDropdownOpen = value; }
        private Vector2 _folderDropdownScroll { get => _playbackUi.FolderDropdownScroll; set => _playbackUi.FolderDropdownScroll = value; }
        private bool _videoDropdownOpen { get => _playbackUi.VideoDropdownOpen; set => _playbackUi.VideoDropdownOpen = value; }
        private Vector2 _videoDropdownScroll { get => _playbackUi.VideoDropdownScroll; set => _playbackUi.VideoDropdownScroll = value; }
        private bool _imageSlideshowFolderDropdownOpen { get => _playbackUi.SlideshowFolderDropdownOpen; set => _playbackUi.SlideshowFolderDropdownOpen = value; }
        private Vector2 _imageSlideshowFolderDropdownScroll { get => _playbackUi.SlideshowFolderDropdownScroll; set => _playbackUi.SlideshowFolderDropdownScroll = value; }
        private string _overlayOpacityInput { get => _playbackUi.OverlayOpacityInput; set => _playbackUi.OverlayOpacityInput = value; }
        private const float PlaybackBarMinHeightCollapsedPx = 52f;
        private const float PlaybackBarMinHeightExpandedPx = 530f;

        // フォルダ再生
        private static readonly string[] VideoExtensions =
            { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v", ".ts" };
        private FolderPlaylist _folderPlaylist;
        internal string[] FolderFiles => _folderPlaylist?.Files ?? Array.Empty<string>();
        internal int FolderIndex
        {
            get => _folderPlaylist?.Index ?? -1;
            set { if (_folderPlaylist != null) _folderPlaylist.Index = value; }
        }
        internal RoomLayoutProfile FolderBaselineProfile = null;
        private RoomLayoutProfileRepository _roomLayoutProfiles;

        // フォルダ切り替えフェード（IMGUI黒オーバーレイ）
        private enum FadePhase { None, ToBlack, FromBlack }
        private FadePhase _fadePhase = FadePhase.None;
        private float _fadeElapsed;
        private float _fadePhaseDuration;
        private string _fadePendingPath; // ToBlack完了時にURL差し替えするパス

        private void Awake()
        {
            _paths = PluginPaths.Create(Info.Location, Paths.PluginPath);
            _pluginDir = _paths.PluginDirectory;
            // Do not create or truncate a diagnostic log during normal play.
            _pluginLog = null;

            _settingsRepository = new SettingsRepository(_paths, LogInfo, LogWarn, LogError);
            _mainThreadDispatcher = new MainThreadDispatcher();
            _settings = _settingsRepository.LoadCurrent();
            _mapCatalogInjector = new MapCatalogInjector(LogInfo, LogWarn);
            _mapSession = new MapSessionController(
                new SourceMapBlankifier(LogInfo),
                EnsureVideoRoom,
                DestroyVideoRoom);
            _folderPlaylist = new FolderPlaylist(VideoExtensions);
            _integrations = new IntegrationRegistry(LogInfo, LogWarn);
            _astralController = new AstralRoomController(
                new CoroutineHost(this),
                ClearPendingVideoStarts,
                LogWarn);
            _uiResources = new UiResourceScope();
            SceneManager.sceneLoaded += OnRoomOverlaySceneLoaded;
            InitializeUiInputCapture();
            InitializeWebCamSessions();
            _roomLayoutProfiles = RoomLayoutProfileStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
            SetupConfigEntries();
            InitializeStreetlightRoadConfig();
            InitializeVamHotelConfig();
            InitializeMapSkyLibrary();

            OnVideoEnded += OnFolderVideoEnded;
            StartHttpServer();

            _harmony = new Harmony(GUID);
            _harmony.PatchAll(typeof(Plugin));
            LogAvailableWebCamDevices();

            LogInfo(
                $"settings loaded mapNo={_settings.AddedMapNo} source={_settings.SourceMapNo} " +
                $"blankify={_settings.BlankifySceneOnLoad}");
            LogInfo("[slideshow] renderer build=opacity-shader-v2");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnRoomOverlaySceneLoaded;
            DisposeRoomOverlay();
            if (_poseAllRoomSprite != null) Destroy(_poseAllRoomSprite);
            if (_poseAllRoomThumbnail != null) Destroy(_poseAllRoomThumbnail);
            OnVideoEnded -= OnFolderVideoEnded;
            StopHttpServer();
            _uiInputCapture?.Dispose();
            _uiResources?.Dispose();
            _astralController?.Dispose();
            try
            {
                DestroyVideoRoom();
                _harmony?.UnpatchSelf();
            }
            catch (Exception ex)
            {
                LogWarn($"unpatch failed: {ex.Message}");
            }
            finally
            {
                _pluginLog?.Dispose();
                _pluginLog = null;
                _mainThreadDispatcher?.Dispose();
                _mainThreadDispatcher = null;
            }
        }

        private void LogMainThreadDrainError(Exception ex)
        {
            LogWarn("command error: " + ex.Message);
        }

        private void Update()
        {
            UpdateImpactBoost();
            UpdateBlankRoomGate("update");
            TickRoomOverlay();
            ApplyConfigChangesIfNeeded();
            TryStartPendingVideosIfRoomReady();
            TickDeferredPlayFromBar();

            // HTTPコマンドキューをメインスレッドで処理
            if (_mainThreadDrainErrorHandler == null)
                _mainThreadDrainErrorHandler = LogMainThreadDrainError;
            _mainThreadDispatcher?.Drain(_mainThreadDrainErrorHandler);

            // フォルダフェード更新
            if (_fadePhase != FadePhase.None) UpdateFolderFade();
            MaintainAstralRoomOffset();
            if (CanonPlugins.Shared.PluginInput.Down("MainGameBlankMapAdd.bar", "LeftControl"))
            {
                _playbackBarCtrlHidden = !_playbackBarCtrlHidden;
                _playbackBarHiddenByUser = _playbackBarCtrlHidden;
                _playbackBarHideLatchActive = false;
                if (_playbackBarCtrlHidden)
                {
                    _playbackSeekDragging = false;
                    if (_playbackVolumeDragging && !_vmdBarMode) CommitPlaybackBarVolume();
                    if (_playbackGainDragging) CommitPlaybackBarVideoGain();
                    _playbackVolumeDragging = false;
                    _playbackGainDragging = false;
                    _vmdGroupOpen = _vmdEntryOpen = _folderDropdownOpen = _videoDropdownOpen = _reverbDropdownOpen = false;
                    HideUiBlocker();
                }
            }

            // Ctrl+P: toggle video playback.
            if (CanonPlugins.Shared.PluginInput.Down("MainGameBlankMapAdd.play", "P + Ctrl"))
            {
                if (_mainVideoPlayer != null)
                {
                    if (_mainVideoPlayer.isPlaying)
                    {
                        _mainVideoPlayer.Pause();
                        LogInfo("video paused");
                    }
                    else
                    {
                        if (IsAstralModeSuppressed)
                        {
                            ForceHoldVideoAtStart(_mainVideoPlayer, _mainVideoPlayer.url, "astral-suppressed-hotkey");
                            LogInfo("video resume blocked by AstralMode");
                        }
                        else
                        {
                            if (!ExternalMediaBlocksPlayback) _mainVideoPlayer.Play();
                            LogInfo("video resumed");
                        }
                    }
                }
            }

            // Ctrl+R: reload JSON settings.
            if (CanonPlugins.Shared.PluginInput.Down("MainGameBlankMapAdd.reload", "R + Ctrl"))
            {
                ReloadSettingsAndApply();
            }

            // Ctrl+D: toggle gizmo edit mode.
            if (CanonPlugins.Shared.PluginInput.Down("MainGameBlankMapAdd.edit", "D + Ctrl"))
            {
                if (_videoRoomRoot != null)
                {
                    _editMode = !_editMode;
                    _gizmo?.SetVisible(_editMode);
                    LogInfo($"gizmo edit mode: {(_editMode ? "ON" : "OFF")}");
                }
            }

            UpdateUiCaptureState();
            TryLogAudioDiagnosticsTick();
            UpdateWebCamTextures();
            UpdateImageSlideshow();
            UpdateSubCameraOverlay();
            if (_videoRoomRoot == null) return;
            UpdateSolidRoomColorMode();

            EnsureFemaleCharaRef();

            // Edit mode: mouse wheel rotates room around selected axis.
            if (_editMode)
            {
                Vector3 axis = Vector3.right;
                float scroll = CanonPlugins.Shared.PluginInput.Axis("MainGameBlankMapAdd.pitch", "Wheel + Shift");
                if (Mathf.Abs(scroll) <= 0.001f)
                {
                    axis = Vector3.forward;
                    scroll = CanonPlugins.Shared.PluginInput.Axis("MainGameBlankMapAdd.roll", "Wheel + Alt");
                }
                if (Mathf.Abs(scroll) <= 0.001f)
                {
                    axis = Vector3.up;
                    scroll = CanonPlugins.Shared.PluginInput.Axis("MainGameBlankMapAdd.yaw", "Wheel");
                }
                if (Mathf.Abs(scroll) > 0.001f)
                {
                    float deg = scroll * 900f; // one notch (~0.1) => ~90 deg
                    _videoRoomRoot.transform.Rotate(axis, deg, Space.World);
                }
            }

            // Follow female in non-edit mode.
            // アストラルモードで床を下げている間は追従しない。追従すると毎フレーム
            // 元のY(=baseY+yOffset)に上書きしてしまい、MaintainAstralRoomOffset の
            // -100m オフセットと綱引きになって床が下がらなくなる。
            // この間は MaintainAstralRoomOffset(line 196) が target に固定する。
            if (!_editMode && _femaleChara != null && !(IsAstralModeSuppressed && IsAstralRoomMoved))
            {
                var fp = _femaleChara.transform.position;
                float baseY = _hasFemaleBaseY ? _femaleBaseY : fp.y;
                float yOffset = _settings.VideoRoomOffsetY;
                Vector3 roomPosition = new Vector3(
                    fp.x + _settings.VideoRoomOffsetX,
                    baseY + yOffset,
                    fp.z + _settings.VideoRoomOffsetZ);
                if ((_videoRoomRoot.transform.position - roomPosition).sqrMagnitude > 0.00000001f)
                    _videoRoomRoot.transform.position = roomPosition;
            }

            // Reverb origin should follow female position.
            SyncReverbZoneToFemale(false);
            // Video audio origin should follow female position in world space.
            SyncVideoAudioSourceToFemale(false);
            EnforceReverbBypassWhileDisabled();
            // Position log every 5 seconds (VerboseLog only).
            if (!_settings.VerboseLog) return;
            if (Time.unscaledTime < _nextPositionLogTime) return;
            _nextPositionLogTime = Time.unscaledTime + 5f;

            if (_femaleChara != null)
            {
                LogInfo(
                    $"[pos] female pos={_femaleChara.transform.position} " +
                    $"rot={_femaleChara.transform.eulerAngles}");
            }

            LogInfo(
                $"[pos] video room pos={_videoRoomRoot.transform.position} " +
                $"rot={_videoRoomRoot.transform.eulerAngles}");
            if (_videoRoomAudioSource != null)
            {
                LogInfo($"[pos] video audio pos={_videoRoomAudioSource.transform.position}");
            }
        }

        // ── フォルダ再生 ────────────────────────────────────────────

        internal void ScanFolderIfNeeded()
        {
            string folder = _settings?.FolderPlayPath ?? string.Empty;
            string sortMode = _settings?.FolderPlaySortMode ?? "Name";
            bool ascending = _settings?.FolderPlaySortAscending ?? true;
            if (!_folderPlaylist.ScanIfChanged(folder, sortMode, ascending, out string scanError)) return;
            if (!string.IsNullOrWhiteSpace(scanError))
            {
                LogInfo($"folder play: {scanError} path={folder}");
                return;
            }
            // フォルダ切り替え時点の全設定をベースラインとして保存（動画個別設定なし時の戻し先）
            FolderBaselineProfile = CaptureCurrentRoomLayoutProfile(includeAudioGain: true, markAsRoomLayout: true);
            if (TryGetBeatSyncSnapshot(out BeatSyncSnapshot baselineBeat))
            {
                FolderBaselineProfile.HasBeatSync = true;
                FolderBaselineProfile.BeatEnabled = baselineBeat.Enabled;
                FolderBaselineProfile.BeatBpm = baselineBeat.Bpm;
                FolderBaselineProfile.BeatAutoMotionSwitch = baselineBeat.AutoMotionSwitch;
                FolderBaselineProfile.BeatAutoThreshold = baselineBeat.AutoThreshold;
                FolderBaselineProfile.BeatLowThreshold = baselineBeat.LowThreshold;
                FolderBaselineProfile.BeatHighThreshold = baselineBeat.HighThreshold;
                FolderBaselineProfile.BeatLowIntensity = baselineBeat.LowIntensity;
                FolderBaselineProfile.BeatMidIntensity = baselineBeat.MidIntensity;
                FolderBaselineProfile.BeatHighIntensity = baselineBeat.HighIntensity;
                FolderBaselineProfile.BeatSmoothTime = baselineBeat.SmoothTime;
                FolderBaselineProfile.BeatStrongMotionBeats = baselineBeat.StrongMotionBeats;
                FolderBaselineProfile.BeatWeakMotionBeats = baselineBeat.WeakMotionBeats;
                FolderBaselineProfile.BeatLowPassHz = baselineBeat.LowPassHz;
                FolderBaselineProfile.BeatVerboseLog = baselineBeat.VerboseLog;
            }
            if (TryGetSpeedLimitBreakSnapshot(out SpeedLimitBreakSnapshot baselineSlb))
            {
                FolderBaselineProfile.HasSpeedLimitBreak = true;
                FolderBaselineProfile.SpeedForceVanilla = baselineSlb.ForceVanillaSpeed;
                FolderBaselineProfile.SpeedEnableVideoTimeSpeedCues = baselineSlb.EnableVideoTimeSpeedCues;
                FolderBaselineProfile.SpeedAppliedBpmMax = baselineSlb.AppliedBpmMax;
            }
            LogInfo($"folder play: scanned path={folder} count={FolderFiles.Length}");
        }

        internal void ForceFolderRescan()
        {
            _folderPlaylist.Invalidate();
            ScanFolderIfNeeded();
            if (_settings != null && _settings.FolderPlayEnabled)
                ApplyFolderVideoPath();
        }

        internal void ApplyFolderVideoPath()
        {
            ScanFolderIfNeeded();
            if (FolderIndex >= 0 && FolderIndex < FolderFiles.Length && !IsWebCamUrl(_settings.VideoPath))
                _settings.VideoPath = FolderFiles[FolderIndex];

            TryApplySavedRoomLayoutForCurrentSelection("apply-folder-video-path");
        }

        internal void PlayFolderEntry(int index)
        {
            if (!_folderPlaylist.TrySelect(index, out string newPath)) return;
            _settings.VideoPath = newPath;
            TryApplySavedRoomLayoutForCurrentSelection("play-folder-entry", newPath);
            LogInfo($"folder play: [{FolderIndex + 1}/{FolderFiles.Length}] {newPath}");

            // VideoRoomとメインPlayerが存在する場合はURL差し替え（Destroy不要）
            if (_videoRoomRoot != null && _mainVideoPlayer != null && !string.IsNullOrEmpty(newPath))
            {
                StartFolderVideoSwap(newPath);
                return;
            }

            // フォールバック: 従来のDestroy→再構築
            if (LastReservedMap == null || LastReservedMap.mapRoot == null) return;
            ForceReapplyCurrentMap();
        }

        private void StartFolderVideoSwap(string newPath)
        {
            string resolvedPath = ResolveVideoPath(newPath);
            if (string.IsNullOrEmpty(resolvedPath))
            {
                LogWarn($"folder swap: path unresolved path={newPath}, falling back to rebuild");
                if (LastReservedMap == null || LastReservedMap.mapRoot == null) return;
                ForceReapplyCurrentMap();
                return;
            }

            float fadeDur = _settings?.FolderFadeDuration ?? 1.0f;
            if (fadeDur > 0.01f && _fadePhase == FadePhase.None)
            {
                // フェード開始: まず音量を下げながら黒くする
                // URL差し替えはToBlack完了後に行う
                _fadePendingPath = resolvedPath;
                _fadePhaseDuration = fadeDur * 0.5f;
                _fadeElapsed = 0f;
                _fadePhase = FadePhase.ToBlack;
                ApplyRuntimeVideoAudioLevel(0f);
                LogInfo($"fade to black started duration={fadeDur:F2} path={resolvedPath}");
            }
            else
            {
                SwapVideoUrl(resolvedPath);
            }
        }

        // ── URL差し替え（フェードなし・フェード完了時共用） ─────────────
        private void SwapVideoUrl(string resolvedPath)
        {
            var player = _mainVideoPlayer;
            if (player == null) return;

            // 現在のbindingを特定する
            VideoBinding binding = null;
            string oldKey = null;
            foreach (var kv in _videoBindings)
            {
                if (kv.Value?.Player == player)
                {
                    binding = kv.Value;
                    oldKey = kv.Key;
                    break;
                }
            }
            if (binding == null) return;

            // 古いイベントハンドラを解除
            if (binding.PreparedHandler != null) player.prepareCompleted -= binding.PreparedHandler;
            if (binding.LoopPointHandler != null) player.loopPointReached -= binding.LoopPointHandler;

            // 新しいイベントハンドラを登録
            string capturedPath = resolvedPath;
            binding.PreparedHandler = _ =>
            {
                ApplySquareCropToBinding(binding);
                LogInfo($"folder swap prepared: {capturedPath}");
                FireOnVideoLoaded(capturedPath);
            };
            binding.LoopPointHandler = _ => FireOnVideoEnded(capturedPath);
            player.prepareCompleted += binding.PreparedHandler;
            player.loopPointReached += binding.LoopPointHandler;

            // bindingのキーを更新
            if (oldKey != null && oldKey != resolvedPath)
            {
                _videoBindings.Remove(oldKey);
                _videoBindings[resolvedPath] = binding;
            }

            // URL差し替えてPrepare
            player.Stop();
            player.url = resolvedPath;
            player.isLooping = false;
            ConfigureVideoAudio(player, true);
            player.Prepare();
            QueueVideoPlaybackStart(player, "VideoSurface", resolvedPath);
            LogInfo($"folder swap: URL replaced path={resolvedPath}");
        }

        // ── IMGUIフェード更新 ─────────────────────────────────────────────
        private void UpdateFolderFade()
        {
            _fadeElapsed += Time.unscaledDeltaTime;

            if (_fadePhase == FadePhase.ToBlack)
            {
                if (_fadeElapsed >= _fadePhaseDuration)
                {
                    // 完全に黒くなった: URL差し替え実行
                    if (!string.IsNullOrEmpty(_fadePendingPath))
                    {
                        SwapVideoUrl(_fadePendingPath);
                        _fadePendingPath = null;
                    }
                    _fadeElapsed = 0f;
                    _fadePhase = FadePhase.FromBlack;
                }
            }
            else if (_fadePhase == FadePhase.FromBlack)
            {
                if (_fadeElapsed >= _fadePhaseDuration)
                {
                    // フェード完了: 音量を戻す
                    ApplyRuntimeVideoAudioLevel(_settings?.VideoVolume ?? 0.5f);
                    _fadePhase = FadePhase.None;
                    LogInfo("fade complete");
                }
            }
        }

        private void OnFolderVideoEnded(string path)
        {
            if (!(_settings?.FolderPlayEnabled ?? false)) return;
            if (FolderFiles.Length == 0) return;

            if (_settings.FolderPlaySingleLoop)
            {
                int current = FolderIndex;
                if (current < 0 || current >= FolderFiles.Length) current = 0;
                PlayFolderEntry(current);
                return;
            }

            int next = FolderIndex + 1;
            if (next >= FolderFiles.Length)
            {
                if (_settings.FolderPlayLoop) next = 0;
                else return;
            }
            PlayFolderEntry(next);
        }

        // ── OnGUI ──────────────────────────────────────────────────

        // Playback bar implementation moved to Plugin.PlaybackBar.cs


        private string SaveCurrentStateSnapshot(string trigger)
        {
            if (_videoRoomRoot == null)
            {
                LogWarn($"save ignored trigger={trigger}: video room is null");
                return null;
            }

            // Pull latest values from ConfigManager before snapshot so typed VideoPath is included.
            ApplyConfigToSettings(reason: $"save-prep:{trigger}");
            EnsureFemaleCharaRef();

            if (_femaleChara != null)
            {
                var off = _videoRoomRoot.transform.position - _femaleChara.transform.position;
                _settings.VideoRoomOffsetX = off.x;
                float baseY = _hasFemaleBaseY ? _femaleBaseY : _femaleChara.transform.position.y;
                _settings.VideoRoomOffsetY = _videoRoomRoot.transform.position.y - baseY;
                _settings.VideoRoomOffsetZ = off.z;
            }
            else
            {
                LogWarn($"save trigger={trigger}: female reference is null, keep previous offset");
            }

            var euler = _videoRoomRoot.transform.eulerAngles;
            _settings.VideoRoomRotationX = euler.x;
            _settings.VideoRoomRotationY = euler.y;
            _settings.VideoRoomRotationZ = euler.z;

            _settingsRepository.SaveCurrent(_settings);
            FireOnSettingsApplied();
            string presetSnapshot = SaveCurrentSettingsPresetSnapshot();
            RefreshPresetNameList(presetSnapshot);
            SyncConfigEntriesFromSettings();

            LogInfo(
                $"saved trigger={trigger} video={_settings.VideoPath} " +
                $"offset=({_settings.VideoRoomOffsetX:F3},{_settings.VideoRoomOffsetY:F3},{_settings.VideoRoomOffsetZ:F3}) " +
                $"rot=({euler.x:F1},{euler.y:F1},{euler.z:F1})");

            if (_editMode)
            {
                _editMode = false;
                _gizmo?.SetVisible(false);
            }

            return presetSnapshot;
        }

        private string SaveCurrentSettingsPresetSnapshot()
        {
            try
            {
                string presetDir = Path.Combine(_pluginDir, "MapAddPresets");
                Directory.CreateDirectory(presetDir);

                string videoFileBase = GetCurrentVideoFileNameForPreset();
                string presetName = $"{videoFileBase}_{DateTime.Now:yyyyMMdd_HHmmss_fff}";
                string presetPath = Path.Combine(presetDir, presetName + ".json");
                _settingsRepository.SaveToPath(presetPath, _settings);
                LogInfo($"preset snapshot saved name={presetName} path={presetPath}");
                return presetName;
            }
            catch (Exception ex)
            {
                LogWarn($"preset snapshot save failed: {ex.Message}");
                return null;
            }
        }

        private string GetCurrentVideoFileNameForPreset()
        {
            string path = _mainVideoPlayer?.url;
            if (string.IsNullOrWhiteSpace(path))
                path = _settings?.VideoPath;
            if (string.IsNullOrWhiteSpace(path))
                return "preset";

            string normalizedPath = NormalizeVideoPathInput(path);
            string fileName = Path.GetFileNameWithoutExtension(normalizedPath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "preset";

            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
                fileName = fileName.Replace(invalid[i], '_');

            return fileName;
        }

        private static string NormalizeVideoPathInput(string value)
        {
            return VideoPathRules.Normalize(value);
        }

        private void LogCore(string level, string msg)
        {
            // File and BepInEx logging are disabled for normal play.
        }

        private void LogInfo(string msg) => LogCore("INFO", msg);
        private void LogWarn(string msg) => LogCore("WARN", msg);
        private void LogError(string msg) => LogCore("ERROR", msg);

        public static bool TryGetMainVideoPlaybackSnapshot(
            out double timeSec,
            out double lengthSec,
            out bool isPrepared,
            out bool isPlaying)
        {
            timeSec = 0d;
            lengthSec = 0d;
            isPrepared = false;
            isPlaying = false;

            Plugin inst = PluginCompatibilityResolver.ResolveLoadedPlugin();
            if (inst == null)
                return false;

            var player = inst._mainVideoPlayer;
            if (player == null)
            {
                if (Time.unscaledTime >= inst._nextBridgeSnapshotMissingLogTime)
                {
                    inst._nextBridgeSnapshotMissingLogTime = Time.unscaledTime + 2f;
                    inst.LogInfo("[bridge] snapshot requested but main video player is null");
                }
                return false;
            }

            isPrepared = player.isPrepared;
            isPlaying = player.isPlaying;
            timeSec = player.time;
            lengthSec = player.length;

            if (inst._settings.VerboseLog && Time.unscaledTime >= inst._nextBridgeSnapshotLogTime)
            {
                inst._nextBridgeSnapshotLogTime = Time.unscaledTime + 2f;
                inst.LogInfo(
                    $"[bridge] snapshot exported prepared={isPrepared} playing={isPlaying} " +
                    $"time={timeSec:F3} length={lengthSec:F3}");
            }

            return true;
        }

    }
}
