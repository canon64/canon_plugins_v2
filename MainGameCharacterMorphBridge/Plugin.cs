using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameCharacterMorphBridge
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string Guid = "com.kks.maingame.charactermorphbridge";
        public const string PluginName = "MainGameCharacterMorphBridge";
        public const string Version = "0.1.0";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static Plugin Instance { get; private set; }

        private string _pluginDir;
        private string _logPath;
        private Harmony _harmony;
        private PluginSettings _settings;
        private HSceneProc _currentHScene;
        private MorphSnapshot _originalSnapshot;
        private MorphSnapshot _targetSnapshot;
        private int _originalFemaleIndex = -1;
        private int _targetFemaleIndex = -1;
        private string _targetCardPath = string.Empty;

        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgEnableLogs;
        private ConfigEntry<bool> _cfgAutoCaptureOriginal;
        private ConfigEntry<bool> _cfgAutoLoadOnHSceneStart;
        private ConfigEntry<bool> _cfgAutoResetBlendOnHSceneStart;
        private ConfigEntry<int> _cfgTargetFemaleIndex;
        private ConfigEntry<string> _cfgTargetCardPath;
        private ConfigEntry<string> _cfgSelectedCardWord;
        private ConfigEntry<string> _cfgSelectedCardTriggerWords;
        private ConfigEntry<bool> _cfgCaptureOriginalButton;
        private ConfigEntry<bool> _cfgLoadTargetCardButton;
        private ConfigEntry<bool> _cfgResetOriginalButton;
        private ConfigEntry<float> _cfgBlend;
        private ConfigEntry<float> _cfgHeight;
        private ConfigEntry<float> _cfgBreast;
        private ConfigEntry<int> _cfgBodyShapeIndex;
        private ConfigEntry<float> _cfgBodyShapeValue;
        private ConfigEntry<int> _cfgFaceShapeIndex;
        private ConfigEntry<float> _cfgFaceShapeValue;
        private ConfigEntry<bool> _cfgMorphColorEnabled;
        private ConfigEntry<bool> _cfgMorphColorHair;
        private ConfigEntry<bool> _cfgMorphHairStyleEnabled;
        private ConfigEntry<bool> _cfgMorphColorFace;
        private ConfigEntry<bool> _cfgMorphColorBody;
        private ConfigEntry<bool> _cfgMorphOverlaySwapEnabled;
        private ConfigEntry<bool> _cfgMorphColorMakeup;
        private ConfigEntry<float> _cfgMorphColorIntervalSeconds;
        private ConfigEntry<MorphEasingMode> _cfgMorphEasing;
        private ConfigEntry<float> _cfgMorphEasingPower;
        private ConfigEntry<bool> _cfgMorphDurationOverrideEnabled;
        private ConfigEntry<float> _cfgMorphDurationSeconds;
        private ConfigEntry<bool> _cfgMorphGlowEnabled;
        private ConfigEntry<float> _cfgMorphGlowMaxScale;
        private ConfigEntry<float> _cfgMorphGlowFadeInSeconds;
        private ConfigEntry<float> _cfgMorphGlowFadeOutSeconds;
        private ConfigEntry<float> _cfgMorphGlowThreshold;
        private ConfigEntry<float> _cfgMorphGlowStrength;
        private ConfigEntry<bool> _cfgMorphSoundEnabled;
        private ConfigEntry<string> _cfgMorphSoundPath;
        private ConfigEntry<float> _cfgMorphSoundVolume;
        private ConfigEntry<float> _cfgMorphSoundGain;
        private ConfigEntry<bool> _cfgMorphSoundTestButton;

        private void Awake()
        {
            Instance = this;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            _logPath = Path.Combine(_pluginDir, PluginName + ".log");
            _settings = SettingsStore.LoadOrCreate(_pluginDir, null, LogWarnAlways, LogErrorAlways);

            BindConfig();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(HSceneHooks));
            _harmony.PatchAll(typeof(ChaControlVisualRefreshHooks));
            LogExecution("plugin loaded");
        }

        private void OnDisable()
        {
            SaveSettings("plugin disabled");
        }

        private void OnDestroy()
        {
            try
            {
                EndMorphGlow();
                _harmony?.UnpatchSelf();
                _harmony = null;
                SaveSettings("plugin destroyed");
                LogExecution("plugin destroyed");
            }
            finally
            {
                if (ReferenceEquals(Instance, this))
                    Instance = null;
            }
        }

        private void BindConfig()
        {
            _cfgEnabled = Config.Bind(
                "00 General",
                "00 Enable",
                _settings.Enabled,
                BuildConfigDescription("プラグインの有効/無効。OFFにした時点でJSONへ保存します。", 1000));
            _cfgEnabled.SettingChanged += OnEnabledChanged;

            _cfgEnableLogs = Config.Bind(
                "00 General",
                "01 EnableLogs",
                _settings.EnableLogs,
                BuildConfigDescription("分岐と実行時だけログを出します。毎フレームログは出しません。", 990));
            _cfgEnableLogs.SettingChanged += OnEnableLogsChanged;
            _settings.EnableLogs = _cfgEnableLogs.Value;

            _cfgTargetFemaleIndex = Config.Bind(
                "10 Target",
                "00 Target Female Index",
                _settings.TargetFemaleIndex,
                BuildIntSliderDescription("Hシーン女性Index。通常は0、3P時の2人目は1。", 900, 0, 1, DrawTargetFemaleIndexSlider));

            _cfgAutoLoadOnHSceneStart = Config.Bind(
                "10 Target",
                "01 Auto Load On HScene Start",
                _settings.AutoLoadOnHSceneStart,
                BuildConfigDescription("Hシーン開始時に元キャラ数値と補間先カード数値を自動取得します。", 895));
            _cfgAutoLoadOnHSceneStart.SettingChanged += (_, __) => _settings.AutoLoadOnHSceneStart = _cfgAutoLoadOnHSceneStart.Value;

            _cfgAutoCaptureOriginal = Config.Bind(
                "10 Target",
                "02 Auto Capture Original",
                _settings.AutoCaptureOriginal,
                BuildConfigDescription("カード数値を読み込む前に、現在の元キャラ数値を自動保存します。", 890));
            _cfgAutoCaptureOriginal.SettingChanged += (_, __) => _settings.AutoCaptureOriginal = _cfgAutoCaptureOriginal.Value;

            _cfgAutoResetBlendOnHSceneStart = Config.Bind(
                "10 Target",
                "03 Auto Reset Blend On HScene Start",
                _settings.AutoResetBlendOnHSceneStart,
                BuildConfigDescription("Hシーン開始時の自動取得後、Blendを0に戻して元キャラ状態で待機します。", 885));
            _cfgAutoResetBlendOnHSceneStart.SettingChanged += (_, __) => _settings.AutoResetBlendOnHSceneStart = _cfgAutoResetBlendOnHSceneStart.Value;

            _cfgTargetCardPath = Config.Bind(
                "10 Target",
                "04 Target Card Path",
                _settings.TargetCardPath ?? string.Empty,
                BuildConfigDescription("手動で変身させる時のカードPNGのフルパス。合言葉ごとのカードはVoiceFaceEventBridge側で設定します。", 880));
            _cfgTargetCardPath.SettingChanged += (_, __) => _settings.TargetCardPath = PluginSettings.NormalizePath(_cfgTargetCardPath.Value);

            _cfgCaptureOriginalButton = Config.Bind(
                "10 Target",
                "05 Capture Original",
                false,
                BuildButtonDescription("現在の元キャラ数値を記憶", 870, _ => CaptureOriginalFromConfig()));

            _cfgLoadTargetCardButton = Config.Bind(
                "10 Target",
                "06 Load Target Card Values",
                false,
                BuildButtonDescription("カード数値を読み込む", 860, _ => LoadTargetCardFromConfig()));

            _cfgResetOriginalButton = Config.Bind(
                "10 Target",
                "07 Reset To Original",
                false,
                BuildButtonDescription("元キャラ数値へ戻す", 850, _ => ResetToOriginalFromConfig()));

            _cfgBlend = Config.Bind(
                "20 Blend",
                "00 Blend",
                _settings.Blend,
                BuildFloatSliderDescription("0=元キャラ、1=指定カード。ドラッグ中も反映し、ログは離した時だけ出します。", 800, 0f, 1f, "0.00", DrawBlendSlider));

            _cfgHeight = Config.Bind(
                "30 Direct Body",
                "00 Height",
                _settings.Height,
                BuildFloatSliderDescription("shapeValueBody[0] 身長。ドラッグ中もheightとMotionIKを更新します。", 700, 0f, 1f, "0.00", DrawHeightSlider));

            _cfgBreast = Config.Bind(
                "30 Direct Body",
                "01 Breast",
                _settings.Breast,
                BuildFloatSliderDescription("shapeValueBody[4] 胸サイズ。ドラッグ中もBreastとMotionIKを更新します。", 690, 0f, 1f, "0.00", DrawBreastSlider));

            _cfgBodyShapeIndex = Config.Bind(
                "30 Direct Body",
                "10 Body Shape Index",
                _settings.BodyShapeIndex,
                BuildIntSliderDescription("直接変更するbody shape index。0..43。", 680, 0, 43, DrawBodyShapeIndexSlider));

            _cfgBodyShapeValue = Config.Bind(
                "30 Direct Body",
                "11 Body Shape Value",
                _settings.BodyShapeValue,
                BuildFloatSliderDescription("選択中body shape indexへドラッグ中も反映します。", 670, 0f, 1f, "0.00", DrawBodyShapeValueSlider));

            _cfgFaceShapeIndex = Config.Bind(
                "40 Direct Face",
                "00 Face Shape Index",
                _settings.FaceShapeIndex,
                BuildIntSliderDescription("直接変更するface shape index。0..51。", 600, 0, 51, DrawFaceShapeIndexSlider));

            _cfgFaceShapeValue = Config.Bind(
                "40 Direct Face",
                "01 Face Shape Value",
                _settings.FaceShapeValue,
                BuildFloatSliderDescription("選択中face shape indexへドラッグ中も反映します。", 590, 0f, 1f, "0.00", DrawFaceShapeValueSlider));

            _cfgMorphDurationOverrideEnabled = Config.Bind(
                "45 Morph Duration",
                "00 Override Duration",
                _settings.MorphDurationOverrideEnabled,
                BuildConfigDescription("変身にかける秒数をここで決めます。OFFなら呼び出し側（VoiceFaceEventBridgeのTransitionSeconds）の値を使います。", 520));
            _cfgMorphDurationOverrideEnabled.SettingChanged += (_, __) => _settings.MorphDurationOverrideEnabled = _cfgMorphDurationOverrideEnabled.Value;

            _cfgMorphDurationSeconds = Config.Bind(
                "45 Morph Duration",
                "01 Duration Seconds",
                _settings.MorphDurationSeconds,
                BuildFloatSliderDescription("変身の全体秒数。上の上書きがONの時だけ効きます。戻す時にも同じ秒数を使います。", 510, 0f, 60f, "0.00", null));
            _cfgMorphDurationSeconds.SettingChanged += (_, __) => _settings.MorphDurationSeconds = PluginSettings.Round2(PluginSettings.ClampFloat(_cfgMorphDurationSeconds.Value, 0f, 60f));

            _cfgMorphColorEnabled = Config.Bind(
                "47 Morph Color",
                "00 Blend Colors",
                _settings.MorphColorEnabled,
                BuildConfigDescription("髪や肌の色もカードの色へ補間します。髪型や服の種類は変わりません。", 495));
            _cfgMorphColorEnabled.SettingChanged += (_, __) => _settings.MorphColorEnabled = _cfgMorphColorEnabled.Value;

            _cfgMorphColorHair = Config.Bind(
                "47 Morph Color",
                "01 Hair",
                _settings.MorphColorHair,
                BuildConfigDescription("髪の色・光沢色・輪郭色・髪アクセ色。", 494));
            _cfgMorphColorHair.SettingChanged += (_, __) => _settings.MorphColorHair = _cfgMorphColorHair.Value;

            _cfgMorphHairStyleEnabled = Config.Bind(
                "47 Morph Color",
                "01b Hair Style (Mesh Swap)",
                _settings.MorphHairStyleEnabled,
                BuildConfigDescription("変身の中間点で髪型そのものをカードの髪へ差し替える。髪だけ作り直すのでモーフと肌色は保たれるが、一瞬引っかかることがある。", 493));
            _cfgMorphHairStyleEnabled.SettingChanged += (_, __) => _settings.MorphHairStyleEnabled = _cfgMorphHairStyleEnabled.Value;

            _cfgMorphColorFace = Config.Bind(
                "47 Morph Color",
                "02 Face",
                _settings.MorphColorFace,
                BuildConfigDescription("眉・アイライン・アイシャドウ・唇の色。", 493));
            _cfgMorphColorFace.SettingChanged += (_, __) => _settings.MorphColorFace = _cfgMorphColorFace.Value;

            _cfgMorphColorBody = Config.Bind(
                "47 Morph Color",
                "03 Skin",
                _settings.MorphColorBody,
                BuildConfigDescription("肌の色・影の色・日焼け・乳首・下の毛・肌の光沢。", 492));
            _cfgMorphColorBody.SettingChanged += (_, __) => _settings.MorphColorBody = _cfgMorphColorBody.Value;

            _cfgMorphOverlaySwapEnabled = Config.Bind(
                "47 Morph Color",
                "03b Skin Overlay (Tattoo) Swap",
                _settings.MorphOverlaySwapEnabled,
                BuildConfigDescription("変身の中間点でKSOXの肌上絵(タトゥー)をカードのものへ差し替える。顔を覆う上絵を持つカードでは肌色の変化が隠れる。", 491));
            _cfgMorphOverlaySwapEnabled.SettingChanged += (_, __) => _settings.MorphOverlaySwapEnabled = _cfgMorphOverlaySwapEnabled.Value;

            _cfgMorphColorMakeup = Config.Bind(
                "47 Morph Color",
                "03c Makeup / Paint",
                _settings.MorphColorMakeup,
                BuildConfigDescription("アイシャドウ・チーク・リップと、顔/体のペイント。色とレイアウトは補間し、テクスチャ番号は変身の中間で切り替えます。", 490));
            _cfgMorphColorMakeup.SettingChanged += (_, __) => _settings.MorphColorMakeup = _cfgMorphColorMakeup.Value;

            _cfgMorphColorIntervalSeconds = Config.Bind(
                "47 Morph Color",
                "04 Update Interval Seconds",
                _settings.MorphColorIntervalSeconds,
                BuildFloatSliderDescription("色を塗り直す間隔。重い場合は上げてください。0で毎フレーム。", 491, 0f, 2f, "0.00", null));
            _cfgMorphColorIntervalSeconds.SettingChanged += (_, __) => _settings.MorphColorIntervalSeconds = PluginSettings.Round2(PluginSettings.ClampFloat(_cfgMorphColorIntervalSeconds.Value, 0f, 2f));

            _cfgMorphEasing = Config.Bind(
                "46 Morph Easing",
                "00 Easing",
                _settings.MorphEasing,
                BuildConfigDescription("数値変化のかかり方。EaseInOut=最初に加速して最後に減速。Linear=等速。", 505));
            _cfgMorphEasing.SettingChanged += (_, __) => _settings.MorphEasing = _cfgMorphEasing.Value;

            _cfgMorphEasingPower = Config.Bind(
                "46 Morph Easing",
                "01 Easing Power",
                _settings.MorphEasingPower,
                BuildFloatSliderDescription("加減速の強さ。1.0で実質リニア、大きいほど出だしと終わりがゆっくりになります。", 500, 1f, 5f, "0.00", null));
            _cfgMorphEasingPower.SettingChanged += (_, __) => _settings.MorphEasingPower = PluginSettings.Round2(PluginSettings.ClampFloat(_cfgMorphEasingPower.Value, 1f, 5f));

            _cfgMorphGlowEnabled = Config.Bind(
                "50 Morph Glow",
                "00 Glow On Morph",
                _settings.MorphGlowEnabled,
                BuildConfigDescription("Blend遷移中にMainGameGlowを焚きます。変身する本人以外は光りません。MainGameGlowが無ければ何もしません。", 500));
            _cfgMorphGlowEnabled.SettingChanged += (_, __) =>
            {
                _settings.MorphGlowEnabled = _cfgMorphGlowEnabled.Value;
                if (!_settings.MorphGlowEnabled)
                    EndMorphGlow();
            };

            _cfgMorphGlowMaxScale = Config.Bind(
                "50 Morph Glow",
                "01 Glow Max Scale",
                _settings.MorphGlowMaxScale,
                BuildFloatSliderDescription("変身中の一番強い時の発光量。MainGameGlow側の設定値に対する倍率です。", 490, 0f, 1f, "0.00", null));
            _cfgMorphGlowMaxScale.SettingChanged += (_, __) => _settings.MorphGlowMaxScale = PluginSettings.Round2(PluginSettings.Clamp01(_cfgMorphGlowMaxScale.Value));

            _cfgMorphGlowFadeInSeconds = Config.Bind(
                "50 Morph Glow",
                "02 Glow Fade In Seconds",
                _settings.MorphGlowFadeInSeconds,
                BuildFloatSliderDescription("光り始めてから最大になるまでの秒数。0なら即最大。", 480, 0f, 30f, "0.00", null));
            _cfgMorphGlowFadeInSeconds.SettingChanged += (_, __) => _settings.MorphGlowFadeInSeconds = PluginSettings.Round2(PluginSettings.ClampFloat(_cfgMorphGlowFadeInSeconds.Value, 0f, 30f));

            _cfgMorphGlowFadeOutSeconds = Config.Bind(
                "50 Morph Glow",
                "03 Glow Fade Out Seconds",
                _settings.MorphGlowFadeOutSeconds,
                BuildFloatSliderDescription("変身の終わりぎわに光を引かせる秒数。変身完了と同時に0になります。", 470, 0f, 30f, "0.00", null));
            _cfgMorphGlowFadeOutSeconds.SettingChanged += (_, __) => _settings.MorphGlowFadeOutSeconds = PluginSettings.Round2(PluginSettings.ClampFloat(_cfgMorphGlowFadeOutSeconds.Value, 0f, 30f));

            _cfgMorphGlowThreshold = Config.Bind(
                "50 Morph Glow",
                "04 Glow Threshold",
                _settings.MorphGlowThreshold,
                BuildFloatSliderDescription("この明るさを超えた所が光ります。下げるほど暗い部分まで光ります。変身中だけMainGameGlowの値を上書きします。", 460, 0f, 5f, "0.00", null));
            _cfgMorphGlowThreshold.SettingChanged += (_, __) => _settings.MorphGlowThreshold = PluginSettings.Round2(PluginSettings.ClampFloat(_cfgMorphGlowThreshold.Value, 0f, 5f));

            _cfgMorphGlowStrength = Config.Bind(
                "50 Morph Glow",
                "05 Glow Strength",
                _settings.MorphGlowStrength,
                BuildFloatSliderDescription("発光の強さ。変身中だけMainGameGlowの値を上書きします。", 450, 0f, 10f, "0.00", null));
            _cfgMorphGlowStrength.SettingChanged += (_, __) => _settings.MorphGlowStrength = PluginSettings.Round2(PluginSettings.ClampFloat(_cfgMorphGlowStrength.Value, 0f, 10f));

            _cfgMorphSoundEnabled = Config.Bind(
                "60 Morph Sound",
                "00 Play Sound On Morph",
                _settings.MorphSoundEnabled,
                BuildConfigDescription("変身の開始に合わせて効果音を鳴らします。", 400));
            _cfgMorphSoundEnabled.SettingChanged += (_, __) =>
            {
                _settings.MorphSoundEnabled = _cfgMorphSoundEnabled.Value;
                if (!_settings.MorphSoundEnabled)
                    StopMorphSound();
            };

            _cfgMorphSoundPath = Config.Bind(
                "60 Morph Sound",
                "01 Sound File Path",
                _settings.MorphSoundPath ?? string.Empty,
                BuildConfigDescription("効果音ファイル。相対パスはプラグインフォルダ基準（既定は sound\\ の中）。wav推奨、mp3はUnityが再生できません。", 390));
            _cfgMorphSoundPath.SettingChanged += (_, __) => _settings.MorphSoundPath = PluginSettings.NormalizePath(_cfgMorphSoundPath.Value);

            _cfgMorphSoundVolume = Config.Bind(
                "60 Morph Sound",
                "02 Sound Volume",
                _settings.MorphSoundVolume,
                BuildFloatSliderDescription("効果音の音量。", 380, 0f, 1f, "0.00", null));
            _cfgMorphSoundVolume.SettingChanged += (_, __) => _settings.MorphSoundVolume = PluginSettings.Round2(PluginSettings.Clamp01(_cfgMorphSoundVolume.Value));

            _cfgMorphSoundGain = Config.Bind(
                "60 Morph Sound",
                "03 Sound Gain",
                _settings.MorphSoundGain,
                BuildFloatSliderDescription("音量が1.0でも足りない時の増幅。1.0で等倍。上げすぎると歪みます。", 370, 0f, 8f, "0.00", null));
            _cfgMorphSoundGain.SettingChanged += (_, __) => _settings.MorphSoundGain = PluginSettings.Round2(PluginSettings.ClampFloat(_cfgMorphSoundGain.Value, 0f, 8f));

            _cfgMorphSoundTestButton = Config.Bind(
                "60 Morph Sound",
                "04 Test Play",
                false,
                BuildButtonDescription("今の設定で鳴らしてみる", 360, _ => ForcePlayMorphSound()));
        }

        private ConfigDescription BuildConfigDescription(string description, int order)
        {
            return new ConfigDescription(
                description,
                null,
                new ConfigurationManager.ConfigurationManagerAttributes
                {
                    Order = order
                });
        }

        private void OnEnabledChanged(object sender, EventArgs e)
        {
            _settings.Enabled = _cfgEnabled.Value;
            LogExecution("Enable changed: " + _settings.Enabled);
            if (!_settings.Enabled)
                SaveSettings("disabled by config");
        }

        private void OnEnableLogsChanged(object sender, EventArgs e)
        {
            _settings.EnableLogs = _cfgEnableLogs.Value;
            LogExecution("EnableLogs changed: " + _settings.EnableLogs);
        }

        private bool IsEnabled()
        {
            return _settings != null && _settings.Enabled;
        }

        private int TargetFemaleIndex()
        {
            return PluginSettings.ClampInt(_settings.TargetFemaleIndex, 0, 1);
        }

        private void SaveSettings(string reason)
        {
            if (_settings == null || string.IsNullOrEmpty(_pluginDir))
                return;

            SettingsStore.Save(_pluginDir, _settings, LogWarnAlways);
            LogExecution("settings saved: " + reason);
        }

        internal void SetCurrentHScene(HSceneProc proc)
        {
            if (proc == null || ReferenceEquals(_currentHScene, proc))
                return;

            _currentHScene = proc;
            _originalSnapshot = null;
            _targetSnapshot = null;
            _originalFemaleIndex = -1;
            _targetFemaleIndex = -1;
            ResetAnimationObservation();
            ClearDirectOverrideState();
            LogExecution("HScene detected");
            StartHSceneAutoInitialize(proc);
        }

        internal void ClearCurrentHScene(HSceneProc proc)
        {
            if (!ReferenceEquals(_currentHScene, proc))
                return;

            _currentHScene = null;
            _originalSnapshot = null;
            _targetSnapshot = null;
            _originalFemaleIndex = -1;
            _targetFemaleIndex = -1;
            StopHSceneAutoInitialize();
            StopBlendTransition();
            StopMorphSound();
            StopPostMotionReapply();
            StopExternalReapply();
            StopClothesChangeReapply();
            ResetAnimationObservation();
            ClearDirectOverrideState();
            LogExecution("HScene cleared");
        }

        internal void LogExecution(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (_settings == null || !_settings.EnableLogs)
                return;

            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message;
            Logger.LogInfo("[" + PluginName + "] " + message);
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine, Utf8NoBom);
            }
            catch
            {
            }
        }

        internal void LogWarnAlways(string message)
        {
            Logger.LogWarning("[" + PluginName + "] " + message);
        }

        internal void LogErrorAlways(string message)
        {
            Logger.LogError("[" + PluginName + "] " + message);
        }

        private static class HSceneHooks
        {
            [HarmonyPatch(typeof(HSceneProc), "Start")]
            [HarmonyPostfix]
            private static void StartPostfix(HSceneProc __instance)
            {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
                Instance?.SetCurrentHScene(__instance);
            }

            [HarmonyPatch(typeof(HSceneProc), "OnDestroy")]
            [HarmonyPrefix]
            private static void OnDestroyPrefix(HSceneProc __instance)
            {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
                Instance?.ClearCurrentHScene(__instance);
            }
        }

        [HarmonyPatch]
        private static class ChaControlVisualRefreshHooks
        {
            private static readonly HashSet<string> TargetNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "ChangeCoordinateTypeAndReload",
                "Reload",
                "SetClothesState",
                "SetClothesStatePrev",
                "SetClothesStateNext",
                "SetClothesStateAll",
                "UpdateClothesStateAll",
                "RandomChangeOfClothesLowPoly",
                "RandomChangeOfClothesLowPolyEnd",
                "ChangeToiletStateLowPoly"
            };

            private static IEnumerable<MethodBase> TargetMethods()
            {
                return AccessTools.GetDeclaredMethods(typeof(ChaControl))
                    .Where(m => m != null && TargetNames.Contains(m.Name));
            }

            private static void Postfix(ChaControl __instance, MethodBase __originalMethod)
            {
                // BlankMapAdd の追加マップ以外では発火させない。
                if (!BlankRoomGate.IsInBlankRoom) return;

                // ここから例外を外へ出すと、ChaControl.Reload を呼んでいるゲーム側
                // （キャラクリなど）ごと止まる。何があっても飲み込む。
                try
                {
                    string methodName = __originalMethod != null ? __originalMethod.Name : "unknown";
                    Instance?.ScheduleChaControlVisualRefreshReapply(__instance, methodName);
                }
                catch (Exception ex)
                {
                    Instance?.LogWarnAlways("visual refresh hook failed: " + ex.Message);
                }
            }
        }
    }
}
