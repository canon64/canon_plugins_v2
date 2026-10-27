using Bindings = CanonPlugins.Shared.PluginInput;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameSpeedLimitBreak
{
    
    
    
    public partial class Plugin : MonoBehaviour
    {
        public const string Guid = "com.kks.maingame.speedlimitbreak";
        public const string PluginName = "MainGameSpeedLimitBreak";
        public const string Version = "0.2.0";

        private const string SettingsFileName = "SpeedLimitBreakSettings.json";

        internal static Plugin Instance { get; private set; }
        internal static PluginSettings Settings { get; private set; }

        private string _pluginDir;
        private string _logFilePath;
        private readonly object _logLock = new object();
        private HSceneProc _hSceneProc;
        private float _nextHSceneScanTime;
        private bool _insideHScene;
        private bool _masturbationSourceRangeActive;
        private float _nextVerboseLogTime;
        private float _nextHijackLogTime;
        private float _nextVideoRoomPollTime;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private bool _showBpmUi = false;
        private Rect _windowRect = new Rect(32f, 32f, 520f, 520f);
        private Vector2 _windowScroll;
        private string _appliedBpmMaxInput = "240";
        private string _appliedBpmMinInput = "60";
        private string _bpmMinInput = "0";
        private string _bpmMaxInput = "135.6";
        private string _presetNameInput = "BPM 01";
        private string _uiNotice = "";
        private float _uiNoticeUntil;
        private readonly int _windowId = "MainGameSpeedLimitBreak.BpmUi".GetHashCode();
        private ConfigEntry<float> _cfgBpmReferenceAtSourceMin;
        private ConfigEntry<float> _cfgBpmReferenceAtSourceMax;
        private ConfigEntry<bool> _cfgEnableBpmSpeedRemap;
        private ConfigEntry<bool> _cfgForceVanillaSpeed;
        private ConfigEntry<bool> _cfgEnablePerFrameTrace;
        private ConfigEntry<bool> _cfgEnableMasturbationTransitionTrace;
        private ConfigEntry<float> _cfgMasturbationTraceIntervalSec;
        private ConfigEntry<bool> _cfgShowBpmUi;
        private ConfigEntry<bool> _cfgEnableVideoTimeSpeedCues;
        private ConfigEntry<string> _cfgVideoTimeCueFilePath;
        private bool _suppressConfigSync;
        private bool _calibrationCfgMirrorInitialized;
        private int _lastAppliedPresetOrderIndex = -1;
        private bool _videoRoomPlaybackAvailable;
        private double _videoRoomPlaybackTimeSec;
        private double _videoRoomPlaybackLengthSec;
        private bool _videoRoomPlaybackPrepared;
        private bool _videoRoomPlaybackPlaying;
        private float _nextVideoRoomFetchLogTime;
        private float _nextVideoRoomInvokeErrorLogTime;
        private bool _videoRoomFetchWasAvailable;
        private float _nextTimelineHijackLogTime;
        private readonly HashSet<int> _videoCueTriggeredIndices = new HashSet<int>();
        private readonly HashSet<int> _videoCueTriggeredOnceIndices = new HashSet<int>();
        private double _videoCuePrevTimeSec = -1d;
        private int _videoCueLoopCount;
        private bool _timelineGaugeOverrideEnabled;
        private float _timelineGaugeOverride01 = -1f;
        private bool _timelineGaugeTransitionActive;
        private float _timelineGaugeTransitionStart01 = -1f;
        private float _timelineGaugeTransitionTarget01 = -1f;
        private float _timelineGaugeTransitionStartUnscaledTime = -1f;
        private float _timelineGaugeTransitionDurationSec = 0f;
        private GaugeTransitionEasing _timelineGaugeTransitionEasing = GaugeTransitionEasing.Linear;
        private string _videoCueFileResolvedPath;
        private bool _videoCueResetOnLoop = true;
        private List<VideoTimeSpeedCue> _videoCueTimeline = new List<VideoTimeSpeedCue>();
        private readonly Dictionary<string, float> _diagNextLogTime = new Dictionary<string, float>(StringComparer.Ordinal);

        private const float MasturbationEffectiveSourceMin = 0.35f;
        private const float MasturbationEffectiveSourceMax = 2.2f;

        private void GetEffectiveSourceRange(out float min, out float max)
        {
            var s = Settings;
            if (_masturbationSourceRangeActive)
            {
                min = MasturbationEffectiveSourceMin;
                max = MasturbationEffectiveSourceMax;
            }
            else if (s != null)
            {
                min = s.SourceMinSpeed;
                max = s.SourceMaxSpeed;
            }
            else
            {
                min = 1f;
                max = 3f;
            }
        }

        private static readonly FieldInfo LstFemaleField = AccessTools.Field(typeof(HSceneProc), "lstFemale");

        private enum BpmMeasureTarget
        {
            None = 0,
            Min = 1,
            Max = 2
        }

        private sealed class BpmMeasureRuntime
        {
            public bool Running;
            public BpmMeasureTarget Target;
            public int StateHash;
            public float LastNorm;
            public float LastSampleTime;
            public float AccumNorm;
            public float AccumSec;
        }

        private readonly BpmMeasureRuntime _bpmMeasure = new BpmMeasureRuntime();
        private float _measuredMinBpm = -1f;
        private float _measuredMaxBpm = -1f;

        // ── 外部プラグイン向けの公開 ──────────────────────────────
        // 参照するのは設定に保存された較正値(AppliedBpmMin/Max)。計測ボタンの一時値
        // (_measuredMinBpm/_measuredMaxBpm)ではない。あちらは起動で消えるため、
        // 使うと毎回計測させることになる。
        // HandySync がストローク速度を決めるのに使う。DLL参照はさせずリフレクションで引く。
        internal static float CalibratedBpmMin =>
            Settings != null ? Settings.AppliedBpmMin : -1f;

        internal static float CalibratedBpmMax =>
            Settings != null ? Settings.AppliedBpmMax : -1f;

        /// <summary>
        /// 速度ゲージ(0〜1)から現在のBPMを線形補間で出す。較正が無ければ -1。
        /// ゲージ値の出どころは HFlag.speedCalc で、BeatSync が動いていればその書いた値、
        /// 動いていなければ手動操作の値がそのまま入っている。
        /// </summary>
        internal static float BpmFromGauge(float gauge01)
        {
            var rhythm = MainGameRhythmSync.Plugin.Instance;
            if (rhythm != null && rhythm.IsPhaseSyncActive)
                return rhythm.CurrentCycleBpm;
            float lo = CalibratedBpmMin, hi = CalibratedBpmMax;
            if (hi <= 0f)
                return -1f;
            if (lo < 0f) lo = 0f;
            if (gauge01 < 0f) gauge01 = 0f;
            else if (gauge01 > 1f) gauge01 = 1f;
            return lo + (hi - lo) * gauge01;
        }

        private string SettingsPath => Path.Combine(_pluginDir, SettingsFileName);

        public static void ApplyTapBpm(float bpmMax)
        {
            if (Instance == null || Settings == null) return;
            float bpmMin = bpmMax * 0.25f;
            Instance.ApplyAppliedBpmRange(bpmMin, bpmMax, "tap-tempo", $"タップBPM適用: {bpmMax:0.##}");
        }

        private void Awake()
        {
            Instance = this;

            _pluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            _logFilePath = Path.Combine(_pluginDir, "MainGameSpeedLimitBreak.log");
            Directory.CreateDirectory(_pluginDir);

            if (Host.LoggingEnabled)
                File.WriteAllText(
                    _logFilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === {PluginName} {Version} started ==={Environment.NewLine}",
                    Utf8NoBom);

            Settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
            SetupConfigManagerBindings();
            SyncUiFromSettings();

            LogInfo("speed controller initialized by MainGameRhythmSync");
        }

        private void OnDestroy()
        {
            Instance = null;
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            UpdateBpmMeasure();
            UpdateBpmReferenceMode();

            if (Bindings.Down("MainGameRhythmSync.bpmUi", "S + Alt"))
            {
                ToggleBpmUi("hotkey-update");
            }

            if (Bindings.Down("MainGameRhythmSync.reload", "R + Ctrl"))
            {
                Settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
                ApplyConfigManagerOverrides(saveIfChanged: false, reason: "settings reload");
                ReloadVideoCueTimelineFromConfiguredPath(migrateLegacyIfMissing: false);
                SyncUiFromSettings();
                ResetVideoCueRuntime(clearTriggerOnce: true);
                UpdateBpmReferenceMode(force: true);
                LogInfo("settings reloaded by Ctrl+R");
                LogInfo($"range map src=[{Settings.SourceMinSpeed:0.###}..{Settings.SourceMaxSpeed:0.###}] -> dst=[{Settings.TargetMinSpeed:0.###}..{Settings.TargetMaxSpeed:0.###}]");
            }

            // 字幕の入力欄で打っている最中の Enter は向こうの送信キー。
            if (_showBpmUi && !TextInputGate.IsTextInputActive && Bindings.Down("MainGameRhythmSync.applyBpm", "Return"))
            {
                ApplyMaxBpmFromInput();
            }
            PollVideoRoomPlaybackSnapshot();
            UpdateVideoTimeSpeedCues();

            if (Time.unscaledTime >= _nextHSceneScanTime)
            {
                _nextHSceneScanTime = Time.unscaledTime + 0.5f;
                if (_hSceneProc == null)
                {
                    _hSceneProc = FindObjectOfType<HSceneProc>();
                }
                _insideHScene = _hSceneProc != null;
            }

            if (_hSceneProc == null)
            {
                _insideHScene = false;
            }

            UpdateMasturbationStateTrace();
        }

        private void ToggleBpmUi(string reason)
        {
            SetBpmUiVisibility(!_showBpmUi, reason, saveSettings: true);
        }

        private void SetBpmUiVisibility(bool visible, string reason, bool saveSettings)
        {
            _showBpmUi = visible;
            PushBpmUiVisibilityToConfigEntry(visible);
            var s = Settings;
            if (s != null)
            {
                bool changed = s.ShowBpmUi != visible;
                s.ShowBpmUi = visible;
                if (changed && saveSettings)
                {
                    SaveSettings(reason);
                }
            }

            string state = _showBpmUi ? "ON" : "OFF";
            LogInfo($"BPM UI {state} ({reason})");
            ShowUiNotice("BPM UI " + state);
        }

        private void PushBpmUiVisibilityToConfigEntry(bool visible)
        {
            if (_cfgShowBpmUi == null)
                return;

            if (_cfgShowBpmUi.Value == visible)
                return;

            _suppressConfigSync = true;
            _cfgShowBpmUi.Value = visible;
            _suppressConfigSync = false;
        }

        // ─── ログ ───────────────────────────────────────────────────────────────

        private void LogInfo(string message)
        {
            if (Host == null || !Host.LoggingEnabled) return;
            Logger.LogInfo(message);
            Append("[INFO] " + message);
        }

        private void LogWarn(string message)
        {
            if (Host == null || !Host.LoggingEnabled) return;
            Logger.LogWarning(message);
            Append("[WARN] " + message);
        }

        private void LogError(string message)
        {
            if (Host == null || !Host.LoggingEnabled) return;
            Logger.LogError(message);
            Append("[ERROR] " + message);
        }

        private void Append(string message)
        {
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
                lock (_logLock)
                {
                    File.AppendAllText(_logFilePath, line, Utf8NoBom);
                }
            }
            catch
            {
                // no throw from logging
            }
        }

        private void LogPatchedMethodsSummary()
        {
            try
            {
                var methods = Harmony.GetAllPatchedMethods()
                    .Where(m => Harmony.GetPatchInfo(m)?.Owners?.Contains(Guid) == true)
                    .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                LogInfo($"harmony patch methods count={methods.Count}");
                for (int i = 0; i < methods.Count; i++)
                {
                    LogInfo("[patch-map] " + methods[i]);
                }
            }
            catch (Exception ex)
            {
                LogWarn("failed to dump patch map: " + ex.Message);
            }
        }
    }
}
