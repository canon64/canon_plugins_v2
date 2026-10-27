using BepInEx;
using BepInEx.Configuration;
using CanonPlugins.Shared;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MainGameHandySync
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    internal sealed partial class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kks.maingame.handysync";
        public const string PluginName = "MainGameHandySync";
        public const string Version = "0.1.0";

        internal static Plugin Instance { get; private set; }

        internal static new BepInEx.Logging.ManualLogSource Logger { get; private set; }

        // BepInEx config entries (runtime, hot-reload friendly)
        private ConfigEntry<bool> _cfgEnabled;
        // ── HAMP BPM追従（実機検証済みの方式） ────────────────────
        private ConfigEntry<bool>  _cfgHampEnabled;
        private ConfigEntry<bool>  _cfgStopWhenIdle;
        private ConfigEntry<bool>  _cfgUseSpeedLimitBreakBpm;
        private ConfigEntry<float> _cfgBpmMultiplier;
        private ConfigEntry<float> _cfgBpmMin;
        private ConfigEntry<float> _cfgBpmMax;
        private ConfigEntry<float> _cfgManualBpmAtGauge0;
        private ConfigEntry<float> _cfgManualBpmAtGauge1;
        private ConfigEntry<float> _cfgStrokeMinAtGauge0;
        private ConfigEntry<float> _cfgStrokeMaxAtGauge0;
        private ConfigEntry<float> _cfgStrokeMinAtGauge1;
        private ConfigEntry<float> _cfgStrokeMaxAtGauge1;
        private ConfigEntry<float> _cfgVelocityConstant;
        private ConfigEntry<float> _cfgVelocityMin;
        private ConfigEntry<float> _cfgVelocityMax;
        private ConfigEntry<float> _cfgVelocityDeadband;
        private ConfigEntry<float> _cfgStrokeDeadband;
        private ConfigEntry<float> _cfgSwitchCooldownSec;
        private ConfigEntry<float> _cfgGaugeInputMin;
        private ConfigEntry<float> _cfgGaugeInputMax;
        private ConfigEntry<float> _cfgLogIntervalSec;
        private ConfigEntry<float> _cfgSendIntervalSec;
        private ConfigEntry<float> _cfgGaugeSmoothSec;
        private ConfigEntry<bool>  _cfgSendOnlyWhenChanged;
        private ConfigEntry<string> _cfgConnectionKey;
        private ConfigEntry<float> _cfgSampleInterval;
        private ConfigEntry<int> _cfgStrokeMin;
        private ConfigEntry<int> _cfgStrokeMax;
        private ConfigEntry<bool> _cfgInvert;
        private ConfigEntry<float> _cfgPhaseOffset;           // 0.0 ~ 1.0 slider
        private ConfigEntry<bool> _cfgShowUI;
        private ConfigEntry<bool> _cfgEnableNormalLog;
        private ConfigEntry<bool> _cfgEnableDetailedLog;
        private ConfigEntry<bool> _cfgEnableErrorLog;
        private ConfigEntry<bool> _cfgSavePhasePerPose;

        // v3 HDSP 位置同期
        private ConfigEntry<bool> _cfgUseV3;
        private ConfigEntry<string> _cfgV3Token;   // 秘密キー(ApplicationKey)
        private ConfigEntry<bool> _cfgSmooth;      // 滑らかストローク（sine細分化）。重い/詰まる場合はoff
        private ConfigEntry<int> _cfgUpdatesHz;    // smooth時の細分化レート

        // LoveMachine(Sauceke, GPLv3) 移植：ボーン解析＋ストローク制御
        private LM.LmGame _lmGame;
        private LM.LmAnalyzer _lmAnalyzer;
        private LM.LmStroker _lmStroker;
        private Coroutine _strokeCoroutine;

        private string _pluginDir;
        private float _nextSampleTime;
        private readonly AnimationPhaseReader _animationReader = new AnimationPhaseReader();
        private readonly HandyClient _handyClient = new HandyClient();

        // Per-pose phase memory
        private readonly Dictionary<string, float> _posePhaseOffsets = new Dictionary<string, float>();
        private string _currentPoseKey = "";

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            _pluginDir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            SimpleFileLogger.Init(Path.Combine(_pluginDir, "MainGameHandySync.log"));

            SimpleFileLogger.Info($"=== {PluginName} {Version} Awake ===");

            SetupConfig();
            SimpleFileLogger.Configure(ShouldLogNormal, ShouldLogDetailed, ShouldLogError);
            SetupNoMusicBpmConfig();

            ApplyHarmonyPatches();

            ConfigureHandyClient();

            // キーを入れ直したら即座に繋ぎ直す。再起動を要らなくする。
            _cfgConnectionKey.SettingChanged += (_, __) => ConfigureHandyClient();
            _cfgV3Token.SettingChanged += (_, __) => ConfigureHandyClient();
            _cfgUseV3.SettingChanged += (_, __) => ConfigureHandyClient();

            // LoveMachine 移植のセットアップ。LinearCmd(pos0-1, secs) → v3 /hdsp/xpt
            _lmGame = new LM.LmGame();
            _lmAnalyzer = new LM.LmAnalyzer();
            _lmStroker = new LM.LmStroker((pos01, secs) => _handyClient.SendXpt(pos01, secs * 1000f));

            SimpleFileLogger.Info("Awake complete. Waiting for HScene...");
        }

        private void SetupConfig()
        {
            _cfgEnabled = Config.Bind("General", "Enabled", true, "Master toggle for Handy sync");
            _cfgConnectionKey = Config.Bind("Handy", "ConnectionKey", "", "Your device's Connection Key");
            _cfgSampleInterval = Config.Bind("Sync", "SampleInterval", 0.1f, new ConfigDescription("How often to read animation (seconds). 0.1 = 10 times per second.", new AcceptableValueRange<float>(0.05f, 0.5f)));
            _cfgStrokeMin = Config.Bind("Handy", "StrokeMin", 5, new ConfigDescription("Minimum stroke position (0-100).", new AcceptableValueRange<int>(0, 100)));
            _cfgStrokeMax = Config.Bind("Handy", "StrokeMax", 95, new ConfigDescription("Maximum stroke position (0-100).", new AcceptableValueRange<int>(0, 100)));
            _cfgInvert = Config.Bind("Handy", "Invert", false, "Invert stroke direction");
            _cfgPhaseOffset = Config.Bind("Sync", "PhaseOffset", 0.0f, new ConfigDescription("Phase shift (0.0 ~ 1.0). Use slider in UI for live adjustment.", new AcceptableValueRange<float>(0f, 1f)));
            _cfgShowUI = Config.Bind("UI", "ShowUI", true, "Show the HandySync control window");
            _cfgEnableNormalLog = Config.Bind("Logging", "EnableNormalLog", false, "Normal log output");
            _cfgEnableDetailedLog = Config.Bind("Logging", "EnableDetailedLog", false, "Detailed (verbose) log output");
            _cfgEnableErrorLog = Config.Bind("Logging", "EnableErrorLog", false, "Error / warning log output");
            _cfgSavePhasePerPose = Config.Bind("Sync", "SavePhasePerPose", true, "Remember phase offset per body position + motion");

            // ── HAMP BPM追従 ─────────────────────────────────────
            // 全項目 BepInEx の cfg に自動保存される。決め打ちの定数は置かない。
            _cfgHampEnabled = Config.Bind("HAMP", "00 Enabled", true,
                "HAMP自走でBPMに追従する。OFFで旧HDSP経路（この回線では滑らかに動かない）");
            _cfgStopWhenIdle = Config.Bind("HAMP", "01 StopWhenIdle", true,
                "待機/演出中(ループ状態でない時)は止める。ゲージ0では止めない");
            _cfgUseSpeedLimitBreakBpm = Config.Bind("HAMP", "02 UseSpeedLimitBreakBpm", true,
                "SpeedLimitBreakの較正BPMを使う。取れない場合は下の手動BPMへ落ちる");

            _cfgManualBpmAtGauge0 = Config.Bind("HAMP BPM", "00 ManualBpmAtGauge0", 30f,
                new ConfigDescription("SpeedLimitBreakが使えない時の、ゲージ0でのBPM(=1/4拍相当)",
                    new AcceptableValueRange<float>(1f, 400f)));
            _cfgManualBpmAtGauge1 = Config.Bind("HAMP BPM", "01 ManualBpmAtGauge1", 120f,
                new ConfigDescription("同上、ゲージ最大でのBPM(=1拍相当)",
                    new AcceptableValueRange<float>(1f, 400f)));
            _cfgBpmMultiplier = Config.Bind("HAMP BPM", "02 BpmMultiplier", 1f,
                new ConfigDescription("BPMの倍数。0.5で半分、2で倍。拍に合わない時の調整",
                    new AcceptableValueRange<float>(0.05f, 8f)));
            _cfgBpmMin = Config.Bind("HAMP BPM", "03 BpmMin", 5f,
                new ConfigDescription("BPMの下限クランプ", new AcceptableValueRange<float>(1f, 400f)));
            _cfgBpmMax = Config.Bind("HAMP BPM", "04 BpmMax", 400f,
                new ConfigDescription("BPMの上限クランプ", new AcceptableValueRange<float>(1f, 600f)));

            _cfgStrokeMinAtGauge0 = Config.Bind("HAMP Stroke", "00 MinAtGauge0", 0.0f,
                new ConfigDescription("ゲージ0のときの最下点(0-1)。遅い時は深く広く",
                    new AcceptableValueRange<float>(0f, 1f)));
            _cfgStrokeMaxAtGauge0 = Config.Bind("HAMP Stroke", "01 MaxAtGauge0", 1.0f,
                new ConfigDescription("ゲージ0のときの最上点(0-1)", new AcceptableValueRange<float>(0f, 1f)));
            _cfgStrokeMinAtGauge1 = Config.Bind("HAMP Stroke", "02 MinAtGauge1", 0.3f,
                new ConfigDescription("ゲージ最大のときの最下点(0-1)。速い時は浅く狭く",
                    new AcceptableValueRange<float>(0f, 1f)));
            _cfgStrokeMaxAtGauge1 = Config.Bind("HAMP Stroke", "03 MaxAtGauge1", 0.6f,
                new ConfigDescription("ゲージ最大のときの最上点(0-1)", new AcceptableValueRange<float>(0f, 1f)));

            _cfgVelocityConstant = Config.Bind("HAMP Tuning", "00 VelocityConstant", 133f,
                new ConfigDescription("velocity = BPM × 振幅の幅 / この値。実機H02_proの実測値",
                    new AcceptableValueRange<float>(10f, 600f)));
            _cfgVelocityMin = Config.Bind("HAMP Tuning", "01 VelocityMin", 0.02f,
                new ConfigDescription("velocityの下限", new AcceptableValueRange<float>(0f, 1f)));
            _cfgVelocityMax = Config.Bind("HAMP Tuning", "02 VelocityMax", 1.0f,
                new ConfigDescription("velocityの上限", new AcceptableValueRange<float>(0f, 1f)));
            _cfgVelocityDeadband = Config.Bind("HAMP Tuning", "03 VelocityDeadband", 0.02f,
                new ConfigDescription("この幅を超えた時だけ速度を送り直す。指令1本に約1.5秒かかるため",
                    new AcceptableValueRange<float>(0f, 0.5f)));
            _cfgStrokeDeadband = Config.Bind("HAMP Tuning", "04 StrokeDeadband", 0.03f,
                new ConfigDescription("この幅を超えた時だけ振幅を送り直す",
                    new AcceptableValueRange<float>(0f, 0.5f)));
            _cfgSwitchCooldownSec = Config.Bind("HAMP Tuning", "05 SwitchCooldownSec", 1.0f,
                new ConfigDescription("開始/停止の最低間隔(秒)", new AcceptableValueRange<float>(0f, 10f)));
            _cfgSendIntervalSec = Config.Bind("HAMP Tuning", "09 SendIntervalSec", 0.3f,
                new ConfigDescription(
                    "指令を送る最短間隔(秒)。実測で1本届くのに約0.92秒かかる(内訳: 日本→サーバー0.28秒、"
                    + "サーバー→デバイス0.64秒)。これより短くしても古い方は捨てられる。0で毎フレーム",
                    new AcceptableValueRange<float>(0f, 5f)));
            _cfgGaugeSmoothSec = Config.Bind("HAMP Tuning", "10 GaugeSmoothSec", 0.8f,
                new ConfigDescription(
                    "速度ゲージの平滑化の時定数(秒)。HFlag.speedCalcは毎秒大きく揺れるので、"
                    + "そのまま追従すると指令が渋滞する。0で平滑化しない",
                    new AcceptableValueRange<float>(0f, 5f)));
            _cfgSendOnlyWhenChanged = Config.Bind("HAMP Tuning", "11 SendOnlyWhenChanged", true,
                "デッドバンドを超えた時だけ送る。OFFにすると間隔ごとに毎回送る");
            _cfgLogIntervalSec = Config.Bind("HAMP Tuning", "08 LogIntervalSec", 1.5f,
                new ConfigDescription("動作ログの間引き間隔(秒)。0で出さない。詳細ログONの時だけ出る",
                    new AcceptableValueRange<float>(0f, 30f)));
            _cfgGaugeInputMin = Config.Bind("HAMP Tuning", "06 GaugeInputMin", 0f,
                new ConfigDescription("HFlag.speedCalc をゲージ0とみなす値",
                    new AcceptableValueRange<float>(0f, 1f)));
            _cfgGaugeInputMax = Config.Bind("HAMP Tuning", "07 GaugeInputMax", 1f,
                new ConfigDescription("HFlag.speedCalc をゲージ最大とみなす値",
                    new AcceptableValueRange<float>(0f, 1f)));

            _cfgUseV3 = Config.Bind("V3", "UseV3", true, "Use v3 API + HDSP position sync (recommended). If false, falls back to v2 setPosition.");
            _cfgV3Token = Config.Bind("V3", "V3Token", "", "v3 secret key (ApplicationKey). Create one at user.handyfeeling.com with System=Handy REST API.");
            _cfgSmooth = Config.Bind("HDSP", "SmoothStroking", false, "Smooth (sine, subdivided) strokes. Off = 2 segments per stroke (lighter, proven). On may flood the device.");
            _cfgUpdatesHz = Config.Bind("HDSP", "UpdatesHz", 16, new ConfigDescription("Subdivision rate when SmoothStroking is on.", new AcceptableValueRange<int>(4, 30)));

            // Clamp stroke range sanity
            if (_cfgStrokeMin.Value > _cfgStrokeMax.Value)
            {
                var tmp = _cfgStrokeMin.Value;
                _cfgStrokeMin.Value = _cfgStrokeMax.Value;
                _cfgStrokeMax.Value = tmp;
            }
        }

        // BlankMapAdd の追加マップ以外では一切発火させない。
        private bool _gateWasInRoom;

        private void Update()
        {
            // 外部機器を掴んだまま部屋を出ると動き続けてしまうので、退室時に必ず手を離す。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, OnBlankRoomLeft)) return;

            if (_cfgEnabled == null || !_cfgEnabled.Value) return;

            EnsureHSceneRefs();
            UpdatePoseKeyIfChanged();

            if (InHScene && _hFlag != null && _hSceneProc != null)
            {
                _lmGame.Flags = _hFlag;

                if (_cfgHampEnabled != null && _cfgHampEnabled.Value
                    && _handyClient.UsesV3 && !string.IsNullOrWhiteSpace(GetConnectionKey()))
                {
                    // HAMP自走でBPMに追従する。往復遅延1.5秒の影響を受けない唯一の方式。
                    // 旧HDSP経路は動かさない（併走すると指令が競合する）。
                    if (_strokeCoroutine != null) { StopCoroutine(_strokeCoroutine); _strokeCoroutine = null; }
                    TickHampBpm();
                }
                else if (_handyClient.UsesV3 && !string.IsNullOrWhiteSpace(GetConnectionKey()))
                {
                    // 旧: LoveMachine 駆動（ボーン解析→HDSP位置送信）
                    if (_strokeCoroutine == null)
                        _strokeCoroutine = StartCoroutine(StrokeLoop());
                }
                else if (!_handyClient.UsesV3 && !string.IsNullOrWhiteSpace(GetConnectionKey()))
                {
                    // v2 フォールバック（従来の位置送信）
                    if (Time.unscaledTime >= _nextSampleTime)
                    {
                        float interval = _cfgSampleInterval != null ? _cfgSampleInterval.Value : 0.1f;
                        _nextSampleTime = Time.unscaledTime + interval;
                        float animPhase = _animationReader.GetAnimationPhase(_hSceneProc, _hFlag);
                        float offset = GetEffectivePhaseOffset();
                        float finalPhase = (animPhase + offset) % 1.0f;
                        if (finalPhase < 0f) finalPhase += 1f;
                        _handyClient.SendPositionV2(finalPhase, _cfgStrokeMin.Value, _cfgStrokeMax.Value, _cfgInvert.Value);
                    }
                }
            }
            else if (!InHScene)
            {
                // Hシーンを抜けたら必ず手を離す。掴んだまま出ると動き続ける。
                StopHampIfRunning("leave-hscene");
                if (_strokeCoroutine != null) { StopCoroutine(_strokeCoroutine); _strokeCoroutine = null; }
                _lmAnalyzer?.Clear();
                _animationReader.Reset();
                _currentPoseKey = "";
            }
        }

        private void OnBlankRoomLeft()
        {
            if (_strokeCoroutine != null) { StopCoroutine(_strokeCoroutine); _strokeCoroutine = null; }
            _lmAnalyzer?.Clear();
            _animationReader.Reset();
            _currentPoseKey = "";
        }

        // ===== LoveMachine 駆動ループ =====
        // ClassicButtplugController.RunStrokeLoop + ButtplugController.TryGetCurrentStrokeInfo 相当。
        // 解析結果(StrokeInfo)が出るまでは待機し、出たら StrokerController.HandleAnimation を回す。

        private IEnumerator StrokeLoop()
        {
            while (true)
            {
                if (!InHScene || _hFlag == null || _hSceneProc == null
                    || !_handyClient.UsesV3 || string.IsNullOrWhiteSpace(GetConnectionKey())
                    || _externalMotionStopped)
                {
                    yield return new WaitForSecondsRealtime(0.2f);
                    continue;
                }

                _lmGame.Flags = _hFlag;

                // アイドル（非挿入/低速）なら待機
                bool idle;
                try { idle = _lmGame.IsIdle(0); } catch { idle = true; }
                if (idle)
                {
                    yield return new WaitForSecondsRealtime(0.2f);
                    continue;
                }

                LM.TrackingKey key = default;
                float normalizedTime = 0f;
                bool stateOk = true;
                try
                {
                    key = new LM.TrackingKey
                    {
                        GirlIndex = 0,
                        Bone = LM.Bone.Auto,
                        Pose = _lmGame.GetPose(0),
                        POV = LM.POV.Balanced,
                        Axis = LM.Axis.Longest,
                        MovementType = LM.MovementType.Linear
                    };
                    _lmGame.GetAnimState(0, out normalizedTime, out _, out _);
                    normalizedTime += GetEffectivePhaseOffset();   // 位相合わせ
                }
                catch (Exception ex)
                {
                    if (ShouldLogError()) SimpleFileLogger.Warn($"[LM] state read failed: {ex.Message}");
                    stateOk = false;
                }
                if (!stateOk)
                {
                    yield return new WaitForSecondsRealtime(0.2f);
                    continue;
                }

                if (!_lmAnalyzer.TryGetCurrentStrokeInfo(_lmGame, key, normalizedTime, out var strokeInfo))
                {
                    // 未解析 → 解析コルーチンを起動して待つ
                    if (!_lmAnalyzer.IsAnalyzed(key) && !_lmAnalyzer.IsAnalyzing(key))
                        StartCoroutine(_lmAnalyzer.AnalyzeAnimation(_lmGame, key));
                    yield return new WaitForSecondsRealtime(0.1f);
                    continue;
                }

                // ストローカー設定を config から反映
                _lmStroker.SmoothStroking = _cfgSmooth != null && _cfgSmooth.Value;
                _lmStroker.UpdatesHz = _cfgUpdatesHz != null ? _cfgUpdatesHz.Value : 16;
                _lmStroker.ZoneMin = (_cfgStrokeMin != null ? _cfgStrokeMin.Value : 5) / 100f;
                _lmStroker.ZoneMax = (_cfgStrokeMax != null ? _cfgStrokeMax.Value : 95) / 100f;
                if (_cfgInvert != null && _cfgInvert.Value)
                {
                    var t = _lmStroker.ZoneMin; _lmStroker.ZoneMin = _lmStroker.ZoneMax; _lmStroker.ZoneMax = t;
                }
                _lmStroker.Pattern = LM.StrokingPattern.Animation; // 解析した実ストローク形状を使う

                if (ShouldLogDetailed())
                    SimpleFileLogger.Debug($"[LM] stroke dur={strokeInfo.DurationSecs:F2} c={strokeInfo.Completion:F2} amp={strokeInfo.Amplitude:F3}");

                // LoveMachine RunStrokeLoop と同じく、内側コルーチンを手動で回して
                // Current(WaitForSeconds等)を直接 yield する（ネスト yield だと待ちが化ける）
                var handleAnimation = _lmStroker.HandleAnimation(strokeInfo);
                while (handleAnimation.MoveNext())
                    yield return handleAnimation.Current;
            }
        }

        private void ApplyHarmonyPatches()
        {
            var harmony = new Harmony(GUID);
            harmony.PatchAll(typeof(Plugin));   // Patches in all partials (HScene.cs etc.) will be picked up
            SimpleFileLogger.Info($"Harmony patches applied (count approx via methods)");
        }

        // Helper for other partials
        internal string GetConnectionKey() => _cfgConnectionKey?.Value?.Trim() ?? "";
        internal bool IsEnabled() => _cfgEnabled != null && _cfgEnabled.Value;

        // TODO stubs (implemented in later partials)
        private void StartHandySyncForCurrentScene() { }
        private void StopHandySyncAndExportIfNeeded() { }

        /// <summary>
        /// Test current Connection Key against the Handy API.
        /// </summary>
        internal async void TestHandyConnection()
        {
            // v3/v2 configuration is handled in ConfigureHandyClient()
            _ = _handyClient.TestConnectionAsync();
        }

        private float GetEffectivePhaseOffset()
        {
            float baseOffset = _cfgPhaseOffset != null ? _cfgPhaseOffset.Value : 0f;

            if (_cfgSavePhasePerPose != null && _cfgSavePhasePerPose.Value && !string.IsNullOrEmpty(_currentPoseKey))
            {
                if (_posePhaseOffsets.TryGetValue(_currentPoseKey, out float saved))
                    return (baseOffset + saved) % 1.0f;
            }
            return baseOffset;
        }

        private void UpdatePoseKeyIfChanged()
        {
            if (_hFlag == null) return;

            string newKey = GeneratePoseKey();
            if (newKey != _currentPoseKey)
            {
                // Save previous pose's offset if enabled
                if (_cfgSavePhasePerPose != null && _cfgSavePhasePerPose.Value && !string.IsNullOrEmpty(_currentPoseKey))
                {
                    float currentUserOffset = _cfgPhaseOffset != null ? _cfgPhaseOffset.Value : 0f;
                    _posePhaseOffsets[_currentPoseKey] = currentUserOffset;
                }

                _currentPoseKey = newKey;

                // Load saved offset for new pose
                if (_cfgSavePhasePerPose != null && _cfgSavePhasePerPose.Value && _posePhaseOffsets.TryGetValue(_currentPoseKey, out float saved))
                {
                    if (_cfgPhaseOffset != null)
                        _cfgPhaseOffset.Value = saved;
                }

                if (ShouldLogNormal())
                    SimpleFileLogger.Info($"Pose changed: {_currentPoseKey}");
            }
        }

        private HSceneProc.AnimationListInfo _poseKeyInfo;
        private HFlag.EMode _poseKeyMode;
        private int _poseKeyInfoId;
        private string _poseKeyName;
        private string _cachedGeneratedPoseKey;
        private float _nextPoseKeyNameCheckTime;
        private Type _poseKeyInfoType;
        private System.Reflection.PropertyInfo _poseKeyNameProperty;

        private string GeneratePoseKey()
        {
            if (_hFlag == null) return "unknown";

            HSceneProc.AnimationListInfo animInfo = _hFlag.nowAnimationInfo;
            HFlag.EMode mode = _hFlag.mode;
            int infoId = animInfo != null ? animInfo.id : int.MinValue;
            bool identityChanged = !ReferenceEquals(_poseKeyInfo, animInfo) || _poseKeyMode != mode || _poseKeyInfoId != infoId;
            if (!identityChanged && _cachedGeneratedPoseKey != null && Time.unscaledTime < _nextPoseKeyNameCheckTime)
                return _cachedGeneratedPoseKey;
            _nextPoseKeyNameCheckTime = Time.unscaledTime + 0.5f;

            string animName = "default";
            try
            {
                if (animInfo != null)
                {
                    Type infoType = animInfo.GetType();
                    if (_poseKeyInfoType != infoType)
                    {
                        _poseKeyInfoType = infoType;
                        _poseKeyNameProperty = infoType.GetProperty("name");
                    }
                    if (_poseKeyNameProperty != null)
                    {
                        string n = _poseKeyNameProperty.GetValue(animInfo) as string;
                        if (!string.IsNullOrEmpty(n)) animName = n;
                    }
                }
            }
            catch { }

            if (!identityChanged && _cachedGeneratedPoseKey != null && _poseKeyName == animName)
                return _cachedGeneratedPoseKey;
            _poseKeyInfo = animInfo;
            _poseKeyMode = mode;
            _poseKeyInfoId = infoId;
            _poseKeyName = animName;
            _cachedGeneratedPoseKey = mode + "_" + animName;
            return _cachedGeneratedPoseKey;
        }

        internal bool ShouldLogNormal() => _cfgEnableNormalLog != null && _cfgEnableNormalLog.Value;
        internal bool ShouldLogDetailed() => _cfgEnableDetailedLog != null && _cfgEnableDetailedLog.Value;
        internal bool ShouldLogError() => _cfgEnableErrorLog != null && _cfgEnableErrorLog.Value;

        private void ConfigureHandyClient()
        {
            bool useV3 = _cfgUseV3 != null && _cfgUseV3.Value;
            string token = _cfgV3Token != null ? _cfgV3Token.Value : "";
            string connKey = GetConnectionKey();

            if (useV3 && !string.IsNullOrWhiteSpace(token))
            {
                _handyClient.ConfigureV3(token, connKey);
                SimpleFileLogger.Info($"[connect] v3 設定完了 key={(connKey.Length>0 ? connKey : "(空)")} "
                    + $"token={(token.Length>0 ? token.Length + "文字" : "(空)")} "
                    + $"hamp={(_cfgHampEnabled != null && _cfgHampEnabled.Value ? "ON" : "OFF")}");
            }
            else
            {
                _handyClient.ConfigureV2(connKey);
                SimpleFileLogger.Info($"[connect] v2 フォールバック key={(connKey.Length>0 ? connKey : "(空)")}"
                    + " ※v3トークンが空かUseV3=false");
            }
        }
    }
}
