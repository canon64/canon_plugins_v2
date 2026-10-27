using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using MainGameLogRelay;
using UnityEngine;

namespace MainGamePregnancyPlusBridge
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInDependency(MainGameLogRelay.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kks.main.pregnancyplusbridge";
        public const string PluginName = "MainGamePregnancyPlusBridge";
        public const string Version = "0.3.1";

        private const string RelayOwner = GUID;
        private const string RelayLogKey = "main/" + PluginName;

        private const string PregnancyPlusAssemblyName = "KKS_PregnancyPlus";
        private const string PregnancyPlusPluginTypeName = "KK_PregnancyPlus.PregnancyPlusPlugin";
        private const string PregnancyPlusControllerTypeName = "KK_PregnancyPlus.PregnancyPlusCharaController";
        private const string PregnancyPlusDataTypeName = "KK_PregnancyPlus.PregnancyPlusData";

        private static readonly string[] DataFieldNames =
        {
            "inflationSize",
            "inflationMoveY",
            "inflationMoveZ",
            "inflationStretchX",
            "inflationStretchY",
            "inflationShiftY",
            "inflationShiftZ",
            "inflationTaperY",
            "inflationTaperZ",
            "inflationMultiplier",
            "inflationClothOffset",
            "inflationFatFold",
            "inflationFatFoldHeight",
            "inflationFatFoldGap",
            "GameplayEnabled",
            "inflationRoundness",
            "inflationDrop",
            "clothingOffsetVersion",
            "pluginVersion"
        };

        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<float> _cfgApplyIntervalSeconds;

        private ConfigEntry<bool> _cfgDataGameplayEnabled;
        private ConfigEntry<float> _cfgDataInflationMoveY;
        private ConfigEntry<float> _cfgDataInflationMoveZ;
        private ConfigEntry<float> _cfgDataInflationStretchX;
        private ConfigEntry<float> _cfgDataInflationStretchY;
        private ConfigEntry<float> _cfgDataInflationShiftY;
        private ConfigEntry<float> _cfgDataInflationShiftZ;
        private ConfigEntry<float> _cfgDataInflationTaperY;
        private ConfigEntry<float> _cfgDataInflationTaperZ;
        private ConfigEntry<float> _cfgDataInflationMultiplier;
        private ConfigEntry<float> _cfgDataInflationClothOffset;
        private ConfigEntry<float> _cfgDataInflationFatFold;
        private ConfigEntry<float> _cfgDataInflationFatFoldHeight;
        private ConfigEntry<float> _cfgDataInflationFatFoldGap;
        private ConfigEntry<float> _cfgDataInflationRoundness;
        private ConfigEntry<float> _cfgDataInflationDrop;
        private ConfigEntry<int> _cfgDataClothingOffsetVersion;
        private ConfigEntry<string> _cfgDataPluginVersion;

        private ConfigEntry<bool> _cfgLogEnabled;
        private ConfigEntry<bool> _cfgVerboseLog;

        private Assembly _pregnancyAssembly;
        private Type _pluginType;
        private Type _controllerType;
        private Type _dataType;
        private MethodInfo _getCharaControllerMethod;
        private MethodInfo _meshInflateByDataMethod;
        private MethodInfo _meshInflateBySizeMethod;

        private readonly Dictionary<string, FieldInfo> _dataFieldMap = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnedKeys = new HashSet<string>(StringComparer.Ordinal);

        private bool _bridgeReady;
        private bool _dirty = true;
        private float _nextApplyTime;
        private float _nextBindTryTime;
        private bool _defaultPresetApplied;

        private static ConfigurationManager.ConfigurationManagerAttributes UiOrder(int order)
        {
            return new ConfigurationManager.ConfigurationManagerAttributes
            {
                Order = order
            };
        }

                private void Awake()
        {
            _instance = this;
            string pluginDir = Path.GetDirectoryName(Info.Location) ?? Directory.GetCurrentDirectory();

            _cfgEnabled = Config.Bind(
                "00.General",
                "Enabled",
                true,
                new ConfigDescription("ブリッジ機能の有効/無効", null, UiOrder(2000)));
            _cfgApplyIntervalSeconds = Config.Bind(
                "00.General",
                "ApplyIntervalSeconds",
                0.5f,
                new ConfigDescription("全キャラへ再適用する間隔（秒）", new AcceptableValueRange<float>(0.1f, 5f), UiOrder(1999)));

            _cfgDataGameplayEnabled = Config.Bind(
                "30.PregnancyPlusData",
                "GameplayEnabled",
                true,
                new ConfigDescription("本編でPregnancy+の変形適用を有効にする", null, UiOrder(799)));
            _cfgDataInflationMoveY = Config.Bind(
                "30.PregnancyPlusData",
                "InflationMoveY",
                0f,
                new ConfigDescription("Move Y（上下移動）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(797)));
            _cfgDataInflationMoveZ = Config.Bind(
                "30.PregnancyPlusData",
                "InflationMoveZ",
                0f,
                new ConfigDescription("Move Z（前後移動）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(796)));
            _cfgDataInflationStretchX = Config.Bind(
                "30.PregnancyPlusData",
                "InflationStretchX",
                0f,
                new ConfigDescription("Stretch X（横方向の伸縮）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(795)));
            _cfgDataInflationStretchY = Config.Bind(
                "30.PregnancyPlusData",
                "InflationStretchY",
                0f,
                new ConfigDescription("Stretch Y（縦方向の伸縮）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(794)));
            _cfgDataInflationShiftY = Config.Bind(
                "30.PregnancyPlusData",
                "InflationShiftY",
                0f,
                new ConfigDescription("Shift Y（形状シフトY）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(793)));
            _cfgDataInflationShiftZ = Config.Bind(
                "30.PregnancyPlusData",
                "InflationShiftZ",
                0f,
                new ConfigDescription("Shift Z（形状シフトZ）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(792)));
            _cfgDataInflationTaperY = Config.Bind(
                "30.PregnancyPlusData",
                "InflationTaperY",
                0f,
                new ConfigDescription("Taper Y（先細りY）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(791)));
            _cfgDataInflationTaperZ = Config.Bind(
                "30.PregnancyPlusData",
                "InflationTaperZ",
                0f,
                new ConfigDescription("Taper Z（先細りZ）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(790)));
            _cfgDataInflationMultiplier = Config.Bind(
                "30.PregnancyPlusData",
                "InflationMultiplier",
                0f,
                new ConfigDescription("Multiplier（全体倍率）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(789)));
            _cfgDataInflationClothOffset = Config.Bind(
                "30.PregnancyPlusData",
                "InflationClothOffset",
                0f,
                new ConfigDescription("Cloth Offset（衣装オフセット補正）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(788)));
            _cfgDataInflationFatFold = Config.Bind(
                "30.PregnancyPlusData",
                "InflationFatFold",
                0f,
                new ConfigDescription("Fat Fold（しわ量）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(787)));
            _cfgDataInflationFatFoldHeight = Config.Bind(
                "30.PregnancyPlusData",
                "InflationFatFoldHeight",
                0f,
                new ConfigDescription("Fat Fold Height（しわ高さ）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(786)));
            _cfgDataInflationFatFoldGap = Config.Bind(
                "30.PregnancyPlusData",
                "InflationFatFoldGap",
                0f,
                new ConfigDescription("Fat Fold Gap（しわ間隔）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(785)));
            _cfgDataInflationRoundness = Config.Bind(
                "30.PregnancyPlusData",
                "InflationRoundness",
                0f,
                new ConfigDescription("Roundness（丸み）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(784)));
            _cfgDataInflationDrop = Config.Bind(
                "30.PregnancyPlusData",
                "InflationDrop",
                0f,
                new ConfigDescription("Drop（下垂）", new AcceptableValueRange<float>(-5f, 5f), UiOrder(783)));
            _cfgDataClothingOffsetVersion = Config.Bind(
                "30.PregnancyPlusData",
                "ClothingOffsetVersion",
                1,
                new ConfigDescription("ClothingOffsetVersion（通常は1）", new AcceptableValueRange<int>(0, 9), UiOrder(782)));
            _cfgDataPluginVersion = Config.Bind(
                "30.PregnancyPlusData",
                "PluginVersion",
                string.Empty,
                new ConfigDescription("pluginVersion（空欄時は本プラグインVersionを使用）", null, UiOrder(781)));

            _cfgLogEnabled = Config.Bind(
                "90.Logging",
                "EnableLog",
                false,
                new ConfigDescription("ログ出力の有効/無効", null, UiOrder(100)));
            _cfgVerboseLog = Config.Bind(
                "90.Logging",
                "VerboseLog",
                false,
                new ConfigDescription("詳細ログの有効/無効", null, UiOrder(99)));
            _cfgLogEnabled.SettingChanged += (_, __) =>
            {
                _lastBellyDiagGate = string.Empty;
                ApplyRelayLoggingState();
            };
            _cfgVerboseLog.SettingChanged += (_, __) => ApplyRelayLoggingState();
            ApplyRelayLoggingState();

            InitializePresetSystem(pluginDir);
            InitializeBellyBokoSystem(pluginDir);
            InitializeSettingJsonSystem(pluginDir);

            BindDirtyEvents();
            TryBindBridge("awake");
        }
        private void Update()
        {
            if (!_cfgEnabled.Value)
                return;

            if (!_bridgeReady)
            {
                if (Time.unscaledTime >= _nextBindTryTime)
                    TryBindBridge("retry");
                return;
            }
            bool forceApplyByBellyBoko = UpdateBellyBokoRuntime();

            bool fullApplyDue = _dirty || Time.unscaledTime >= _nextApplyTime;
            if (!forceApplyByBellyBoko && !fullApplyDue)
                return;

            if (fullApplyDue || _meshInflateBySizeMethod == null)
            {
                _nextApplyTime = Time.unscaledTime + Mathf.Clamp(_cfgApplyIntervalSeconds.Value, 0.1f, 5f);
                ApplyToAllCharacters();
                _dirty = false;
            }
            else
            {
                ApplyInflationSizeToAllCharacters();
            }
        }

        private void BindDirtyEvents()
        {
            _cfgEnabled.SettingChanged += MarkDirty;
            _cfgApplyIntervalSeconds.SettingChanged += MarkDirty;
            _cfgDataGameplayEnabled.SettingChanged += MarkDirty;
            _cfgDataInflationMoveY.SettingChanged += MarkDirty;
            _cfgDataInflationMoveZ.SettingChanged += MarkDirty;
            _cfgDataInflationStretchX.SettingChanged += MarkDirty;
            _cfgDataInflationStretchY.SettingChanged += MarkDirty;
            _cfgDataInflationShiftY.SettingChanged += MarkDirty;
            _cfgDataInflationShiftZ.SettingChanged += MarkDirty;
            _cfgDataInflationTaperY.SettingChanged += MarkDirty;
            _cfgDataInflationTaperZ.SettingChanged += MarkDirty;
            _cfgDataInflationMultiplier.SettingChanged += MarkDirty;
            _cfgDataInflationClothOffset.SettingChanged += MarkDirty;
            _cfgDataInflationFatFold.SettingChanged += MarkDirty;
            _cfgDataInflationFatFoldHeight.SettingChanged += MarkDirty;
            _cfgDataInflationFatFoldGap.SettingChanged += MarkDirty;
            _cfgDataInflationRoundness.SettingChanged += MarkDirty;
            _cfgDataInflationDrop.SettingChanged += MarkDirty;
            _cfgDataClothingOffsetVersion.SettingChanged += MarkDirty;
            _cfgDataPluginVersion.SettingChanged += MarkDirty;
        }

        private void MarkDirty(object sender, EventArgs e)
        {
            _dirty = true;
        }

        private void ApplyRelayLoggingState()
        {
            if (!LogRelayApi.IsAvailable)
                return;

            bool enabled = _cfgLogEnabled != null && _cfgLogEnabled.Value;
            bool verbose = _cfgVerboseLog != null && _cfgVerboseLog.Value;

            LogRelayApi.SetOwnerLogKey(RelayOwner, RelayLogKey);
            LogRelayApi.SetOwnerOutputMode(RelayOwner, LogRelayOutputMode.FileOnly);
            LogRelayApi.SetOwnerMinimumLevel(RelayOwner, verbose ? LogRelayLevel.Debug : LogRelayLevel.Info);
            LogRelayApi.SetOwnerEnabled(RelayOwner, enabled);
        }

        private void TryBindBridge(string reason)
        {
            _bridgeReady = false;
            _pregnancyAssembly = ResolvePregnancyAssembly();
            if (_pregnancyAssembly == null)
            {
                _nextBindTryTime = Time.unscaledTime + 2f;
                LogVerbose("bind skipped reason=" + reason + " detail=assembly-not-loaded");
                return;
            }

            _pluginType = _pregnancyAssembly.GetType(PregnancyPlusPluginTypeName, false);
            _controllerType = _pregnancyAssembly.GetType(PregnancyPlusControllerTypeName, false);
            _dataType = _pregnancyAssembly.GetType(PregnancyPlusDataTypeName, false);
            if (_pluginType == null || _controllerType == null || _dataType == null)
            {
                _nextBindTryTime = Time.unscaledTime + 2f;
                LogWarnOnce("bind-type-missing", "bind failed reason=" + reason + " detail=required-type-missing");
                return;
            }

            _getCharaControllerMethod = _pluginType.GetMethod("GetCharaController", BindingFlags.Public | BindingFlags.Static);
            if (_getCharaControllerMethod == null)
            {
                _nextBindTryTime = Time.unscaledTime + 2f;
                LogWarnOnce("bind-get-controller", "bind failed reason=" + reason + " detail=GetCharaController-not-found");
                return;
            }

            _meshInflateByDataMethod = ResolveMeshInflateMethod();
            _meshInflateBySizeMethod = ResolveMeshInflateSizeMethod();
            if (_meshInflateByDataMethod == null)
            {
                _nextBindTryTime = Time.unscaledTime + 2f;
                LogWarnOnce("bind-mesh-inflate", "bind failed reason=" + reason + " detail=MeshInflate(data)-not-found");
                return;
            }

            _dataFieldMap.Clear();
            for (int i = 0; i < DataFieldNames.Length; i++)
            {
                string fieldName = DataFieldNames[i];
                FieldInfo field = _dataType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                _dataFieldMap[fieldName] = field;
                if (field == null)
                    LogWarnOnce("field-" + fieldName, "PregnancyPlusData field missing: " + fieldName);
            }

            _bridgeReady = true;
            _dirty = true;
            LogInfo("bind ok reason=" + reason + " assembly=" + _pregnancyAssembly.GetName().Name);

            if (!_defaultPresetApplied)
            {
                _defaultPresetApplied = true;
                _cfgPresetSelectedSlot.Value = 1;
                LoadPresetToCurrent();
            }
        }

        private Assembly ResolvePregnancyAssembly()
        {
            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loaded.Length; i++)
            {
                Assembly asm = loaded[i];
                if (string.Equals(asm.GetName().Name, PregnancyPlusAssemblyName, StringComparison.OrdinalIgnoreCase))
                    return asm;
            }

            for (int i = 0; i < loaded.Length; i++)
            {
                Assembly asm = loaded[i];
                if (asm.GetType(PregnancyPlusPluginTypeName, false) != null)
                    return asm;
            }

            return null;
        }

        private MethodInfo ResolveMeshInflateMethod()
        {
            MethodInfo[] methods = _controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, "MeshInflate", StringComparison.Ordinal))
                    continue;

                ParameterInfo[] p = method.GetParameters();
                if (p.Length != 3)
                    continue;
                if (p[0].ParameterType != _dataType)
                    continue;
                if (p[1].ParameterType != typeof(string))
                    continue;

                return method;
            }

            return null;
        }

        private MethodInfo ResolveMeshInflateSizeMethod()
        {
            MethodInfo[] methods = _controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, "MeshInflate", StringComparison.Ordinal)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 3 && parameters[0].ParameterType == typeof(float)
                    && parameters[1].ParameterType == typeof(string))
                    return method;
            }
            return null;
        }

        private ChaControl[] _applyCharactersCache;
        private float _nextApplyCharactersScanTime;
        private readonly object[] _sizeControllerArgs = new object[1];
        private readonly object[] _sizeInflateArgs = { 0f, "MainGamePregnancyPlusBridge", null };

        private ChaControl[] GetApplyCharacters()
        {
            if (_applyCharactersCache == null || Time.unscaledTime >= _nextApplyCharactersScanTime)
            {
                _applyCharactersCache = CanonPlugins.Shared.IndependentActorFilter.FindNonIndependent();
                _nextApplyCharactersScanTime = Time.unscaledTime + 0.25f;
            }
            return _applyCharactersCache;
        }

        private void ApplyToAllCharacters()
        {
            ChaControl[] characters = GetApplyCharacters();
            if (characters == null || characters.Length == 0)
            {
                LogVerbose("apply skipped detail=no-character");
                return;
            }

            int foundControllers = 0;
            int applied = 0;
            int failed = 0;
            object[] controllerArgs = new object[1];
            object[] inflateArgs = { null, "MainGamePregnancyPlusBridge", null };

            for (int i = 0; i < characters.Length; i++)
            {
                ChaControl cha = characters[i];
                if (cha == null)
                    continue;

                object controller;
                try
                {
                    controllerArgs[0] = cha;
                    controller = _getCharaControllerMethod.Invoke(null, controllerArgs);
                }
                catch (Exception ex)
                {
                    failed++;
                    LogWarn("get-controller failed name=" + cha.name + " err=" + Unwrap(ex).Message);
                    continue;
                }

                if (controller == null)
                    continue;

                foundControllers++;

                try
                {
                    object data = BuildDataObject();
                    inflateArgs[0] = data;
                    try { _meshInflateByDataMethod.Invoke(controller, inflateArgs); }
                    finally { inflateArgs[2] = null; }
                    applied++;
                }
                catch (Exception ex)
                {
                    failed++;
                    LogWarn("apply failed name=" + cha.name + " err=" + Unwrap(ex).Message);
                }
            }

            LogVerbose("apply complete chars=" + characters.Length + " controllers=" + foundControllers + " applied=" + applied + " failed=" + failed);
        }

        private void ApplyInflationSizeToAllCharacters()
        {
            ChaControl[] characters = GetApplyCharacters();
            if (characters == null || characters.Length == 0) return;

            object[] controllerArgs = _sizeControllerArgs;
            object[] inflateArgs = _sizeInflateArgs;
            inflateArgs[0] = GetEffectiveInflationSize();
            for (int i = 0; i < characters.Length; i++)
            {
                ChaControl cha = characters[i];
                if (cha == null) continue;
                try
                {
                    controllerArgs[0] = cha;
                    object controller = _getCharaControllerMethod.Invoke(null, controllerArgs);
                    if (controller == null) continue;
                    try { _meshInflateBySizeMethod.Invoke(controller, inflateArgs); }
                    finally { inflateArgs[2] = null; }
                }
                catch (Exception ex)
                {
                    LogWarnOnce("inflate-size-" + cha.GetInstanceID(),
                        "belly size apply failed name=" + cha.name + " err=" + Unwrap(ex).Message);
                }
            }
        }

        private object BuildDataObject()
        {
            object data = Activator.CreateInstance(_dataType);
            SetDataFieldValue(data, "GameplayEnabled", _cfgDataGameplayEnabled.Value);
            SetDataFieldValue(data, "inflationSize", GetEffectiveInflationSize());
            SetDataFieldValue(data, "inflationMoveY", _cfgDataInflationMoveY.Value);
            SetDataFieldValue(data, "inflationMoveZ", _cfgDataInflationMoveZ.Value);
            SetDataFieldValue(data, "inflationStretchX", _cfgDataInflationStretchX.Value);
            SetDataFieldValue(data, "inflationStretchY", _cfgDataInflationStretchY.Value);
            SetDataFieldValue(data, "inflationShiftY", _cfgDataInflationShiftY.Value);
            SetDataFieldValue(data, "inflationShiftZ", _cfgDataInflationShiftZ.Value);
            SetDataFieldValue(data, "inflationTaperY", _cfgDataInflationTaperY.Value);
            SetDataFieldValue(data, "inflationTaperZ", _cfgDataInflationTaperZ.Value);
            SetDataFieldValue(data, "inflationMultiplier", _cfgDataInflationMultiplier.Value);
            SetDataFieldValue(data, "inflationClothOffset", _cfgDataInflationClothOffset.Value);
            SetDataFieldValue(data, "inflationFatFold", _cfgDataInflationFatFold.Value);
            SetDataFieldValue(data, "inflationFatFoldHeight", _cfgDataInflationFatFoldHeight.Value);
            SetDataFieldValue(data, "inflationFatFoldGap", _cfgDataInflationFatFoldGap.Value);
            SetDataFieldValue(data, "inflationRoundness", _cfgDataInflationRoundness.Value);
            SetDataFieldValue(data, "inflationDrop", _cfgDataInflationDrop.Value);
            SetDataFieldValue(data, "clothingOffsetVersion", _cfgDataClothingOffsetVersion.Value);

            string pluginVersion = string.IsNullOrWhiteSpace(_cfgDataPluginVersion.Value) ? Version : _cfgDataPluginVersion.Value;
            SetDataFieldValue(data, "pluginVersion", pluginVersion);

            return data;
        }

        private void SetDataFieldValue(object instance, string fieldName, object value)
        {
            if (!_dataFieldMap.TryGetValue(fieldName, out FieldInfo field) || field == null)
                return;

            try
            {
                object converted = value;
                Type targetType = field.FieldType;

                if (targetType == typeof(float))
                    converted = Convert.ToSingle(value);
                else if (targetType == typeof(int))
                    converted = Convert.ToInt32(value);
                else if (targetType == typeof(bool))
                    converted = Convert.ToBoolean(value);
                else if (targetType == typeof(string))
                    converted = value?.ToString();

                field.SetValue(instance, converted);
            }
            catch (Exception ex)
            {
                LogWarnOnce("set-field-" + fieldName, "set field failed: " + fieldName + " err=" + Unwrap(ex).Message);
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            return ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
        }

        private void LogInfo(string message)
        {
            if (!_cfgLogEnabled.Value)
                return;
            if (!LogRelayApi.IsAvailable)
                return;
            LogRelayApi.Info(RelayOwner, message);
        }

        private void LogWarn(string message)
        {
            if (!_cfgLogEnabled.Value)
                return;
            if (!LogRelayApi.IsAvailable)
                return;
            LogRelayApi.Warn(RelayOwner, message);
        }

        private void LogVerbose(string message)
        {
            if (!_cfgLogEnabled.Value || !_cfgVerboseLog.Value)
                return;
            if (!LogRelayApi.IsAvailable)
                return;
            LogRelayApi.Debug(RelayOwner, message);
        }

        private void LogWarnOnce(string key, string message)
        {
            if (_warnedKeys.Contains(key))
                return;

            _warnedKeys.Add(key);
            LogWarn(message);
        }
    }
}
