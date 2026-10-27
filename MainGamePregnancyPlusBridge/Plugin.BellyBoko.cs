using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGamePregnancyPlusBridge
{
    public sealed partial class Plugin
    {
        private const string MotionStrengthStrong = "strong";
        private const string MotionStrengthWeak = "weak";
        private const string MotionStrengthUnknown = "unknown";

        private ConfigEntry<bool> _cfgBellyEnabled;
        private ConfigEntry<float> _cfgBellyMinInflationSize;
        private ConfigEntry<float> _cfgBellyMaxInflationSize;
        private ConfigEntry<float> _cfgBellyDistanceCutPercent;
        private ConfigEntry<float> _cfgBellyDistanceMinMeters;
        private ConfigEntry<float> _cfgBellyDistanceMaxMeters;
        private ConfigEntry<float> _cfgBellyDistanceSmoothing;
        private ConfigEntry<int> _cfgBellyDistanceAnalyzeTurns;
        private ConfigEntry<bool> _cfgBellyDistanceAnalyzeNow;
        private ConfigEntry<KeyboardShortcut> _cfgBellyDistanceAnalyzeKey;
        private ConfigEntry<string> _cfgBellyEaseUp;
        private ConfigEntry<string> _cfgBellyEaseDown;
        private ConfigEntry<string> _cfgBellyContext;
        private ConfigEntry<string> _cfgBellyMotionStrength;
        private string _bellyContextDisplay = "(no-context)";
        private string _bellyMotionStrengthDisplay = MotionStrengthUnknown;

        private BellyBokoStore _bellyStore;
        private BellyBokoProfile _bellyFallbackProfile;

        private bool _hasRuntimeInflationSizeOverride;
        private float _runtimeInflationSizeOverride;
        private string _bellyCurrentAnimationKey;
        private float _bellyDistanceSmoothed;
        private bool _hasBellyDistanceSmoothed;
        private float _bellyDistancePrev;
        private bool _hasBellyDistancePrev;
        private bool _bellyDistanceApproachingPrev;
        private bool _hasBellyDistanceApproachingPrev;
        private float _bellyNormalizedWeightPrev;
        private bool _hasBellyNormalizedWeightPrev;
        private bool _bellyDistanceAnalyzeActive;
        private int _bellyDistanceAnalyzeTargetTurns;
        private int _bellyDistanceAnalyzeCompletedTurns;
        private float _bellyDistanceAnalyzeMin;
        private float _bellyDistanceAnalyzeMax;
        private float _bellyDistanceAnalyzeLastPhase;
        private bool _hasBellyDistanceAnalyzeLastPhase;
        private string _bellyDistanceAnalyzeKey;
        private bool _bellyDistanceAnalyzeTriggerGuard;
        private HSceneProc _bellyHSceneProc;
        private HSceneProc _bellyFallbackSceneProc;
        private ChaControl _bellyFallbackFemale;
        private ChaControl _bellyFallbackMale;
        private float _nextBellyHScanTime;
        private float _nextBellyDiagLogTime;
        private string _lastBellyDiagGate = string.Empty;
        private ChaControl _bellyMaleCha;
        private ChaControl _bellyFemaleCha;
        private Transform _bellyMaleDistanceRef;
        private Transform _bellyFemaleDistanceRef;
        private readonly List<AnimatorClipInfo> _bellyClipInfos = new List<AnimatorClipInfo>(2);
        private readonly Dictionary<Type, FieldInfo> _bellyLstFemaleFieldCache = new Dictionary<Type, FieldInfo>();
        private readonly Dictionary<Type, MemberInfo> _bellyFlagsMemberCache = new Dictionary<Type, MemberInfo>();
        private readonly Dictionary<Type, MemberInfo> _bellyNowAnimMemberCache = new Dictionary<Type, MemberInfo>();
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> BellyMemberCache =
            new Dictionary<Type, Dictionary<string, MemberInfo>>();
        private bool _bellyContextTextCached;
        private int _bellyCachedPostureId, _bellyCachedPostureMode, _bellyCachedStateHash;
        private string _bellyCachedPostureName, _bellyCachedMotionStrength;
        private string _bellyCachedAnimationKey, _bellyCachedShortKey, _bellyCachedDisplayText;
        private float _nextBellyDisplayTextTime;

        private static ConfigurationManager.ConfigurationManagerAttributes BellyUiOrder(int order)
        {
            return new ConfigurationManager.ConfigurationManagerAttributes { Order = order };
        }

        private void InitializeBellyBokoSystem(string pluginDir)
        {
            string storePath = System.IO.Path.Combine(pluginDir, "MainGamePregnancyPlusBridgeBellyProfiles.json");
            _bellyStore = new BellyBokoStore(storePath, LogInfo, LogWarn);

            _cfgBellyEnabled = Config.Bind(
                "10.BellyBoko",
                "Enabled",
                true,
                new ConfigDescription("腹ボコ機能の有効/無効", null, BellyUiOrder(999)));
            _cfgBellyMinInflationSize = Config.Bind(
                "10.BellyBoko",
                "MinInflationSize",
                0f,
                new ConfigDescription("腹ボコ最小InflationSize", new AcceptableValueRange<float>(0f, 40f), BellyUiOrder(984)));
            _cfgBellyMaxInflationSize = Config.Bind(
                "10.BellyBoko",
                "MaxInflationSize",
                5f,
                new ConfigDescription("腹ボコ最大InflationSize", new AcceptableValueRange<float>(0f, 40f), BellyUiOrder(983)));
            _cfgBellyDistanceCutPercent = Config.Bind(
                "10.BellyBoko",
                "DistanceCutPercent",
                0.9f,
                new ConfigDescription("最大→最小へ落とし切る距離割合（0..1）", new AcceptableValueRange<float>(0f, 1f), BellyUiOrder(977)));
            _cfgBellyDistanceMinMeters = Config.Bind(
                "10.BellyBoko",
                "DistanceMinMeters",
                0.04f,
                new ConfigDescription("距離最小点（メートル）", new AcceptableValueRange<float>(0f, 2f), BellyUiOrder(976)));
            _cfgBellyDistanceMaxMeters = Config.Bind(
                "10.BellyBoko",
                "DistanceMaxMeters",
                0.8f,
                new ConfigDescription("距離最大点（メートル）", new AcceptableValueRange<float>(0f, 2f), BellyUiOrder(975)));
            _cfgBellyDistanceSmoothing = Config.Bind(
                "10.BellyBoko",
                "DistanceSmoothing",
                0.8f,
                new ConfigDescription("距離平滑化（0=なし、1=最大）", new AcceptableValueRange<float>(0f, 1f), BellyUiOrder(974)));
            _cfgBellyDistanceAnalyzeTurns = Config.Bind(
                "10.BellyBoko",
                "DistanceAnalyzeTurns",
                10,
                new ConfigDescription("アナライズ往復数", new AcceptableValueRange<int>(1, 20), BellyUiOrder(973)));
            _cfgBellyDistanceAnalyzeNow = Config.Bind(
                "10.BellyBoko",
                "DistanceAnalyzeNow",
                false,
                new ConfigDescription(
                    "現在の体位の距離アナライズを実行（ボタン）",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 972,
                        HideDefaultButton = true,
                        CustomDrawer = DrawBellyDistanceAnalyzeButton
                    }));
            _cfgBellyDistanceAnalyzeKey = Config.Bind(
                "10.BellyBoko",
                "DistanceAnalyzeKey",
                KeyboardShortcut.Empty,
                new ConfigDescription("距離アナライズ起動ショートカット（空欄=無効）", null, BellyUiOrder(971)));
            _cfgBellyEaseUp = Config.Bind(
                "10.BellyBoko",
                "EaseUp",
                "easeOut",
                new ConfigDescription(
                    "入るとき（近づく）のカーブ",
                    new AcceptableValueList<string>("linear", "easeIn", "easeOut", "smoothStep", "smootherStep"),
                    BellyUiOrder(971)));
            _cfgBellyEaseDown = Config.Bind(
                "10.BellyBoko",
                "EaseDown",
                "easeIn",
                new ConfigDescription(
                    "抜けるとき（遠ざかる）のカーブ",
                    new AcceptableValueList<string>("linear", "easeIn", "easeOut", "smoothStep", "smootherStep"),
                    BellyUiOrder(970)));
            _cfgBellyContext = Config.Bind(
                "10.BellyBoko",
                "CurrentContext",
                "(no-context)",
                new ConfigDescription("現在のマッチキー: 体位/強弱/アニメ", null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 969,
                        ReadOnly = true,
                        IsAdvanced = true,
                        CustomDrawer = DrawBellyContext
                    }));
            _cfgBellyMotionStrength = Config.Bind(
                "10.BellyBoko",
                "CurrentStrength",
                MotionStrengthUnknown,
                new ConfigDescription("現在の強/弱分類", null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 968,
                        ReadOnly = true,
                        IsAdvanced = true,
                        CustomDrawer = DrawBellyMotionStrength
                    }));

            _cfgBellyDistanceAnalyzeNow.SettingChanged += OnBellyDistanceAnalyzeRequested;
            _cfgBellyMinInflationSize.SettingChanged += OnBellyEditorValueChanged;
            _cfgBellyMaxInflationSize.SettingChanged += OnBellyEditorValueChanged;
            _cfgBellyDistanceCutPercent.SettingChanged += OnBellyEditorValueChanged;
            _cfgBellyDistanceMinMeters.SettingChanged += OnBellyEditorValueChanged;
            _cfgBellyDistanceMaxMeters.SettingChanged += OnBellyEditorValueChanged;
            _cfgBellyDistanceSmoothing.SettingChanged += OnBellyEditorValueChanged;
            _cfgBellyDistanceAnalyzeTurns.SettingChanged += OnBellyEditorValueChanged;

            _cfgBellyMinInflationSize.Value = Mathf.Clamp(_cfgBellyMinInflationSize.Value, 0f, 40f);
            _cfgBellyMaxInflationSize.Value = Mathf.Clamp(_cfgBellyMaxInflationSize.Value, 0f, 40f);
            _cfgBellyDistanceCutPercent.Value = Mathf.Clamp01(_cfgBellyDistanceCutPercent.Value);
            _cfgBellyDistanceMinMeters.Value = Mathf.Clamp(_cfgBellyDistanceMinMeters.Value, 0f, 2f);
            _cfgBellyDistanceMaxMeters.Value = Mathf.Clamp(_cfgBellyDistanceMaxMeters.Value, 0f, 2f);
            _cfgBellyDistanceSmoothing.Value = Mathf.Clamp01(_cfgBellyDistanceSmoothing.Value);
            _cfgBellyDistanceAnalyzeTurns.Value = Mathf.Clamp(_cfgBellyDistanceAnalyzeTurns.Value, 1, 20);
            _cfgBellyEaseUp.Value = NormalizeEaseName(_cfgBellyEaseUp.Value, "easeOut");
            _cfgBellyEaseDown.Value = NormalizeEaseName(_cfgBellyEaseDown.Value, "easeIn");
        }

        private void DrawBellyDistanceAnalyzeButton(ConfigEntryBase entryBase)
        {
            string label = _bellyDistanceAnalyzeActive ? "ANALYZING..." : "ANALYZE DISTANCE";
            if (GUILayout.Button(label, GUILayout.MinWidth(150f)))
                RequestBellyDistanceAnalyzeNow();
        }

        private void DrawBellyContext(ConfigEntryBase entryBase)
        {
            GUILayout.Label(_bellyContextDisplay);
        }

        private void DrawBellyMotionStrength(ConfigEntryBase entryBase)
        {
            GUILayout.Label(_bellyMotionStrengthDisplay);
        }

        private void OnBellyDistanceAnalyzeRequested(object sender, EventArgs e)
        {
            if (_bellyDistanceAnalyzeTriggerGuard || _cfgBellyDistanceAnalyzeNow == null || !_cfgBellyDistanceAnalyzeNow.Value)
                return;

            try
            {
                RequestBellyDistanceAnalyzeNow();
            }
            finally
            {
                ResetBellyDistanceAnalyzeTrigger();
            }
        }

        private void ResetBellyDistanceAnalyzeTrigger()
        {
            if (_cfgBellyDistanceAnalyzeNow == null)
                return;

            _bellyDistanceAnalyzeTriggerGuard = true;
            try
            {
                _cfgBellyDistanceAnalyzeNow.Value = false;
            }
            finally
            {
                _bellyDistanceAnalyzeTriggerGuard = false;
            }
        }

        private void RequestBellyDistanceAnalyzeNow()
        {
            if (!TryGetBellyContext(out BellyContext context))
            {
                ShowPresetPopup("距離分析失敗: Hシーン文脈を取得できません", true);
                return;
            }

            int turns = Mathf.Clamp(_cfgBellyDistanceAnalyzeTurns != null ? _cfgBellyDistanceAnalyzeTurns.Value : 10, 1, 20);
            _bellyDistanceAnalyzeActive = true;
            _bellyDistanceAnalyzeTargetTurns = turns;
            _bellyDistanceAnalyzeCompletedTurns = 0;
            _bellyDistanceAnalyzeMin = float.MaxValue;
            _bellyDistanceAnalyzeMax = 0f;
            _bellyDistanceAnalyzeLastPhase = context.Phase;
            _hasBellyDistanceAnalyzeLastPhase = true;
            _bellyDistanceAnalyzeKey = context.AnimationKey ?? string.Empty;
            _hasBellyDistanceSmoothed = false;

            ShowPresetPopup("距離分析開始: " + turns + "ターン", false);
            LogInfo("belly distance analysis start key=" + (context.AnimationKey ?? string.Empty) + " turns=" + turns);
        }

        private void OnBellyEditorValueChanged(object sender, EventArgs e)
        {
            _cfgBellyDistanceCutPercent.Value = Mathf.Clamp01(_cfgBellyDistanceCutPercent.Value);
            _cfgBellyDistanceMinMeters.Value = Mathf.Clamp(_cfgBellyDistanceMinMeters.Value, 0f, 2f);
            _cfgBellyDistanceMaxMeters.Value = Mathf.Clamp(_cfgBellyDistanceMaxMeters.Value, 0f, 2f);
            _cfgBellyDistanceSmoothing.Value = Mathf.Clamp01(_cfgBellyDistanceSmoothing.Value);
            _cfgBellyDistanceAnalyzeTurns.Value = Mathf.Clamp(_cfgBellyDistanceAnalyzeTurns.Value, 1, 20);
            _dirty = true;
        }

        private bool UpdateBellyBokoRuntime()
        {
            bool hadPreviousOverride = _hasRuntimeInflationSizeOverride;
            float previousSize = _runtimeInflationSizeOverride;
            _hasRuntimeInflationSizeOverride = false;

            if (_cfgBellyDistanceAnalyzeKey != null && Bindings.Down("MainGamePregnancyPlusBridge.analyze", _cfgBellyDistanceAnalyzeKey.Value.ToString()))
                RequestBellyDistanceAnalyzeNow();

            if (!_cfgBellyEnabled.Value)
            {
                ResetBellyPeakTracking();
                LogBellyGate("disabled");
                return false;
            }

            if (!TryGetBellyContext(out BellyContext context))
            {
                ResetBellyPeakTracking();
                LogBellyGate("no-context");
                return false;
            }

            _bellyContextDisplay = context.DisplayText;
            _bellyMotionStrengthDisplay = context.MotionStrength;

            bool isPiston = IsStrongOrWeakMotionStrength(context.MotionStrength);
            if (!isPiston)
            {
                // S_Idleは腹ボコ非適用
                if (IsIdleClip(context.ClipName))
                {
                    ResetBellyPeakTracking();
                    LogBellyGate("idle-skip", context);
                    return false;
                }
                // 強ピストンプロファイルが未取得なら非適用
                if (_bellyFallbackProfile == null)
                {
                    ResetBellyPeakTracking();
                    LogBellyGate("no-fallback-profile", context);
                    return false;
                }
            }

            bool contextChanged = !string.Equals(_bellyCurrentAnimationKey ?? string.Empty, context.AnimationKey ?? string.Empty, StringComparison.Ordinal);
            if (contextChanged)
            {
                LogInfo("belly context changed key=" + context.AnimationKey);
                _bellyCurrentAnimationKey = context.AnimationKey;
                _hasBellyDistanceSmoothed = false;
                _hasBellyDistancePrev = false;
                _hasBellyDistanceApproachingPrev = false;
                _hasBellyNormalizedWeightPrev = false;
                _bellyFallbackProfile = null;
                if (_bellyDistanceAnalyzeActive && !string.Equals(_bellyDistanceAnalyzeKey ?? string.Empty, context.AnimationKey ?? string.Empty, StringComparison.Ordinal))
                {
                    _bellyDistanceAnalyzeActive = false;
                    ShowPresetPopup("距離分析中断: 体位が切り替わりました", true);
                    LogInfo("belly distance analysis aborted reason=context-changed");
                }
                LoadBellyProfile(context);
                LoadBellyFallbackProfile(context);
            }

            float minInflationSize, maxInflationSize, distanceCutPercent, distanceMinMeters, distanceMaxMeters, distanceSmoothing;
            string easeUp, easeDown;
            if (isPiston || _bellyFallbackProfile == null)
            {
                minInflationSize = Mathf.Clamp(_cfgBellyMinInflationSize.Value, 0f, 40f);
                maxInflationSize = Mathf.Clamp(_cfgBellyMaxInflationSize.Value, 0f, 40f);
                distanceCutPercent = Mathf.Clamp01(_cfgBellyDistanceCutPercent != null ? _cfgBellyDistanceCutPercent.Value : 0.9f);
                distanceMinMeters = Mathf.Clamp(_cfgBellyDistanceMinMeters != null ? _cfgBellyDistanceMinMeters.Value : 0.04f, 0f, 2f);
                distanceMaxMeters = Mathf.Clamp(_cfgBellyDistanceMaxMeters != null ? _cfgBellyDistanceMaxMeters.Value : 0.8f, 0f, 2f);
                distanceSmoothing = Mathf.Clamp01(_cfgBellyDistanceSmoothing != null ? _cfgBellyDistanceSmoothing.Value : 0.8f);
                easeUp = NormalizeEaseName(_cfgBellyEaseUp != null ? _cfgBellyEaseUp.Value : "easeOut", "easeOut");
                easeDown = NormalizeEaseName(_cfgBellyEaseDown != null ? _cfgBellyEaseDown.Value : "easeIn", "easeIn");
            }
            else
            {
                // 非ピストンモーション: 強ピストンプロファイルから距離レンジを流用
                minInflationSize = Mathf.Clamp(_bellyFallbackProfile.MinInflationSize, 0f, 40f);
                maxInflationSize = Mathf.Clamp(_bellyFallbackProfile.MaxInflationSize, 0f, 40f);
                distanceCutPercent = Mathf.Clamp01(_bellyFallbackProfile.DistanceCutPercent);
                distanceMinMeters = Mathf.Clamp(_bellyFallbackProfile.DistanceMinMeters, 0f, 2f);
                distanceMaxMeters = Mathf.Clamp(_bellyFallbackProfile.DistanceMaxMeters, 0f, 2f);
                distanceSmoothing = Mathf.Clamp01(_bellyFallbackProfile.DistanceSmoothing);
                easeUp = NormalizeEaseName(_bellyFallbackProfile.EaseUp, "easeOut");
                easeDown = NormalizeEaseName(_bellyFallbackProfile.EaseDown, "easeIn");
            }

            if (!context.HasDistance)
            {
                _runtimeInflationSizeOverride = minInflationSize;
                _hasRuntimeInflationSizeOverride = true;
                ResetBellyPeakTracking();
                LogBellyGate("distance-ref-missing", context);
                return contextChanged || !hadPreviousOverride || previousSize != _runtimeInflationSizeOverride;
            }

            float currentDistance = Mathf.Max(0f, context.Distance);
            if (_hasBellyDistanceSmoothed)
                _bellyDistanceSmoothed = Mathf.Lerp(currentDistance, _bellyDistanceSmoothed, distanceSmoothing);
            else
                _bellyDistanceSmoothed = currentDistance;
            _hasBellyDistanceSmoothed = true;

            // 近づいているか遠ざかっているかで使うカーブを切り替える
            bool approaching = _hasBellyDistancePrev && (_bellyDistanceSmoothed < _bellyDistancePrev);
            string activeEase = approaching ? easeUp : easeDown;
            _bellyDistancePrev = _bellyDistanceSmoothed;
            _hasBellyDistancePrev = true;

            float evalDistance = _bellyDistanceSmoothed;
            EnsureDistanceRange(ref distanceMinMeters, ref distanceMaxMeters);
            float normalizedWeight = EvaluateDistanceWeight(evalDistance, distanceMinMeters, distanceMaxMeters, distanceCutPercent, activeEase);
            UpdateDistanceAnalysis(context, currentDistance, evalDistance);

            float targetSize = Mathf.Lerp(minInflationSize, maxInflationSize, normalizedWeight);
            _runtimeInflationSizeOverride = Mathf.Clamp(targetSize, 0f, 40f);
            _hasRuntimeInflationSizeOverride = true;

            // ピーク検出: 前フレーム approaching=true → 今フレーム approaching=false の遷移で発火
            // (距離が極小に達した = 男側が突き当たった瞬間 = 腹ボコ最大の瞬間)
            bool wasApproaching = _bellyDistanceApproachingPrev;
            bool hadApproachingPrev = _hasBellyDistanceApproachingPrev;
            bool hadWeightPrev = _hasBellyNormalizedWeightPrev;
            float prevWeight = _bellyNormalizedWeightPrev;

            _bellyDistanceApproachingPrev = approaching;
            _hasBellyDistanceApproachingPrev = true;
            _bellyNormalizedWeightPrev = normalizedWeight;
            _hasBellyNormalizedWeightPrev = true;

            NotifyBellyBokoIntensity(normalizedWeight);
            if (hadApproachingPrev && wasApproaching && !approaching && hadWeightPrev)
            {
                NotifyBellyBokoPeak(prevWeight);
            }

            LogBellyGate(Mathf.Abs(maxInflationSize - minInflationSize) <= 0.0001f ? "applied-flat-range" : "applied", context);
            LogBellyApplySample(context, normalizedWeight, _runtimeInflationSizeOverride, minInflationSize, maxInflationSize, distanceMinMeters, distanceMaxMeters, activeEase);
            return contextChanged || !hadPreviousOverride || previousSize != _runtimeInflationSizeOverride;
        }

        private void ResetBellyPeakTracking()
        {
            _hasBellyDistanceApproachingPrev = false;
            _hasBellyNormalizedWeightPrev = false;
            ClearBellyBokoIntensity();
        }

        private float GetEffectiveInflationSize()
        {
            return _hasRuntimeInflationSizeOverride
                ? _runtimeInflationSizeOverride
                : Mathf.Clamp(_cfgBellyMinInflationSize != null ? _cfgBellyMinInflationSize.Value : 0f, 0f, 40f);
        }

        private bool TryGetBellyContext(out BellyContext context)
        {
            context = default(BellyContext);

            if (Time.unscaledTime >= _nextBellyHScanTime)
            {
                _nextBellyHScanTime = Time.unscaledTime + 1f;
                if (_bellyHSceneProc == null)
                    _bellyHSceneProc = FindObjectOfType<HSceneProc>();
                else if (!_bellyHSceneProc)
                    _bellyHSceneProc = null;
            }

            if (_bellyHSceneProc == null)
                return false;

            if (_bellyFallbackSceneProc != _bellyHSceneProc)
            {
                _bellyFallbackSceneProc = _bellyHSceneProc;
                _bellyFallbackFemale = null;
                _bellyFallbackMale = null;
            }

            ChaControl female = ResolveMainFemaleForBelly(_bellyHSceneProc);
            if (female == null)
                return false;

            AnimatorStateInfo stateInfo;
            try
            {
                stateInfo = female.getAnimatorStateInfo(0);
            }
            catch
            {
                return false;
            }

            string clipName = string.Empty;
            try
            {
                Animator animBody = female.animBody;
                if (animBody != null)
                {
                    _bellyClipInfos.Clear();
                    animBody.GetCurrentAnimatorClipInfo(0, _bellyClipInfos);
                    if (_bellyClipInfos.Count > 0 && _bellyClipInfos[0].clip != null)
                        clipName = _bellyClipInfos[0].clip.name;
                }
            }
            catch { }

            object nowAnimInfo = GetNowAnimationInfoForBelly(_bellyHSceneProc);
            int postureId = GetIntMemberValueByName(nowAnimInfo, "id", int.MinValue);
            int postureMode = GetIntMemberValueByName(nowAnimInfo, "mode", int.MinValue);
            string postureName = GetStringMemberValueByName(nowAnimInfo, "nameAnimation");

            string motionStrength = ClassifyMotionStrength(clipName);
            int stateHash = stateInfo.fullPathHash;
            float phase = NormalizePhase01(stateInfo.normalizedTime);
            bool hasDistance = TryGetBellyDistance(_bellyHSceneProc, female, out float distanceMeters);
            bool textChanged = !_bellyContextTextCached || _bellyCachedPostureId != postureId ||
                _bellyCachedPostureMode != postureMode || _bellyCachedStateHash != stateHash ||
                !string.Equals(_bellyCachedPostureName, postureName, StringComparison.Ordinal) ||
                !string.Equals(_bellyCachedMotionStrength, motionStrength, StringComparison.Ordinal);
            if (textChanged)
            {
                _bellyContextTextCached = true;
                _bellyCachedPostureId = postureId;
                _bellyCachedPostureMode = postureMode;
                _bellyCachedStateHash = stateHash;
                _bellyCachedPostureName = postureName;
                _bellyCachedMotionStrength = motionStrength;
                _bellyCachedAnimationKey = BuildAnimationKey(postureId, postureMode, postureName, motionStrength, stateHash);
                _bellyCachedShortKey = BuildShortKeyText(postureId, postureMode, postureName, motionStrength);
            }
            string key = _bellyCachedAnimationKey;
            string shortKey = _bellyCachedShortKey;
            if (textChanged || Time.unscaledTime >= _nextBellyDisplayTextTime)
            {
                _nextBellyDisplayTextTime = Time.unscaledTime + 0.25f;
                _bellyCachedDisplayText = hasDistance
                    ? shortKey + " hash=" + stateHash + " dist=" + distanceMeters.ToString("0.000")
                    : shortKey + " hash=" + stateHash + " dist=(none)";
            }

            context = new BellyContext
            {
                PostureId = postureId,
                PostureMode = postureMode,
                PostureName = postureName ?? string.Empty,
                MotionStrength = motionStrength,
                AnimatorStateHash = stateHash,
                Phase = phase,
                Distance = distanceMeters,
                HasDistance = hasDistance,
                AnimationKey = key,
                ClipName = clipName,
                ShortKeyText = shortKey,
                DisplayText = _bellyCachedDisplayText
            };
            return true;
        }

        private ChaControl ResolveMainFemaleForBelly(HSceneProc proc)
        {
            if (proc == null)
                return null;

            Type t = proc.GetType();
            if (!_bellyLstFemaleFieldCache.TryGetValue(t, out FieldInfo fi))
            {
                fi = t.GetField("lstFemale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _bellyLstFemaleFieldCache[t] = fi;
            }

            if (fi != null)
            {
                try
                {
                    var list = fi.GetValue(proc) as System.Collections.IList;
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i] is ChaControl cha && cha != null)
                                return cha;
                        }
                    }
                }
                catch
                {
                    // ignored
                }
            }

            if (_bellyFallbackFemale != null)
                return _bellyFallbackFemale;

            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindNonIndependent();
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha != null && cha.sex == 1)
                    return _bellyFallbackFemale = cha;
            }

            return null;
        }

        private object GetNowAnimationInfoForBelly(HSceneProc proc)
        {
            if (proc == null)
                return null;

            object flags = GetFlagsForBelly(proc);
            if (flags == null)
                return null;

            Type ft = flags.GetType();
            if (!_bellyNowAnimMemberCache.TryGetValue(ft, out MemberInfo member))
            {
                FieldInfo fi = ft.GetField("nowAnimationInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null)
                    member = fi;
                else
                    member = ft.GetProperty("nowAnimationInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _bellyNowAnimMemberCache[ft] = member;
            }

            return ReadMemberValue(flags, member);
        }

        private object GetFlagsForBelly(HSceneProc proc)
        {
            Type t = proc.GetType();
            if (!_bellyFlagsMemberCache.TryGetValue(t, out MemberInfo member))
            {
                FieldInfo fi = t.GetField("flags", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null)
                    member = fi;
                else
                    member = t.GetProperty("flags", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _bellyFlagsMemberCache[t] = member;
            }

            return ReadMemberValue(proc, member);
        }

        private static object ReadMemberValue(object owner, MemberInfo member)
        {
            if (owner == null || member == null)
                return null;

            try
            {
                if (member is FieldInfo fi)
                    return fi.GetValue(owner);
                if (member is PropertyInfo pi)
                    return pi.GetValue(owner, null);
            }
            catch
            {
                // ignored
            }
            return null;
        }

        private static int GetIntMemberValueByName(object owner, string memberName, int fallback)
        {
            object value = GetMemberValueByName(owner, memberName);
            if (value == null)
                return fallback;
            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static string GetStringMemberValueByName(object owner, string memberName)
        {
            object value = GetMemberValueByName(owner, memberName);
            return value != null ? value.ToString() : string.Empty;
        }

        private static object GetMemberValueByName(object owner, string memberName)
        {
            if (owner == null || string.IsNullOrEmpty(memberName))
                return null;

            Type type = owner.GetType();
            if (!BellyMemberCache.TryGetValue(type, out Dictionary<string, MemberInfo> members))
            {
                members = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
                BellyMemberCache[type] = members;
            }
            if (!members.TryGetValue(memberName, out MemberInfo member))
            {
                member = (MemberInfo)type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                members[memberName] = member;
            }
            return ReadMemberValue(owner, member);
        }

        private bool TryGetBellyDistance(HSceneProc proc, ChaControl female, out float distanceMeters)
        {
            distanceMeters = 0f;
            if (proc == null || female == null)
                return false;

            ChaControl male = ResolveMaleForBelly(proc);
            if (male == null)
                return false;

            if (_bellyFemaleCha != female || _bellyFemaleDistanceRef == null)
            {
                _bellyFemaleCha = female;
                _bellyFemaleDistanceRef = ResolveFemaleDistanceReference(female);
            }

            if (_bellyMaleCha != male || _bellyMaleDistanceRef == null)
            {
                _bellyMaleCha = male;
                _bellyMaleDistanceRef = ResolveMaleDistanceReference(male);
            }

            if (_bellyFemaleDistanceRef == null || _bellyMaleDistanceRef == null)
                return false;

            distanceMeters = Vector3.Distance(_bellyMaleDistanceRef.position, _bellyFemaleDistanceRef.position);
            return true;
        }

        private ChaControl ResolveMaleForBelly(HSceneProc proc)
        {
            if (proc == null)
                return null;

            object maleObj = GetMemberValueByName(proc, "male");
            if (maleObj is ChaControl maleCha && maleCha != null)
                return maleCha;

            if (_bellyFallbackMale != null)
                return _bellyFallbackMale;

            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindNonIndependent();
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha != null && cha.sex == 0)
                    return _bellyFallbackMale = cha;
            }
            return null;
        }

        private static Transform ResolveFemaleDistanceReference(ChaControl female)
        {
            if (female == null)
                return null;

            Transform root = female.objBodyBone != null ? female.objBodyBone.transform : female.transform;
            return FindFirstTransformByNames(root,
                "k_f_kokan_00",
                "a_n_kokan",
                "cf_j_hips",
                "cf_j_root");
        }

        private static Transform ResolveMaleDistanceReference(ChaControl male)
        {
            if (male == null)
                return null;

            Transform root = male.objBodyBone != null ? male.objBodyBone.transform : male.transform;
            return FindFirstTransformByNames(root,
                "a_n_kokan",
                "cf_j_hips",
                "cf_j_waist01",
                "cm_j_waist01",
                "cf_j_root");
        }

        private static Transform FindFirstTransformByNames(Transform root, params string[] names)
        {
            if (root == null || names == null || names.Length == 0)
                return null;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < names.Length; i++)
            {
                string target = names[i];
                for (int j = 0; j < all.Length; j++)
                {
                    Transform t = all[j];
                    if (t != null && string.Equals(t.name, target, StringComparison.Ordinal))
                        return t;
                }
            }

            return null;
        }

        private static string BuildAnimationKey(int postureId, int postureMode, string postureName, string motionStrength, int animatorStateHash)
        {
            return postureId + "|" + postureMode + "|" + (postureName ?? string.Empty) + "|" + motionStrength + "|" + animatorStateHash;
        }

        private static string BuildShortKeyText(int postureId, int postureMode, string postureName, string motionStrength)
        {
            return "id=" + postureId + " mode=" + postureMode + " name=" + (postureName ?? string.Empty) + " strength=" + motionStrength;
        }

        private static string ClassifyMotionStrength(string clipName)
        {
            if (IsStrongPistonClip(clipName))
                return MotionStrengthStrong;
            if (IsWeakPistonClip(clipName))
                return MotionStrengthWeak;
            return MotionStrengthUnknown;
        }

        private static bool IsStrongOrWeakMotionStrength(string strength)
        {
            return string.Equals(strength ?? string.Empty, MotionStrengthStrong, StringComparison.Ordinal)
                || string.Equals(strength ?? string.Empty, MotionStrengthWeak, StringComparison.Ordinal);
        }

        private static bool IsStrongPistonClip(string clipName)
        {
            return (clipName ?? string.Empty).IndexOf("SLoop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsWeakPistonClip(string clipName)
        {
            return (clipName ?? string.Empty).IndexOf("WLoop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SaveBellyProfile(BellyContext context)
        {
            if (_bellyStore == null)
                return;

            var profile = new BellyBokoProfile
            {
                AnimationKey = context.AnimationKey ?? string.Empty,
                PostureId = context.PostureId,
                PostureMode = context.PostureMode,
                PostureName = context.PostureName ?? string.Empty,
                MotionStrength = context.MotionStrength ?? string.Empty,
                DistanceMinMeters = Mathf.Clamp(_cfgBellyDistanceMinMeters.Value, 0f, 2f),
                DistanceMaxMeters = Mathf.Clamp(_cfgBellyDistanceMaxMeters.Value, 0f, 2f),
                DistanceCutPercent = Mathf.Clamp01(_cfgBellyDistanceCutPercent.Value),
                DistanceSmoothing = Mathf.Clamp01(_cfgBellyDistanceSmoothing.Value),
                EaseUp = NormalizeEaseName(_cfgBellyEaseUp != null ? _cfgBellyEaseUp.Value : "easeOut", "easeOut"),
                EaseDown = NormalizeEaseName(_cfgBellyEaseDown != null ? _cfgBellyEaseDown.Value : "easeIn", "easeIn"),
                MinInflationSize = Mathf.Clamp(_cfgBellyMinInflationSize.Value, 0f, 40f),
                MaxInflationSize = Mathf.Clamp(_cfgBellyMaxInflationSize.Value, 0f, 40f)
            };

            _bellyStore.Upsert(profile);
            _bellyStore.Save();
            LogInfo("belly profile saved key=" + context.AnimationKey);
        }

        private void LoadBellyProfile(BellyContext context)
        {
            if (_bellyStore == null)
                return;

            if (!_bellyStore.TryGet(context.AnimationKey, out BellyBokoProfile profile) || profile == null)
            {
                LogInfo("belly profile not found key=" + context.AnimationKey);
                return;
            }

            _cfgBellyDistanceMinMeters.Value = Mathf.Clamp(profile.DistanceMinMeters, 0f, 2f);
            _cfgBellyDistanceMaxMeters.Value = Mathf.Clamp(profile.DistanceMaxMeters, 0f, 2f);
            _cfgBellyDistanceCutPercent.Value = Mathf.Clamp01(profile.DistanceCutPercent);
            _cfgBellyDistanceSmoothing.Value = Mathf.Clamp01(profile.DistanceSmoothing);
            if (_cfgBellyEaseUp != null)
                _cfgBellyEaseUp.Value = NormalizeEaseName(profile.EaseUp, "easeOut");
            if (_cfgBellyEaseDown != null)
                _cfgBellyEaseDown.Value = NormalizeEaseName(profile.EaseDown, "easeIn");
            _cfgBellyMinInflationSize.Value = Mathf.Clamp(profile.MinInflationSize, 0f, 40f);
            _cfgBellyMaxInflationSize.Value = Mathf.Clamp(profile.MaxInflationSize, 0f, 40f);
            _hasBellyDistanceSmoothed = false;

            LogInfo("belly profile loaded key=" + context.AnimationKey);
            ShowPresetPopup("腹ボコプロファイル読込: " + context.ShortKeyText, false);
        }

        private void LoadBellyFallbackProfile(BellyContext context)
        {
            _bellyFallbackProfile = null;
            if (_bellyStore == null)
                return;

            if (_bellyStore.TryGetByMotionStrength(context.PostureId, context.PostureMode, context.PostureName, MotionStrengthStrong, out BellyBokoProfile p))
            {
                _bellyFallbackProfile = p;
                LogInfo("belly fallback profile loaded from strong key=" + (p != null ? p.AnimationKey : "?"));
            }
            else
            {
                LogInfo("belly fallback profile not found (no strong piston profile for posture id=" + context.PostureId + " mode=" + context.PostureMode + ")");
            }
        }

        private static bool IsIdleClip(string clipName)
        {
            return (clipName ?? string.Empty).IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogBellyGate(string gate)
        {
            if (_cfgLogEnabled == null || !_cfgLogEnabled.Value)
                return;
            string state = gate ?? string.Empty;
            if (string.Equals(_lastBellyDiagGate, state, StringComparison.Ordinal))
                return;

            _lastBellyDiagGate = state;
            LogInfo("belly runtime gate=" + state);
        }

        private void LogBellyGate(string gate, BellyContext context)
        {
            if (_cfgLogEnabled == null || !_cfgLogEnabled.Value)
                return;
            string state = (gate ?? string.Empty) + "|" + (context.AnimationKey ?? string.Empty);
            if (string.Equals(_lastBellyDiagGate, state, StringComparison.Ordinal))
                return;

            _lastBellyDiagGate = state;
            LogInfo("belly runtime gate=" + (gate ?? string.Empty)
                + " key=" + context.ShortKeyText
                + " hash=" + context.AnimatorStateHash);
        }

        private void LogBellyApplySample(BellyContext context, float weight, float targetSize, float minSize, float maxSize, float distMin, float distMax, string easeDown)
        {
            if (!_cfgVerboseLog.Value)
                return;
            if (Time.unscaledTime < _nextBellyDiagLogTime)
                return;

            _nextBellyDiagLogTime = Time.unscaledTime + 0.5f;

            LogVerbose("belly runtime apply"
                + " key=" + context.ShortKeyText
                + " dist=" + (context.HasDistance ? context.Distance.ToString("0.000") : "(none)")
                + " smoothed=" + _bellyDistanceSmoothed.ToString("0.000")
                + " distRange=" + distMin.ToString("0.000") + "->" + distMax.ToString("0.000")
                + " ease=" + easeDown
                + " weight=" + weight.ToString("0.000")
                + " minSize=" + minSize.ToString("0.###")
                + " maxSize=" + maxSize.ToString("0.###")
                + " target=" + targetSize.ToString("0.###"));
        }

        private static void EnsureDistanceRange(ref float minMeters, ref float maxMeters)
        {
            minMeters = Mathf.Clamp(minMeters, 0f, 2f);
            maxMeters = Mathf.Clamp(maxMeters, 0f, 2f);
            if (maxMeters < minMeters + 0.0001f)
                maxMeters = minMeters + 0.0001f;
        }

        private static float EvaluateDistanceWeight(float distanceMeters, float minMeters, float maxMeters, float cutPercent, string easeName)
        {
            EnsureDistanceRange(ref minMeters, ref maxMeters);
            float d = Mathf.Max(0f, distanceMeters);
            float p = Mathf.Clamp01(cutPercent);
            float cut = minMeters + (maxMeters - minMeters) * p;
            if (cut < minMeters + 0.0001f)
                cut = minMeters + 0.0001f;

            if (d <= minMeters)
                return 1f;
            if (d >= cut)
                return 0f;

            float t = Mathf.InverseLerp(minMeters, cut, d);
            return 1f - EaseByName(t, easeName);
        }

        private void UpdateDistanceAnalysis(BellyContext context, float rawDistance, float evalDistance)
        {
            if (!_bellyDistanceAnalyzeActive)
                return;

            if (!string.Equals(_bellyDistanceAnalyzeKey ?? string.Empty, context.AnimationKey ?? string.Empty, StringComparison.Ordinal))
                return;

            float d = Mathf.Max(0f, evalDistance);
            if (d < _bellyDistanceAnalyzeMin)
                _bellyDistanceAnalyzeMin = d;
            if (d > _bellyDistanceAnalyzeMax)
                _bellyDistanceAnalyzeMax = d;

            if (_hasBellyDistanceAnalyzeLastPhase)
            {
                if (context.Phase + 0.5f < _bellyDistanceAnalyzeLastPhase)
                    _bellyDistanceAnalyzeCompletedTurns++;
            }

            _bellyDistanceAnalyzeLastPhase = context.Phase;
            _hasBellyDistanceAnalyzeLastPhase = true;

            if (_bellyDistanceAnalyzeCompletedTurns < _bellyDistanceAnalyzeTargetTurns)
                return;

            _bellyDistanceAnalyzeActive = false;
            float learnedMin = Mathf.Clamp(_bellyDistanceAnalyzeMin, 0f, 2f);
            float learnedMax = Mathf.Clamp(_bellyDistanceAnalyzeMax, 0f, 2f);
            EnsureDistanceRange(ref learnedMin, ref learnedMax);

            _cfgBellyDistanceMinMeters.Value = learnedMin;
            _cfgBellyDistanceMaxMeters.Value = learnedMax;

            SaveBellyProfile(context);

            ShowPresetPopup(
                "距離分析完了: min=" + learnedMin.ToString("0.000")
                + " max=" + learnedMax.ToString("0.000")
                + " (" + _bellyDistanceAnalyzeTargetTurns + "ターン)",
                false);
            LogInfo("belly distance analysis done"
                + " key=" + (context.AnimationKey ?? string.Empty)
                + " turns=" + _bellyDistanceAnalyzeTargetTurns
                + " min=" + learnedMin.ToString("0.000")
                + " max=" + learnedMax.ToString("0.000")
                + " raw=" + rawDistance.ToString("0.000"));
        }

        private static string NormalizeEaseName(string easing, string fallback)
        {
            if (easing == "linear" || easing == "easeIn" || easing == "easeOut" ||
                easing == "smoothStep" || easing == "smootherStep")
                return easing;
            string key = (easing ?? string.Empty).Trim().ToLowerInvariant();
            if (key == "linear")
                return "linear";
            if (key == "easein" || key == "in")
                return "easeIn";
            if (key == "easeout" || key == "out")
                return "easeOut";
            if (key == "smoothstep" || key == "smooth")
                return "smoothStep";
            if (key == "smootherstep" || key == "smoother")
                return "smootherStep";
            return fallback;
        }

        private static float EaseByName(float t, string easing)
        {
            t = Mathf.Clamp01(t);
            string key = NormalizeEaseName(easing, "linear");
            if (key == "easeIn")
                return t * t;
            if (key == "easeOut")
                return 1f - (1f - t) * (1f - t);
            if (key == "smoothStep")
                return t * t * (3f - 2f * t);
            if (key == "smootherStep")
                return t * t * t * (t * (6f * t - 15f) + 10f);
            return t;
        }

        private static float NormalizePhase01(float v)
        {
            return Mathf.Repeat(v, 1f);
        }

        private struct BellyContext
        {
            public int PostureId;
            public int PostureMode;
            public string PostureName;
            public string MotionStrength;
            public int AnimatorStateHash;
            public float Phase;
            public float Distance;
            public bool HasDistance;
            public string AnimationKey;
            public string ClipName;
            public string ShortKeyText;
            public string DisplayText;
        }
    }
}
