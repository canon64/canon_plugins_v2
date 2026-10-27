
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameVoiceFaceEventBridge
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(MainGameCharacterMorphBridge.Plugin.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    internal sealed partial class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kks.maingame.voicefaceeventbridge";
        public const string PluginName = "MainGameVoiceFaceEventBridge";
        public const string Version = "0.4.5";

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static string PluginDir { get; private set; }
        internal static string LogFilePath { get; private set; }
        internal static PluginSettings Settings { get; private set; }
        internal static HSceneProc CurrentProc { get; private set; }

        private static readonly object FileLogLock = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly FieldInfo LstFemaleField = AccessTools.Field(typeof(HSceneProc), "lstFemale");
        private static readonly FieldInfo FaceField = AccessTools.Field(typeof(HSceneProc), "face");
        private static readonly FieldInfo Face1Field = AccessTools.Field(typeof(HSceneProc), "face1");
        private static readonly FieldInfo LstUseAnimInfoField = AccessTools.Field(typeof(HSceneProc), "lstUseAnimInfo");
        private static readonly FieldInfo LstProcField = AccessTools.Field(typeof(HSceneProc), "lstProc");
        private static readonly FieldInfo SpriteField = AccessTools.Field(typeof(HSceneProc), "sprite");
        private static readonly FieldInfo CategorysField = AccessTools.Field(typeof(HSceneProc), "categorys");
        private static readonly FieldInfo ChangeTaiiField = AccessTools.Field(typeof(HSceneProc), "changeTaii");
        private static readonly FieldInfo BChangePointField = AccessTools.Field(typeof(HSceneProc), "bChangePoint");

        private Harmony _harmony;
        private BridgeApplication _application;
        private Infrastructure.PipeServerHost _pipeServer;
        private Audio.AudioPlaybackService _externalVoicePlayer => _application?.Audio;

        /// <summary>
        /// 外部プラグイン向け: 再生中の女性音声を止める。
        /// SubtitleCore の入力パネルのキャンセルボタンから、DLL参照せずリフレクションで呼ばれる。
        /// 戻り値は止める処理まで到達したか（実際に鳴っていたかは問わない）。
        /// </summary>
        internal static bool StopFemaleVoice(string reason)
        {
            Plugin self = Instance;
            var audio = self?._externalVoicePlayer;
            if (audio == null)
            {
                return false;
            }

            try
            {
                audio.Stop(string.IsNullOrEmpty(reason) ? "external-cancel" : reason);
                return true;
            }
            catch
            {
                return false;
            }
        }
        private External.SubtitleEventClient _subtitleEventClient => _application?.SubtitleEvents;
        private Inbound.InboundCommandQueue _inboundCommandQueue => _application?.InboundQueue;
        private Inbound.InboundCommandDecoder _inboundCommandDecoder => _application?.InboundDecoder;
        private Scheduling.StableActionScheduler _actionScheduler => _application?.ActionScheduler;
        private Scheduling.SequenceSessionCoordinator _sequenceSessions => _application?.SequenceSessions;
        private ResponseText.ResponseTextCommandQueue _responseTextQueue => _application?.ResponseTextQueue;
        private Game.GameVoiceGuardState _voiceGuardState => _application?.VoiceGuardState;
        private float _nextProcProbeTime;

        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgVerboseLog;
        private ConfigEntry<bool> _cfgReceiveTimingEnabled;
        private ConfigEntry<KeyboardShortcut> _cfgReloadKey;
        private ConfigEntry<KeyboardShortcut> _cfgStateDumpKey;
        private ConfigEntry<float> _cfgPlaybackVolume;
        private ConfigEntry<float> _cfgFemalePlaybackVolume;
        private ConfigEntry<float> _cfgExternalPlaybackPitch;

        // ClothesDetection
        private ConfigEntry<string> _cfgTopKeywords;
        private ConfigEntry<string> _cfgBottomKeywords;
        private ConfigEntry<string> _cfgBraKeywords;
        private ConfigEntry<string> _cfgShortsKeywords;
        private ConfigEntry<string> _cfgGlovesKeywords;
        private ConfigEntry<string> _cfgPanthoseKeywords;
        private ConfigEntry<string> _cfgSocksKeywords;
        private ConfigEntry<string> _cfgShoesKeywords;
        private ConfigEntry<string> _cfgGlassesKeywords;
        private ConfigEntry<string> _cfgRemoveKeywords;
        private ConfigEntry<string> _cfgShiftKeywords;
        private ConfigEntry<string> _cfgShiftSecondKeywords;
        private ConfigEntry<string> _cfgPutOnKeywords;
        private ConfigEntry<string> _cfgRemoveAllKeywords;
        private ConfigEntry<string> _cfgPutOnAllKeywords;
        private ConfigEntry<string> _cfgCoordPattern;
        private ConfigEntry<string> _cfgNamedPoseTriggerSuffix;
        private ConfigEntry<string> _cfgCameraTriggerKeywords;
        private ConfigEntry<bool> _cfgEnableVideoPlaybackByResponseText;
        private ConfigEntry<string> _cfgVideoPlaybackTriggerKeywords;
        private ConfigEntry<string> _cfgAstralModeTriggerKeywords;
        private ConfigEntry<string> _cfgRealWorldTriggerKeywords;
        private ConfigEntry<bool> _cfgSequenceSubtitleEnabled;
        private ConfigEntry<string> _cfgSequenceSubtitleHost;
        private ConfigEntry<int> _cfgSequenceSubtitlePort;
        private ConfigEntry<string> _cfgSequenceSubtitleEndpointPath;
        private ConfigEntry<string> _cfgSequenceSubtitleBackend;
        private ConfigEntry<string> _cfgSequenceSubtitleDisplayMode;
        private ConfigEntry<string> _cfgSequenceSubtitleSendMode;
        private ConfigEntry<float> _cfgSequenceSubtitleHoldPaddingSeconds;
        private ConfigEntry<bool> _cfgSequenceSubtitleProgressPrefixEnabled;
        private ConfigEntry<bool> _cfgEnableFacePresetApply;
        private ConfigEntry<string> _cfgFacePresetJsonRelativePath;
        private ConfigEntry<bool> _cfgEnableSubCameraPresetForward;
        private ConfigEntry<bool> _cfgEnableMorphBridgeTriggers;
        private ConfigEntry<string> _cfgMorphBridgeTransformKeywords;
        private ConfigEntry<string> _cfgMorphBridgeResetKeywords;
        private ConfigEntry<bool> _impactCueDiagnosticLog;
        private ConfigEntry<float> _cfgMorphBridgeTransitionSeconds;
        private ConfigEntry<bool> _cfgMorphCardsExpanded;
        private ConfigEntry<bool> _cfgMorphCardList;
        private ConfigEntry<string> _cfgMorphCardKeyword;
        private ConfigEntry<string> _cfgMorphCardPath;
        private ConfigEntry<bool> _cfgMorphCardUpsert;
        private bool _morphCardsExpanded;
        private ConfigEntry<bool> _cfgPoseChangeEnabled;
        private ConfigEntry<bool> _cfgPoseSimpleModeEnabled;
        private ConfigEntry<string> _cfgPoseSimpleModeTriggerKeywords;
        private ConfigEntry<bool> _cfgPoseRulesEnabled;
        private ConfigEntry<bool> _cfgPoseInferRulesEnabled;
        private ConfigEntry<bool> _cfgPoseCategoriesExpanded;
        private ConfigEntry<bool> _cfgPoseRulesExpanded;
        private ConfigEntry<bool> _cfgPoseInferRulesExpanded;
        private readonly Dictionary<string, ConfigEntry<bool>> _cfgPoseCategoryEnabledEntries = new Dictionary<string, ConfigEntry<bool>>(StringComparer.Ordinal);
        private readonly Dictionary<string, ConfigEntry<bool>> _cfgPoseRuleEnabledEntries = new Dictionary<string, ConfigEntry<bool>>(StringComparer.Ordinal);
        private readonly Dictionary<string, ConfigEntry<bool>> _cfgPoseInferRuleEnabledEntries = new Dictionary<string, ConfigEntry<bool>>(StringComparer.Ordinal);
        private readonly Dictionary<ConfigEntryBase, ConfigurationManager.ConfigurationManagerAttributes> _cfgPoseReadonlyAttributes =
            new Dictionary<ConfigEntryBase, ConfigurationManager.ConfigurationManagerAttributes>();
        private readonly Dictionary<string, List<string>> _poseNameAliasesByCanonical =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private bool _suppressConfigChangeEvent;
        private bool _suppressPoseConfigChangeEvent;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);
            LogFilePath = Path.Combine(PluginDir, PluginName + ".log");
            _application = new BridgeApplication(Log, LogWarn, LogError);
            InitializeReceiveTiming();

            Directory.CreateDirectory(PluginDir);
            File.WriteAllText(
                LogFilePath,
                $"[{DateTime.Now:HH:mm:ss}] === {PluginName} {Version} started ==={Environment.NewLine}",
                Utf8NoBom);

            _impactCueDiagnosticLog = Config.Bind("ImpactCue", "DiagnosticLogEnabled", false,
                "喘ぎ声の発火判定と拒否理由を専用ログに記録する。原因調査時のみON。");
            InitializeKaleidoscopeTrigger();

            Settings = SettingsStore.LoadOrCreate(PluginDir, Log, LogWarn, LogError);
            LoadScenarioTextRules();
            BindConfigEntries();
            BindVmdDanceConfig();
            ApplySettingsToConfigEntries(Settings);
            _externalVoicePlayer.SetDialogueVolumeProvider(() => ResolveExternalAudioDefaultVolume(Settings));
            _externalVoicePlayer.InitializeImpactCues(Path.Combine(PluginDir, "ImpactCueAudio"), new Audio.ImpactCueSettings(Config));
            _externalVoicePlayer.InitializeSpatialVoice(new Audio.SpatialVoiceSettings(Config));
            _externalVoicePlayer.SetGameVoiceBusyProbe(IsGameVoicePlayingOrPending);
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnImpactCueSceneChanged;
            SaveConfigFile(reason: "awake");
            LoadPoseScoreRules();
            LoadPoseCategoryEntries();
            _externalVoicePlayer.PlaybackStarted += OnExternalVoicePlaybackStarted;
            _externalVoicePlayer.SequenceFinished += OnExternalVoiceSequenceFinished;
            LogGuardSettings("awake");

            _harmony = new Harmony(GUID);
            _harmony.PatchAll(typeof(Plugin));
            VoiceGuardHooks.Apply(_harmony, Log, LogWarn, LogError);
            if (RuntimeLoggingEnabled)
                FacePresetProbeHooks.Apply(_harmony, Log, LogWarn, LogError);

            StartOrRestartPipeServer(forceRestart: true, reason: "awake");
            StartWatchdog();
            Log("awake complete");
        }

        private void StartWatchdog()
        {
        }

        private void OnDestroy()
        {
            ResetNativeVoiceAdmission();
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnImpactCueSceneChanged;
            _receiveTiming?.Flush();
            _sequenceSessions?.Deactivate();
            ClearDelayedActions("on_destroy");
            StopPipeServer("on_destroy");
            ResetResponseTextQueue("on_destroy");

            try
            {
                _harmony?.UnpatchSelf();
                _harmony = null;
            }
            catch (Exception ex)
            {
                LogWarn("unpatch failed: " + ex.Message);
            }

            if (_externalVoicePlayer != null)
            {
                _externalVoicePlayer.PlaybackStarted -= OnExternalVoicePlaybackStarted;
                _externalVoicePlayer.Dispose();
            }

            RestoreVoiceProcStopIfNeeded();
            _voiceGuardState.Reset();
            _astralMethods.Dispose();
            _application?.Dispose();
            _application = null;

            CurrentProc = null;
            Instance = null;
        }

        private float _updateProbeNext;
        private int _updateLastStep;
        private volatile int _mainThreadFrameCount;
        private float _nextStateDumpAllowedTime;
        private string _lastResponseTraceId = "(none)";
        private string _lastResponsePhase = "idle";
        private float _lastResponseStartedAt = -1f;
        private float _lastResponseElapsedMs = -1f;
        private int _lastResponseTextLength;

        private void Update()
        {
            _receiveTiming?.Tick(_cfgReceiveTimingEnabled != null && _cfgReceiveTimingEnabled.Value, Time.frameCount);
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom)
            {
                if (_externalVoicePlayer != null && _externalVoicePlayer.PriorityAibuCueActive)
                {
                    if (!IsPriorityDeepMotionAllowed())
                        _externalVoicePlayer.CancelImpactCueForMotion();
                    _externalVoicePlayer.Tick();
                    if (!_externalVoicePlayer.PriorityAibuCueActive) RestoreVoiceProcStopIfNeeded();
                    return;
                }
                SetKaleidoscopeThought(false);
                _thoughtMarkedForSession = false;
                if (_externalVoicePlayer != null && _externalVoicePlayer.IsPlaying)
                    _externalVoicePlayer.Stop("left blank room");
                return;
            }
            float now = Time.realtimeSinceStartup;
            bool extPlaying = _externalVoicePlayer != null && _externalVoicePlayer.IsPlaying;
            if (extPlaying && now >= _updateProbeNext)
            {
                _updateProbeNext = now + 2f;
                if (Settings != null && Settings.VerboseLog)
                {
                    Log($"[update-probe] alive t={now:F1} lastStep={_updateLastStep}");
                }
                _updateLastStep = 0;
            }

            if (Settings != null && Settings.EnableCtrlRReload && IsReloadKeyDown())
            {
                ReloadSettings();
            }

            if (RuntimeLoggingEnabled && IsStateDumpKeyDown() && now >= _nextStateDumpAllowedTime)
            {
                _nextStateDumpAllowedTime = now + 0.5f;
                DumpRuntimeState("manual_hotkey");
            }

            _mainThreadFrameCount++;
            if (extPlaying) _updateLastStep = 1;
            bool wasExternalPlaying = _externalVoicePlayer != null && _externalVoicePlayer.IsPlaying;
            TickNativeVoiceStandbys();
            if (_externalVoicePlayer != null && _externalVoicePlayer.ImpactCueActive
                && !(_externalVoicePlayer.PriorityAibuCueActive
                    ? IsPriorityDeepMotionAllowed() : IsImpactCueMotionAllowed()))
                _externalVoicePlayer.CancelImpactCueForMotion();
            _externalVoicePlayer?.Tick();
            if (_externalVoicePlayer != null && _externalVoicePlayer.IsThoughtPlaying)
            {
                _voiceGuardState.ClearBlock();
                RestoreVoiceProcStopIfNeeded();
            }
            if (extPlaying) _updateLastStep = 2;
            bool isExternalPlaying = _externalVoicePlayer != null && _externalVoicePlayer.IsPlaying;
            if (wasExternalPlaying && !isExternalPlaying)
            {
                _voiceGuardState.ClearBlock();
                Log("[guard] external play finished -> clear block and try restore");
                RestoreVoiceProcStopIfNeeded();
            }

            if (!ShouldBlockGameVoiceEvents())
            {
                RestoreVoiceProcStopIfNeeded();
            }
            if (extPlaying) _updateLastStep = 3;

            if (Settings == null || !Settings.Enabled)
            {
                if (_responseTextCoroutine != null || _responseTextQueue.Count > 0)
                {
                    ResetResponseTextQueue("settings_disabled");
                }
                return;
            }

            if (extPlaying) _updateLastStep = 4;
            ProcessDelayedActions();
            if (extPlaying) _updateLastStep = 5;
            DrainIncomingCommands(8);
            PumpResponseTextQueue();
            if (extPlaying) _updateLastStep = 6;
            UpdateFacePresetProbe();
            TickScenarioTextSend(now);
            if (extPlaying) _updateLastStep = 7;
        }

        /// <summary>
        /// 外部プラグイン (PublicApi 経由) からの一時的音量ブースト要求を
        /// ExternalVoicePlayer に転送する。
        /// </summary>
        internal bool RequestExternalVolumeBoost(float peakMultiplier, float attackSec, float holdSec, float releaseSec)
        {
            return RequestExternalVolumeBoost(peakMultiplier, attackSec, holdSec, releaseSec, 0f, PublicApi.EasingShape.CosineInOut);
        }

        private void OnImpactCueSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene next)
        {
            SetKaleidoscopeThought(false);
            ResetNativeVoiceAdmission();
            _externalVoicePlayer?.Stop("scene changed");
        }

        internal void SetImpactCueEnabled(string owner, bool enabled)
            => _externalVoicePlayer?.SetImpactCueEnabled(owner, enabled);

        internal bool RequestImpactCue(string owner)
        {
            if (!BlankRoomGate.IsInBlankRoom || _externalVoicePlayer == null)
            {
                LogImpactCueDiagnostic("request rejected owner=" + owner + " blankRoom=" + BlankRoomGate.IsInBlankRoom + " playerReady=" + (_externalVoicePlayer != null));
                return false;
            }
            if (!IsImpactCueMotionAllowed())
            {
                HSceneProc proc = FindCurrentProc();
                LogImpactCueDiagnostic("request rejected owner=" + owner + " motion=" + (proc?.flags?.nowAnimStateName ?? "<none>"));
                _externalVoicePlayer.CancelImpactCueForMotion();
                return false;
            }
            return _externalVoicePlayer.RequestImpactCue(owner, ResolveFemaleForMain(0));
        }

        internal bool RequestPriorityDeepCue(string owner)
        {
            if (owner != "com.kks.maingame.vrdildo" || _externalVoicePlayer == null ||
                !_externalVoicePlayer.DeepCueReady || !IsPriorityDeepMotionAllowed() ||
                _externalVoicePlayer.SpecialAibuCuePlaying) return false;
            HSceneProc proc = FindCurrentProc();
            ChaControl female = ResolveFemaleForMain(0);
            CancelNativeVoiceLoads(null, null, false);
            TryStopGameVoice(proc, female, 0);
            Manager.Voice.StopAll(false);
            return _externalVoicePlayer.RequestPriorityDeepCue(owner, female);
        }

        internal bool RequestContinuousPistonCue(string owner)
            => RequestPistonCue(owner, false, 1f, 0f);

        internal bool IsHardPistonCuePlaying => _externalVoicePlayer != null && _externalVoicePlayer.HardPistonCuePlaying;
        internal bool IsOgazmCuePlaying => _externalVoicePlayer != null && _externalVoicePlayer.OgazmCuePlaying;

        internal bool RequestOgazmCue(string owner, float volume, float cooldownSeconds)
            => RequestSpecialAibuCue(owner, 2, volume, cooldownSeconds);

        internal bool RequestPistonCue(string owner, bool hard, float volume, float cooldownSeconds)
            => RequestSpecialAibuCue(owner, hard ? 1 : 0, volume, cooldownSeconds);

        private bool RequestSpecialAibuCue(string owner, int kind, float volume, float cooldownSeconds)
        {
            bool ownerValid = owner == "com.kks.maingame.vrdildo";
            bool aibu = IsPriorityDeepMotionAllowed();
            if (!ownerValid || _externalVoicePlayer == null || !aibu)
            {
                LogImpactCueDiagnostic("piston request rejected ownerValid=" + ownerValid +
                    " playerReady=" + (_externalVoicePlayer != null) + " aibu=" + aibu);
                return false;
            }
            bool canStart = kind == 2 ? _externalVoicePlayer.CanStartOgazmCue(owner)
                : kind == 1 ? _externalVoicePlayer.CanStartHardPistonCue(owner)
                : _externalVoicePlayer.CanStartPistonCue(owner);
            if (!canStart) return false;
            HSceneProc proc = FindCurrentProc();
            ChaControl female = ResolveFemaleForMain(0);
            if (female == null)
            {
                LogImpactCueDiagnostic("piston request rejected female=null");
                return false;
            }
            CancelNativeVoiceLoads(null, null, false);
            TryStopGameVoice(proc, female, 0);
            Manager.Voice.StopAll(false);
            string subtitle;
            float duration;
            bool started = kind == 2
                ? _externalVoicePlayer.RequestOgazmCue(owner, female, volume, cooldownSeconds,
                    out subtitle, out duration)
                : kind == 1
                    ? _externalVoicePlayer.RequestHardPistonCue(owner, female, volume, cooldownSeconds,
                        out subtitle, out duration)
                    : _externalVoicePlayer.RequestContinuousPistonCue(owner, female, volume, cooldownSeconds,
                        out subtitle, out duration);
            if (!started)
            {
                LogImpactCueDiagnostic("piston request failed after voice stop");
                return false;
            }
            LogImpactCueDiagnostic("piston request accepted duration=" + duration.ToString("F2"));
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                var options = new External.SubtitleEventClientOptions
                {
                    Host = "localhost", Port = 18766, EndpointPath = "subtitle-event"
                };
                var message = new External.SubtitleEventMessage
                {
                    Text = External.SubtitleEventPayloadBuilder.BuildDisplayText(subtitle, "StackFemale"),
                    Backend = "Auto", DisplayMode = "StackFemale",
                    HoldSeconds = duration
                };
                if (_subtitleEventClient == null || !_subtitleEventClient.TrySendLocal(options, message))
                    Log("[piston-cue] subtitle unavailable; MainGameSubtitleCore must be loaded");
            }
            return true;
        }


        private bool IsPriorityDeepMotionAllowed()
        {
            HSceneProc proc = FindCurrentProc();
            return proc != null && proc.flags != null && proc.flags.mode == HFlag.EMode.aibu;
        }

        private bool IsImpactCueMotionAllowed()
        {
            HSceneProc proc = FindCurrentProc();
            // Exact loop states only: climax, after-climax, idle and unknown states are all excluded.
            return proc != null && proc.flags != null && IsSonyuLoopState(proc.flags.nowAnimStateName);
        }

        internal bool RequestExternalVolumeBoost(
            float peakMultiplier,
            float attackSec,
            float holdSec,
            float releaseSec,
            float silenceSec,
            PublicApi.EasingShape easing)
        {
            return RequestExternalVolumeBoost(
                peakMultiplier,
                attackSec,
                holdSec,
                releaseSec,
                silenceSec,
                0.08f,
                0.08f,
                easing);
        }

        internal bool RequestExternalVolumeBoost(
            float peakMultiplier,
            float attackSec,
            float holdSec,
            float releaseSec,
            float silenceSec,
            float silenceFadeOutSec,
            float silenceFadeInSec,
            PublicApi.EasingShape easing)
        {
            if (_externalVoicePlayer == null) return false;
            return _externalVoicePlayer.RequestTransientBoost(
                peakMultiplier,
                attackSec,
                holdSec,
                releaseSec,
                silenceSec,
                silenceFadeOutSec,
                silenceFadeInSec,
                easing);
        }




    }
}
