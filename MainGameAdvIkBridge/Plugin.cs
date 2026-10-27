using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using MainGameLogRelay;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameAdvIkBridge
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kks.main.advikbridge";
        public const string PluginName = "MainGameAdvIkBridge";
        public const string Version = "0.2.0";

        private static readonly string[] AssemblyNameCandidates = { "KKS_AdvIKPlugin", "AdvIKPlugin" };

        private static readonly string[] ControllerPropertyNames =
        {
            "ShoulderRotationEnabled", "ReverseShoulderL", "ReverseShoulderR",
            "EnableSpineFKHints", "EnableShoulderFKHints", "EnableToeFKHints",
            "IndependentShoulders", "ShoulderWeight", "ShoulderRightWeight",
            "ShoulderOffset", "ShoulderRightOffset", "SpineStiffness"
        };

        private const string LogOwner = PluginName;

        private Assembly _advAssembly;
        private Type _advControllerType;
        private Type _advPluginType;
        private readonly Dictionary<string, PropertyInfo> _controllerProperties = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        private PropertyInfo _mainGameBreathingProperty;
        private PropertyInfo _mainGameBreathScaleProperty;
        private PropertyInfo _mainGameBreathRateScaleProperty;
        private readonly HashSet<string> _warnedKeys = new HashSet<string>(StringComparer.Ordinal);
        private bool _bridgeBound;
        private float _nextScanTime;
        private float _nextBindRetryTime;
        private ChaControl[] _cachedCharacters;
        private float _nextCharacterRefreshTime;

        // 全般
        private ConfigEntry<bool> _cfgEnableBridge;
        private ConfigEntry<bool> _cfgApplyInHSceneOnly;
        private ConfigEntry<float> _cfgScanIntervalSeconds;

        // 肩回転
        private ConfigEntry<bool> _cfgShoulderRotationEnabled;
        private ConfigEntry<bool> _cfgIndependentShoulders;
        private ConfigEntry<float> _cfgShoulderWeight;
        private ConfigEntry<float> _cfgShoulderOffset;
        private ConfigEntry<float> _cfgShoulderRightWeight;
        private ConfigEntry<float> _cfgShoulderRightOffset;
        private ConfigEntry<bool> _cfgReverseShoulderLeft;
        private ConfigEntry<bool> _cfgReverseShoulderRight;

        // FK補正
        private ConfigEntry<bool> _cfgEnableSpineFKHints;
        private ConfigEntry<bool> _cfgEnableShoulderFKHints;
        private ConfigEntry<bool> _cfgEnableToeFKHints;
        private ConfigEntry<float> _cfgSpineStiffness;

        // 呼吸
        private ConfigEntry<bool> _cfgForceMainGameBreathingConfig;
        private ConfigEntry<bool> _cfgMainGameBreathing;
        private ConfigEntry<float> _cfgMainGameBreathScale;
        private ConfigEntry<float> _cfgMainGameBreathRateScale;

        // 操作
        private ConfigEntry<bool> _cfgDoRebind;

        // ログ
        private ConfigEntry<bool> _cfgEnableLogs;
        private ConfigEntry<bool> _cfgVerboseLogs;

        private void Awake()
        {
            BindConfig();
            LogAlways("start version=" + Version);
            TryBindAdvIk("awake");
        }

        private void BindConfig()
        {
            _cfgEnableBridge = Config.Bind("全般", "有効", true,
                new ConfigDescription(
                    "ADVik連携ループのON/OFF。\n" +
                    "OFFにすると肩回転・FK補正の設定適用が停止します。"));

            _cfgApplyInHSceneOnly = Config.Bind("全般", "Hシーン中のみ適用", false,
                new ConfigDescription(
                    "ONにするとHシーン中のみADVikへ設定を適用します。\n" +
                    "OFFにするとメインゲーム全体で常時適用します。"));

            _cfgScanIntervalSeconds = Config.Bind("全般", "適用間隔（秒）", 0.5f,
                new ConfigDescription(
                    "ADVikへの設定適用間隔（秒）。\n" +
                    "小さいほど設定変更が素早く反映されますが負荷が増えます。",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            _cfgShoulderRotationEnabled = Config.Bind("肩回転", "肩回転補正を有効化", true,
                new ConfigDescription(
                    "ADVikの肩回転補正のON/OFF。\n" +
                    "OFFにすると腕を動かしても肩が追従しなくなります。"));

            _cfgIndependentShoulders = Config.Bind("肩回転", "左右独立モード", false,
                new ConfigDescription(
                    "ONにすると左右の肩を別々のウェイト・オフセットで制御します。\n" +
                    "OFFのときは右肩の設定が無視され、左肩の値が両肩に適用されます。"));

            _cfgShoulderWeight = Config.Bind("肩回転", "左肩ウェイト", 1.5f,
                new ConfigDescription(
                    "左肩の補正強度。値が大きいほど腕に連動して肩が大きく動きます。\n" +
                    "左右独立モードOFFのときは両肩に適用されます。",
                    new AcceptableValueRange<float>(0f, 5f)));

            _cfgShoulderOffset = Config.Bind("肩回転", "左肩オフセット", 0.2f,
                new ConfigDescription(
                    "左肩補正が効き始める閾値の底上げ。\n" +
                    "0だと腕がほぼ伸びきってから効き始め、上げるほど早い段階から肩が動きます。\n" +
                    "左右独立モードOFFのときは両肩に適用されます。",
                    new AcceptableValueRange<float>(-1f, 1f)));

            _cfgShoulderRightWeight = Config.Bind("肩回転", "右肩ウェイト", 1.5f,
                new ConfigDescription(
                    "右肩の補正強度（左右独立モードON時のみ有効）。\n" +
                    "左右独立モードがOFFのときは左肩ウェイトが両肩に使われます。",
                    new AcceptableValueRange<float>(0f, 5f)));

            _cfgShoulderRightOffset = Config.Bind("肩回転", "右肩オフセット", 0.2f,
                new ConfigDescription(
                    "右肩補正の閾値底上げ（左右独立モードON時のみ有効）。\n" +
                    "左右独立モードがOFFのときは左肩オフセットが両肩に使われます。",
                    new AcceptableValueRange<float>(-1f, 1f)));

            _cfgReverseShoulderLeft = Config.Bind("肩回転", "左肩補正方向反転", false,
                new ConfigDescription(
                    "腕が肩より下に下がっているとき、左肩の補正方向を反転します。\n" +
                    "通常はOFFで問題ありません。"));

            _cfgReverseShoulderRight = Config.Bind("肩回転", "右肩補正方向反転", false,
                new ConfigDescription(
                    "腕が肩より下に下がっているとき、右肩の補正方向を反転します。\n" +
                    "通常はOFFで問題ありません。"));

            _cfgEnableSpineFKHints = Config.Bind("FK補正", "Spine FKヒント", true,
                new ConfigDescription(
                    "背骨のFKヒントを有効化します。\n" +
                    "IKで体を動かしたとき、アニメーションの背骨回転をIKに反映させます。"));

            _cfgEnableShoulderFKHints = Config.Bind("FK補正", "Shoulder FKヒント", false,
                new ConfigDescription(
                    "肩ボーンのFKヒントを有効化します。\n" +
                    "通常はOFFで問題ありません。肩の動きが過剰な場合はOFFにしてください。"));

            _cfgEnableToeFKHints = Config.Bind("FK補正", "Toe FKヒント", false,
                new ConfigDescription(
                    "つま先ボーンのFKヒントを有効化します。\n" +
                    "アニメーションのつま先回転をIKに反映させます。"));

            _cfgSpineStiffness = Config.Bind("FK補正", "背骨の剛性", 0.0f,
                new ConfigDescription(
                    "背骨の曲がりにくさ。0で通常のIK挙動、1で背骨がほぼ動かなくなります。\n" +
                    "IKで腰を動かしたとき背骨がつられて曲がりすぎる場合に上げてください。",
                    new AcceptableValueRange<float>(0f, 1f)));

            _cfgForceMainGameBreathingConfig = Config.Bind("呼吸", "呼吸設定を強制上書き", false,
                new ConfigDescription(
                    "ONにするとADVikの本編呼吸設定をこのプラグインの値で上書きします。\n" +
                    "下の呼吸設定を反映させたい場合はONにしてください。"));

            _cfgMainGameBreathing = Config.Bind("呼吸", "本編呼吸を有効化", false,
                new ConfigDescription(
                    "本編での呼吸アニメーション（胸・腹の上下）を有効化します。\n" +
                    "「呼吸設定を強制上書き」がONのときのみ反映されます。"));

            _cfgMainGameBreathScale = Config.Bind("呼吸", "呼吸の大きさ倍率", 1.0f,
                new ConfigDescription(
                    "呼吸アニメーションの振れ幅の倍率。1.0が標準です。\n" +
                    "大きくすると胸・腹の動きが誇張されます。「呼吸設定を強制上書き」がON時のみ反映。",
                    new AcceptableValueRange<float>(0.25f, 3f)));

            _cfgMainGameBreathRateScale = Config.Bind("呼吸", "呼吸速度の倍率", 1.0f,
                new ConfigDescription(
                    "呼吸アニメーションの速度倍率。1.0が標準です。\n" +
                    "大きくすると呼吸が速くなります。「呼吸設定を強制上書き」がON時のみ反映。",
                    new AcceptableValueRange<float>(0.25f, 3f)));

            _cfgDoRebind = Config.Bind("操作", "ADVik再バインドを実行", false,
                new ConfigDescription(
                    "ONにした瞬間にADVikへの再バインドを実行し、自動でOFFに戻ります。\n" +
                    "ADVikが見つからないときやキャラ入れ替え後に動作がおかしい場合に使用してください。"));
            _cfgDoRebind.SettingChanged += (_, __) =>
            {
                if (!_cfgDoRebind.Value) return;
                TryBindAdvIk("manual");
                _cfgDoRebind.Value = false;
            };

            _cfgEnableLogs = Config.Bind("ログ", "ログ出力を有効化", false,
                new ConfigDescription(
                    "プラグインのログファイル出力を有効化します。\n" +
                    "ログはプラグインフォルダ内の MainGameAdvIkBridge.log に出力されます。\n" +
                    "起動・バインド結果はこの設定に関わらず常時記録されます。"));

            _cfgVerboseLogs = Config.Bind("ログ", "詳細ログ", false,
                new ConfigDescription(
                    "毎サイクルの適用状況など詳細なログを出力します。\n" +
                    "通常はOFFで問題ありません。動作確認時にONにしてください。"));
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (!_cfgEnableBridge.Value)
                return;

            if (!_bridgeBound)
            {
                if (Time.unscaledTime >= _nextBindRetryTime)
                    TryBindAdvIk("retry");
                return;
            }

            if (Time.unscaledTime < _nextScanTime)
                return;

            _nextScanTime = Time.unscaledTime + Mathf.Clamp(_cfgScanIntervalSeconds.Value, 0.1f, 5f);
            if (_cfgApplyInHSceneOnly.Value && FindObjectOfType<HSceneProc>() == null)
                return;

            ApplyBridgeSettings();
        }

        private void TryBindAdvIk(string reason)
        {
            _bridgeBound = false;
            _advAssembly = null;
            _advControllerType = null;
            _advPluginType = null;
            _controllerProperties.Clear();
            _mainGameBreathingProperty = null;
            _mainGameBreathScaleProperty = null;
            _mainGameBreathRateScaleProperty = null;
            _warnedKeys.Clear();

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (string candidate in AssemblyNameCandidates)
            {
                foreach (Assembly asm in assemblies)
                {
                    if (string.Equals(asm.GetName().Name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        _advAssembly = asm;
                        break;
                    }
                }
                if (_advAssembly != null) break;
            }

            if (_advAssembly == null)
            {
                foreach (Assembly asm in assemblies)
                {
                    if (asm.GetType("AdvIKPlugin.AdvIKCharaController", false) != null)
                    {
                        _advAssembly = asm;
                        break;
                    }
                }
            }

            if (_advAssembly == null)
            {
                _nextBindRetryTime = Time.unscaledTime + Mathf.Max(5f, _cfgScanIntervalSeconds?.Value ?? 0.5f);
                LogAlways("bind skipped reason=" + reason + " ADVikアセンブリが見つかりません");
                return;
            }

            _advControllerType = _advAssembly.GetType("AdvIKPlugin.AdvIKCharaController", false);
            _advPluginType = _advAssembly.GetType("AdvIKPlugin.AdvIKPlugin", false);

            if (_advControllerType == null)
            {
                _nextBindRetryTime = Time.unscaledTime + Mathf.Max(5f, _cfgScanIntervalSeconds?.Value ?? 0.5f);
                LogAlways("bind failed reason=" + reason + " AdvIKCharaControllerが見つかりません");
                return;
            }

            foreach (string propName in ControllerPropertyNames)
            {
                PropertyInfo prop = _advControllerType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                    _controllerProperties[propName] = prop;
                else
                    LogAlways("property unavailable: " + propName);
            }

            if (_advPluginType != null)
            {
                _mainGameBreathingProperty = _advPluginType.GetProperty("MainGameBreathing", BindingFlags.Public | BindingFlags.Static);
                _mainGameBreathScaleProperty = _advPluginType.GetProperty("MainGameBreathScale", BindingFlags.Public | BindingFlags.Static);
                _mainGameBreathRateScaleProperty = _advPluginType.GetProperty("MainGameBreathRateScale", BindingFlags.Public | BindingFlags.Static);
            }

            _bridgeBound = true;
            LogAlways("bind ok reason=" + reason + " asm=" + _advAssembly.GetName().Name + " props=" + _controllerProperties.Count);
        }

        private void ApplyBridgeSettings()
        {
            float now = Time.unscaledTime;
            if (_cachedCharacters == null || now >= _nextCharacterRefreshTime)
            {
                _cachedCharacters = CanonPlugins.Shared.IndependentActorFilter.FindNonIndependent();
                _nextCharacterRefreshTime = now + 1f;
            }
            ChaControl[] characters = _cachedCharacters;
            if (characters == null || characters.Length == 0)
            {
                LogVerbose("apply skipped: ChaControlが見つかりません");
                return;
            }

            int foundControllers = 0;
            int changedValues = 0;

            foreach (ChaControl cha in characters)
            {
                if (cha == null || !cha.gameObject) continue;
                Component controller = cha.gameObject.GetComponent(_advControllerType);
                if (controller == null) continue;
                foundControllers++;
                changedValues += ApplyControllerValues(controller);
            }

            if (_cfgForceMainGameBreathingConfig.Value)
            {
                if (ApplyStaticConfigEntryValue(_mainGameBreathingProperty, _cfgMainGameBreathing.Value)) changedValues++;
                if (ApplyStaticConfigEntryValue(_mainGameBreathScaleProperty, _cfgMainGameBreathScale.Value)) changedValues++;
                if (ApplyStaticConfigEntryValue(_mainGameBreathRateScaleProperty, _cfgMainGameBreathRateScale.Value)) changedValues++;
            }

            LogVerbose("apply tick controllers=" + foundControllers + " chars=" + characters.Length + " changed=" + changedValues);
        }

        private int ApplyControllerValues(Component controller)
        {
            int changed = 0;
            if (ApplyControllerBool(controller, "ShoulderRotationEnabled", _cfgShoulderRotationEnabled.Value)) changed++;
            if (ApplyControllerBool(controller, "ReverseShoulderL", _cfgReverseShoulderLeft.Value)) changed++;
            if (ApplyControllerBool(controller, "ReverseShoulderR", _cfgReverseShoulderRight.Value)) changed++;
            if (ApplyControllerBool(controller, "EnableSpineFKHints", _cfgEnableSpineFKHints.Value)) changed++;
            if (ApplyControllerBool(controller, "EnableShoulderFKHints", _cfgEnableShoulderFKHints.Value)) changed++;
            if (ApplyControllerBool(controller, "EnableToeFKHints", _cfgEnableToeFKHints.Value)) changed++;
            if (ApplyControllerBool(controller, "IndependentShoulders", _cfgIndependentShoulders.Value)) changed++;
            if (ApplyControllerFloat(controller, "ShoulderWeight", _cfgShoulderWeight.Value)) changed++;
            if (ApplyControllerFloat(controller, "ShoulderRightWeight", _cfgShoulderRightWeight.Value)) changed++;
            if (ApplyControllerFloat(controller, "ShoulderOffset", _cfgShoulderOffset.Value)) changed++;
            if (ApplyControllerFloat(controller, "ShoulderRightOffset", _cfgShoulderRightOffset.Value)) changed++;
            if (ApplyControllerFloat(controller, "SpineStiffness", _cfgSpineStiffness.Value)) changed++;
            return changed;
        }

        private bool ApplyControllerBool(Component controller, string propertyName, bool targetValue)
        {
            PropertyInfo property;
            if (!_controllerProperties.TryGetValue(propertyName, out property)) return false;
            try
            {
                object currentRaw = property.GetValue(controller, null);
                bool current = currentRaw is bool b && b;
                if (current == targetValue) return false;
                property.SetValue(controller, targetValue, null);
                return true;
            }
            catch (Exception ex)
            {
                LogWarnOnce("apply-bool-" + propertyName, "bool適用失敗 " + propertyName + ": " + ex.Message);
                return false;
            }
        }

        private bool ApplyControllerFloat(Component controller, string propertyName, float targetValue)
        {
            PropertyInfo property;
            if (!_controllerProperties.TryGetValue(propertyName, out property)) return false;
            try
            {
                object currentRaw = property.GetValue(controller, null);
                float current = Convert.ToSingle(currentRaw, CultureInfo.InvariantCulture);
                if (Mathf.Abs(current - targetValue) <= 0.0001f) return false;
                property.SetValue(controller, targetValue, null);
                return true;
            }
            catch (Exception ex)
            {
                LogWarnOnce("apply-float-" + propertyName, "float適用失敗 " + propertyName + ": " + ex.Message);
                return false;
            }
        }

        private bool ApplyStaticConfigEntryValue(PropertyInfo entryProperty, object targetValue)
        {
            if (entryProperty == null) return false;
            try
            {
                object entry = entryProperty.GetValue(null, null);
                if (entry == null) return false;
                PropertyInfo valueProp = entry.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                if (valueProp == null || !valueProp.CanRead || !valueProp.CanWrite) return false;
                object current = valueProp.GetValue(entry, null);
                if (Equals(current, targetValue)) return false;
                valueProp.SetValue(entry, targetValue, null);
                return true;
            }
            catch (Exception ex)
            {
                LogWarnOnce("static-config-" + entryProperty?.Name, "静的設定適用失敗: " + ex.Message);
                return false;
            }
        }

        // 常時記録（EnableLogs設定に関わらず出力）
        private void LogAlways(string message)
        {
            Logger.LogInfo("[" + PluginName + "] " + message);
            LogRelayApi.Info(LogOwner, message);
        }

        private void LogInfo(string message)
        {
            if (_cfgEnableLogs == null || !_cfgEnableLogs.Value) return;
            Logger.LogInfo("[" + PluginName + "] " + message);
            LogRelayApi.Info(LogOwner, message);
        }

        private void LogVerbose(string message)
        {
            if (_cfgEnableLogs == null || !_cfgEnableLogs.Value) return;
            if (_cfgVerboseLogs == null || !_cfgVerboseLogs.Value) return;
            Logger.LogDebug("[" + PluginName + "] " + message);
            LogRelayApi.Debug(LogOwner, message);
        }

        private void LogWarnOnce(string key, string message)
        {
            if (_warnedKeys.Contains(key)) return;
            _warnedKeys.Add(key);
            Logger.LogWarning("[" + PluginName + "] " + message);
            LogRelayApi.Warn(LogOwner, message);
        }
    }
}
