using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameBeatSyncSpeed
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public partial class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string Guid    = "com.kks.maingame.beatsyncseed";
        public const string PluginName = "MainGameBeatSyncSpeed";
        public const string Version = "0.1.0";

        internal static Plugin Instance { get; private set; }

        // ── Config ──────────────────────────────────────────────
        private ConfigEntry<bool>             _cfgEnabled;
        private ConfigEntry<KeyboardShortcut> _cfgToggleKey;
        private ConfigEntry<string>           _cfgWavPath;
        private ConfigEntry<int>              _cfgBpm;
        private ConfigEntry<float>            _cfgLowPassHz;
        private ConfigEntry<bool>             _cfgAutoThreshold;
        private ConfigEntry<float>            _cfgLowThreshold;
        private ConfigEntry<float>            _cfgHighThreshold;
        private ConfigEntry<float>            _cfgLowIntensity;
        private ConfigEntry<float>            _cfgMidIntensity;
        private ConfigEntry<float>            _cfgHighIntensity;
        private ConfigEntry<float>            _cfgSmoothTime;
        private ConfigEntry<bool>             _cfgAutoMotionSwitch;
        private ConfigEntry<float>            _cfgStrongMotionBeats;
        private ConfigEntry<float>            _cfgWeakMotionBeats;
        private ConfigEntry<bool>             _cfgVerboseLog;

        // ── State ────────────────────────────────────────────────
        private Harmony   _harmony;
        private string    _pluginDir;
        private string    _logFilePath;
        private readonly object _logLock = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private HSceneProc _hSceneProc;
        private bool       _insideHScene;
        private float      _nextHSceneScanTime;

        private float[] _beatIntensities;
        private float   _beatDurationSec;
        private bool    _analysisReady;
        private string  _lastAnalyzedPath;
        private float   _lastAnalyzedBpm;
        private float   _autoLowThreshold;
        private float   _autoHighThreshold;

        // 動画からの自動抽出
        private volatile bool   _wavExtractionRunning;
        private volatile string _wavExtractionResult; // null=未実行, ""=失敗, path=成功
        private string          _lastVideoPath;
        private string          _ffmpegMissingWarnedFor;

        private double _videoRoomTimeSec;
        private bool   _videoRoomPlaying;
        private float  _nextVideoRoomPollTime;
        private float  _nextVideoRoomErrorLogTime;
        private bool   _videoRoomFetchWasAvailable;

        private float  _nextVerboseTime;
        private float  _smoothedIntensity01 = -1f;

        // 曲ごとのBPM保存
        private readonly Dictionary<string, int> _songBpmByPath =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string _currentSongPathKey;
        private float  _nextSongPathPollTime;
        private bool   _suppressSongBpmPersist;

        // ── Motion switch state ───────────────────────────────────
        private float  _highSustainStart = -1f;
        private float  _lowSustainStart  = -1f;
        private float  _currentZoneRawTarget = -1f; // Low/Mid/High の生ターゲット値

        private static readonly System.Reflection.FieldInfo LstFemaleField =
            AccessTools.Field(typeof(HSceneProc), "lstFemale");

        // Harmonyパッチから参照
        internal static float CurrentIntensity01 = -1f;
        internal static bool IsActive =>
            Instance != null &&
            Instance._cfgEnabled.Value &&
            Instance._analysisReady &&
            Instance._videoRoomPlaying &&
            CurrentIntensity01 >= 0f;

        // ── Unity lifecycle ──────────────────────────────────────
        private void Awake()
        {
            Instance    = this;
            _pluginDir  = Path.GetDirectoryName(Info.Location);
            _logFilePath = Path.Combine(_pluginDir, PluginName + ".log");
            Directory.CreateDirectory(_pluginDir);

            if (_runtimeLoggingEnabled) File.WriteAllText(_logFilePath,
                $"[{DateTime.Now:HH:mm:ss}] === {PluginName} {Version} started ==={Environment.NewLine}",
                Utf8NoBom);

            BindConfig();
            LoadSongBpmMap();
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            LogInfo($"loaded pluginDir={_pluginDir}");
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (_cfgToggleKey.Value.IsDown())
            {
                _cfgEnabled.Value = !_cfgEnabled.Value;
                LogInfo($"toggled enabled={_cfgEnabled.Value}");
                ShowRhythmSyncToggleNotice(_cfgEnabled.Value);
            }

            if (!_cfgEnabled.Value)
            {
                CurrentIntensity01 = -1f;
                return;
            }

            UpdateTapTempo();
            ScanHScene();
            if (!_insideHScene) return;

            RefreshSongBpmAutoLoad();
            EnsureAnalysis();
            PollVideoRoom();
            UpdateCurrentIntensity();
            UpdateMotionSwitch();
        }

        private void ShowRhythmSyncToggleNotice(bool enabled)
        {
            string text = enabled ? "リズム同期 ON" : "リズム同期 OFF";
            if (TryShowSpeedLimitBreakNotice(text))
                return;

            LogWarn("UI通知の表示先(MainGameSpeedLimitBreak)が見つからないため、画面通知を出せませんでした");
        }

        private static bool TryShowSpeedLimitBreakNotice(string text)
        {
            try
            {
                Type speedLimitBreakType = Type.GetType("MainGameSpeedLimitBreak.Plugin, MainGameSpeedLimitBreak", throwOnError: false);
                if (speedLimitBreakType == null)
                    return false;

                PropertyInfo instanceProperty = speedLimitBreakType.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object instance = instanceProperty?.GetValue(null, null);
                if (instance == null)
                    return false;

                MethodInfo showUiNoticeMethod = speedLimitBreakType.GetMethod(
                    "ShowUiNotice",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(string) },
                    modifiers: null);
                if (showUiNoticeMethod == null)
                    return false;

                showUiNoticeMethod.Invoke(instance, new object[] { text });
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ── HScene detection ─────────────────────────────────────
        private void ScanHScene()
        {
            if (Time.unscaledTime < _nextHSceneScanTime) return;
            _nextHSceneScanTime = Time.unscaledTime + 1f;

            if (_hSceneProc == null)
                _hSceneProc = FindObjectOfType<HSceneProc>();

            bool inside = _hSceneProc != null;
            if (inside != _insideHScene)
            {
                _insideHScene = inside;
                LogInfo($"insideHScene={_insideHScene}");
                if (!_insideHScene)
                {
                    CurrentIntensity01 = -1f;
                    _highSustainStart = -1f;
                    _lowSustainStart  = -1f;
                    _currentSongPathKey = null;
                }
            }
        }

        // ── Analysis trigger ─────────────────────────────────────
        private void EnsureAnalysis()
        {
            if (_analysisReady)
            {
                // 動画が切り替わったら再解析
                if (string.IsNullOrEmpty((_cfgWavPath.Value ?? "").Trim()))
                    CheckVideoPathChanged();
                return;
            }

            float  bpm     = (float)_cfgBpm.Value;
            string wavPath = ResolveWavPath();
            if (wavPath == null) return;

            if (wavPath == _lastAnalyzedPath && Mathf.Approximately(bpm, _lastAnalyzedBpm))
                return;

            LogInfo($"WAV解析開始: {wavPath}  BPM={bpm}");
            bool ok = TryAnalyzeWav(wavPath, bpm, _cfgLowPassHz.Value,
                                    out float[] intensities, out float beatDur);
            if (!ok)
            {
                LogWarn("WAV解析失敗。");
                _lastAnalyzedPath = wavPath;
                return;
            }

            _beatIntensities  = intensities;
            _beatDurationSec  = beatDur;
            _analysisReady    = true;
            _lastAnalyzedPath = wavPath;
            _lastAnalyzedBpm  = bpm;

            CalcAutoThresholds(intensities);
            LogInfo($"WAV解析完了: beats={intensities.Length}  beatDur={beatDur:0.###}s  " +
                    $"autoLow={_autoLowThreshold:0.###}  autoHigh={_autoHighThreshold:0.###}");
        }

        /// <summary>
        /// WAVファイルパスを解決する。
        /// WavFilePath設定が空の場合は動画から自動抽出（バックグラウンド）。
        /// 準備できていない場合は null を返す。
        /// </summary>
        private string ResolveWavPath()
        {
            string cfgWav = (_cfgWavPath.Value ?? "").Trim();

            // 手動指定があればそちら優先
            if (!string.IsNullOrEmpty(cfgWav))
            {
                if (!File.Exists(cfgWav))
                {
                    if (_lastAnalyzedPath != cfgWav)
                    {
                        LogWarn($"WAV not found: {cfgWav}");
                        _lastAnalyzedPath = cfgWav;
                    }
                    return null;
                }
                return cfgWav;
            }

            // 抽出結果が届いていれば消費
            if (_wavExtractionResult != null)
            {
                string result = _wavExtractionResult;
                _wavExtractionResult = null;
                if (result == "") return null; // 失敗
                return result;
            }

            if (_wavExtractionRunning) return null;

            // 動画パスを取得
            if (!TryGetVideoFilePath(out string videoPath)) return null;

            _lastVideoPath = videoPath;

            // キャッシュ済みWAVがあればそのまま使う
            string wavPath = Path.Combine(_pluginDir, "cache",
                Path.GetFileNameWithoutExtension(videoPath) + ".wav");
            if (File.Exists(wavPath)) return wavPath;

            // ffmpegを探す
            string ffmpegPath = FindFfmpegPath();
            if (ffmpegPath == null)
            {
                if (_ffmpegMissingWarnedFor != videoPath)
                {
                    LogWarn("ffmpeg.exe が見つかりません。plugins 配下の _tools/ffmpeg/(bin/)ffmpeg.exe を同梱するか、PATH に追加してください。");
                    _ffmpegMissingWarnedFor = videoPath;
                }
                return null;
            }

            // バックグラウンドで抽出
            LogInfo($"動画から音声抽出開始: {videoPath}");
            _wavExtractionRunning = true;
            string vp = videoPath, wp = wavPath, fp = ffmpegPath;
            new Thread(() =>
            {
                bool ok = TryExtractWavFromVideo(vp, wp, fp);
                _wavExtractionResult = ok ? wp : "";
                _wavExtractionRunning = false;
            }) { IsBackground = true }.Start();

            return null;
        }

        private void CalcAutoThresholds(float[] intensities)
        {
            // コピーしてソート → 33/67パーセンタイルを閾値にする
            float[] sorted = new float[intensities.Length];
            Array.Copy(intensities, sorted, intensities.Length);
            Array.Sort(sorted);
            _autoLowThreshold  = sorted[(int)(sorted.Length * 0.33f)];
            _autoHighThreshold = sorted[(int)(sorted.Length * 0.67f)];
        }

        private void CheckVideoPathChanged()
        {
            if (TryGetVideoFilePath(out string currentPath) && currentPath != _lastVideoPath)
            {
                LogInfo($"動画変更検出: {currentPath}");
                _lastVideoPath = currentPath;
                InvalidateAnalysis();
            }
        }

        // ── Intensity lookup ─────────────────────────────────────
        private void UpdateCurrentIntensity()
        {
            if (!_analysisReady || !_videoRoomPlaying ||
                _beatIntensities == null || _beatIntensities.Length == 0)
            {
                CurrentIntensity01 = -1f;
                _smoothedIntensity01 = -1f;
                _currentZoneRawTarget = -1f;
                return;
            }

            int beatIndex = (int)(_videoRoomTimeSec / _beatDurationSec);
            beatIndex = Mathf.Clamp(beatIndex, 0, _beatIntensities.Length - 1);

            float normalized = _beatIntensities[beatIndex];
            float low  = _cfgAutoThreshold.Value ? _autoLowThreshold  : _cfgLowThreshold.Value;
            float high = _cfgAutoThreshold.Value ? _autoHighThreshold : _cfgHighThreshold.Value;

            float target;
            if      (normalized < low)  target = _cfgLowIntensity.Value;
            else if (normalized < high) target = _cfgMidIntensity.Value;
            else                        target = _cfgHighIntensity.Value;

            _currentZoneRawTarget = target;

            // voice.speedMotion をゾーンで直接制御（ゲームの閾値ロジックは無視）
            if (_hSceneProc?.flags?.voice != null)
            {
                if (Mathf.Approximately(target, _cfgHighIntensity.Value))
                    _hSceneProc.flags.voice.speedMotion = true;
                else if (Mathf.Approximately(target, _cfgLowIntensity.Value))
                    _hSceneProc.flags.voice.speedMotion = false;
                // Mid はそのまま維持
            }

            float smoothTime = _cfgSmoothTime.Value;
            if (smoothTime <= 0f || _smoothedIntensity01 < 0f)
                _smoothedIntensity01 = target;
            else
                _smoothedIntensity01 = Mathf.MoveTowards(
                    _smoothedIntensity01, target, Time.unscaledDeltaTime / smoothTime);

            CurrentIntensity01 = _smoothedIntensity01;

            if (_cfgVerboseLog.Value && Time.unscaledTime >= _nextVerboseTime)
            {
                _nextVerboseTime = Time.unscaledTime + 0.5f;
                LogInfo($"beat={beatIndex} norm={normalized:0.###} target={target:0.###} smooth={_smoothedIntensity01:0.###} time={_videoRoomTimeSec:0.###}");
            }
        }

        // ── Config binding ───────────────────────────────────────
        private void BindConfig()
        {
            _cfgEnabled   = Config.Bind("General", "Enabled", true, "ビートシンク有効/無効");
            _cfgToggleKey = Config.Bind("General", "ToggleKey",
                new KeyboardShortcut(KeyCode.F9), "有効/無効切替キー");

            _cfgWavPath = Config.Bind("Audio", "WavFilePath", "",
                "WAVファイルのフルパス（空にすると動画部屋の動画から自動抽出）");
            _cfgBpm = Config.Bind("Audio", "Bpm", 107, "曲のBPM（例: 128, 139, 174）");
            _cfgLowPassHz = Config.Bind("Audio", "LowPassHz", 50f,
                new ConfigDescription("ローパスフィルタ周波数(Hz)。低音のみ解析する",
                    new AcceptableValueRange<float>(50f, 500f)));

            _cfgAutoThreshold = Config.Bind("Speed", "AutoThreshold", false,
                "ON推奨。曲の低音エネルギーを全拍で集計し、下位33%をLow/Mid境界、上位33%をMid/High境界として自動算出する。" +
                "曲ごとに動的に調整されるため手動設定不要。OFFにするとLowThreshold/HighThresholdを手動で使う。");
            _cfgLowThreshold = Config.Bind("Speed", "LowThreshold", 0.25f,
                new ConfigDescription(
                    "[AutoThreshold=OFF時のみ有効] 正規化エネルギー(0-1)がこの値未満の拍をLow(静か)と判定する。" +
                    "例: 0.3 → エネルギーが30%未満の拍はLowSpeed扱い",
                    new AcceptableValueRange<float>(0f, 1f)));
            _cfgHighThreshold = Config.Bind("Speed", "HighThreshold", 0.7f,
                new ConfigDescription(
                    "[AutoThreshold=OFF時のみ有効] 正規化エネルギー(0-1)がこの値以上の拍をHigh(盛り上がり)と判定する。" +
                    "LowThreshold以上HighThreshold未満はMid扱い。例: 0.7 → 70%以上の拍はHighSpeed扱い",
                    new AcceptableValueRange<float>(0f, 1f)));
            _cfgLowIntensity = Config.Bind("Speed", "LowSpeed", 0f,
                new ConfigDescription(
                    "静かな拍(Low)のアニメ速度。0=ゲーム最低速、1=ゲーム最高速。" +
                    "内部的にはspeedCalc(0-1)として使われ、ゲームのspeedカーブで実速度に変換される。",
                    new AcceptableValueRange<float>(0f, 1f)));
            _cfgMidIntensity = Config.Bind("Speed", "MidSpeed", 0.44f,
                new ConfigDescription(
                    "普通の拍(Mid)のアニメ速度。LowSpeedとHighSpeedの中間に設定するのが自然。",
                    new AcceptableValueRange<float>(0f, 1f)));
            _cfgHighIntensity = Config.Bind("Speed", "HighSpeed", 1.0f,
                new ConfigDescription(
                    "盛り上がり拍(High)のアニメ速度。1.0でゲーム最高速。",
                    new AcceptableValueRange<float>(0f, 1f)));

            _cfgSmoothTime = Config.Bind("Speed", "SmoothTime", 0.5f,
                new ConfigDescription("速度変化の補間時間(秒)。0=瞬間切替",
                    new AcceptableValueRange<float>(0f, 2f)));

            _cfgAutoMotionSwitch = Config.Bind("MotionSwitch", "AutoMotionSwitch", false,
                "ONにすると拍エネルギーに応じて強/弱モーションを自動切替する。OFFで無効。");
            _cfgStrongMotionBeats = Config.Bind("MotionSwitch", "StrongMotionBeats", 8f,
                new ConfigDescription(
                    "Highゾーン(盛り上がり)が何拍続いたら強モーションへ切替えるか",
                    new AcceptableValueRange<float>(0.5f, 64f)));
            _cfgWeakMotionBeats = Config.Bind("MotionSwitch", "WeakMotionBeats", 2f,
                new ConfigDescription(
                    "Lowゾーン(静か)が何拍続いたら弱モーションへ切替えるか",
                    new AcceptableValueRange<float>(0.5f, 64f)));

            _cfgVerboseLog = Config.Bind("Debug", "VerboseLog", false,
                "0.5秒ごとにbeat/intensity/timeをログ出力（重い）");

            _cfgWavPath.SettingChanged   += (_, __) => InvalidateAnalysis();
            _cfgBpm.SettingChanged       += OnBpmSettingChanged;
            _cfgLowPassHz.SettingChanged += (_, __) => InvalidateAnalysis();
            _cfgEnabled.SettingChanged   += (_, __) => { if (!_cfgEnabled.Value) CurrentIntensity01 = -1f; };
        }

        // ── Motion switch ─────────────────────────────────────────
        private void UpdateMotionSwitch()
        {
            if (!_cfgAutoMotionSwitch.Value) return;
            if (_hSceneProc?.flags == null) return;
            if (_currentZoneRawTarget < 0f) return;

            // 現在の強/弱状態を Animator state name で判定
            var females = LstFemaleField?.GetValue(_hSceneProc) as System.Collections.Generic.List<ChaControl>;
            ChaControl female = females != null && females.Count > 0 ? females[0] : null;
            if (female == null) return;

            AnimatorStateInfo stateInfo = female.getAnimatorStateInfo(0);
            bool isCurrentlyStrong = stateInfo.IsName("SLoop")    || stateInfo.IsName("A_SLoop") ||
                                     stateInfo.IsName("SS_IN_Loop")|| stateInfo.IsName("SF_IN_Loop") ||
                                     stateInfo.IsName("sameS")     || stateInfo.IsName("orgS");
            bool isCurrentlyWeak   = stateInfo.IsName("WLoop")    || stateInfo.IsName("A_WLoop") ||
                                     stateInfo.IsName("WS_IN_Loop")|| stateInfo.IsName("WF_IN_Loop") ||
                                     stateInfo.IsName("sameW")     || stateInfo.IsName("orgW");

            float now  = Time.unscaledTime;
            float secPerBeat = 60f / Mathf.Max(1f, (float)_cfgBpm.Value);
            bool isHigh = Mathf.Approximately(_currentZoneRawTarget, _cfgHighIntensity.Value);
            bool isLow  = Mathf.Approximately(_currentZoneRawTarget, _cfgLowIntensity.Value);

            if (isHigh)
            {
                _lowSustainStart = -1f;
                if (_highSustainStart < 0f) _highSustainStart = now;
                if (!isCurrentlyStrong && now - _highSustainStart >= _cfgStrongMotionBeats.Value * secPerBeat)
                {
                    _hSceneProc.flags.click = HFlag.ClickKind.motionchange;
                    _highSustainStart = now; // 連発防止
                    LogInfo("[motion] → 強モーション (high sustained)");
                }
            }
            else if (isLow)
            {
                _highSustainStart = -1f;
                if (_lowSustainStart < 0f) _lowSustainStart = now;
                if (!isCurrentlyWeak && now - _lowSustainStart >= _cfgWeakMotionBeats.Value * secPerBeat)
                {
                    _hSceneProc.flags.click = HFlag.ClickKind.motionchange;
                    _lowSustainStart = now; // 連発防止
                    LogInfo("[motion] → 弱モーション (low sustained)");
                }
            }
            else // Mid
            {
                _highSustainStart = -1f;
                _lowSustainStart  = -1f;
            }
        }

        private void InvalidateAnalysis()
        {
            _analysisReady    = false;
            _lastAnalyzedPath = null;
        }

        // ── Logging ──────────────────────────────────────────────
        internal void LogInfo(string msg)  => WriteLog("INFO",  msg);
        internal void LogWarn(string msg)  => WriteLog("WARN",  msg);
        internal void LogError(string msg) => WriteLog("ERROR", msg);

        private void WriteLog(string level, string msg)
        {
            if (!_runtimeLoggingEnabled) return;
            bool verbose = _cfgVerboseLog != null && _cfgVerboseLog.Value;
            bool isError = level == "WARN" || level == "ERROR";
            if (!verbose && !isError) return;

            Logger?.LogInfo($"[{level}] {msg}");
            try
            {
                lock (_logLock)
                {
                    File.AppendAllText(_logFilePath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {msg}{Environment.NewLine}",
                        Utf8NoBom);
                }
            }
            catch { }
        }
    }
}
