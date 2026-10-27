using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGamePregnancyPlusBridge
{
    [DataContract]
    internal sealed class BridgeSettingSnapshot
    {
        [DataMember(Order = 1)]
        public bool GeneralEnabled = true;
        [DataMember(Order = 2)]
        public float GeneralApplyIntervalSeconds = 0.5f;

        [DataMember(Order = 10)]
        public bool DataGameplayEnabled = true;
        [DataMember(Order = 11)]
        public float DataInflationMoveY = 0f;
        [DataMember(Order = 12)]
        public float DataInflationMoveZ = 0f;
        [DataMember(Order = 13)]
        public float DataInflationStretchX = 0f;
        [DataMember(Order = 14)]
        public float DataInflationStretchY = 0f;
        [DataMember(Order = 15)]
        public float DataInflationShiftY = 0f;
        [DataMember(Order = 16)]
        public float DataInflationShiftZ = 0f;
        [DataMember(Order = 17)]
        public float DataInflationTaperY = 0f;
        [DataMember(Order = 18)]
        public float DataInflationTaperZ = 0f;
        [DataMember(Order = 19)]
        public float DataInflationMultiplier = 0f;
        [DataMember(Order = 20)]
        public float DataInflationClothOffset = 0f;
        [DataMember(Order = 21)]
        public float DataInflationFatFold = 0f;
        [DataMember(Order = 22)]
        public float DataInflationFatFoldHeight = 0f;
        [DataMember(Order = 23)]
        public float DataInflationFatFoldGap = 0f;
        [DataMember(Order = 24)]
        public float DataInflationRoundness = 0f;
        [DataMember(Order = 25)]
        public float DataInflationDrop = 0f;
        [DataMember(Order = 26)]
        public int DataClothingOffsetVersion = 1;
        [DataMember(Order = 27)]
        public string DataPluginVersion = string.Empty;

        [DataMember(Order = 30)]
        public bool LoggingEnableLog = false;
        [DataMember(Order = 31)]
        public bool LoggingVerboseLog = false;

        [DataMember(Order = 40)]
        public int PresetSelectedSlot = 1;
        [DataMember(Order = 41)]
        public string PresetName = string.Empty;

        [DataMember(Order = 50)]
        public bool BellyEnabled = true;
        [DataMember(Order = 51)]
        public float BellyMinInflationSize = 0f;
        [DataMember(Order = 52)]
        public float BellyMaxInflationSize = 5f;
        [DataMember(Order = 53)]
        public float BellyDistanceCutPercent = 0.9f;
        [DataMember(Order = 54)]
        public float BellyDistanceMinMeters = 0.04f;
        [DataMember(Order = 55)]
        public float BellyDistanceMaxMeters = 0.8f;
        [DataMember(Order = 56)]
        public float BellyDistanceSmoothing = 0.8f;
        [DataMember(Order = 57)]
        public int BellyDistanceAnalyzeTurns = 10;
        [DataMember(Order = 58)]
        public string BellyEaseUp = "easeOut";
        [DataMember(Order = 59)]
        public string BellyEaseDown = "easeIn";
    }

    public sealed partial class Plugin
    {
        private string _settingJsonPath;
        private bool _isApplyingSettingJson;
        private bool _isSavingSettingJson;

        private void InitializeSettingJsonSystem(string pluginDir)
        {
            _settingJsonPath = Path.Combine(pluginDir, "Setting.json");

            BridgeSettingSnapshot snapshot = null;
            string source = "unknown";
            if (File.Exists(_settingJsonPath))
            {
                try
                {
                    snapshot = ReadSettingSnapshot(_settingJsonPath);
                    source = "json";
                    Logger.LogInfo("[setting] loaded path=" + _settingJsonPath);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("[setting] load failed path=" + _settingJsonPath + " err=" + ex.Message);
                    snapshot = BuildHardcodedDefaultSettingSnapshot();
                    source = "hardcoded-defaults(recovered)";
                    WriteSettingSnapshotSafe(snapshot, false);
                    Logger.LogInfo("[setting] recreated defaults path=" + _settingJsonPath + " source=hardcoded");
                }
            }

            if (snapshot == null)
            {
                snapshot = BuildHardcodedDefaultSettingSnapshot();
                source = "hardcoded-defaults(created)";
                WriteSettingSnapshotSafe(snapshot, false);
                Logger.LogInfo("[setting] created path=" + _settingJsonPath + " source=hardcoded");
            }

            // JSON正本: cfgは常にJSONスナップショットで上書きする。
            ApplySettingSnapshot(snapshot);
            SaveConfigSafely();
            Logger.LogInfo("[setting] applied to cfg source=" + source);

            SubscribeSettingJsonEvents();
        }

        private void SubscribeSettingJsonEvents()
        {
            SubscribeSettingJson(_cfgEnabled);
            SubscribeSettingJson(_cfgApplyIntervalSeconds);

            SubscribeSettingJson(_cfgDataGameplayEnabled);
            SubscribeSettingJson(_cfgDataInflationMoveY);
            SubscribeSettingJson(_cfgDataInflationMoveZ);
            SubscribeSettingJson(_cfgDataInflationStretchX);
            SubscribeSettingJson(_cfgDataInflationStretchY);
            SubscribeSettingJson(_cfgDataInflationShiftY);
            SubscribeSettingJson(_cfgDataInflationShiftZ);
            SubscribeSettingJson(_cfgDataInflationTaperY);
            SubscribeSettingJson(_cfgDataInflationTaperZ);
            SubscribeSettingJson(_cfgDataInflationMultiplier);
            SubscribeSettingJson(_cfgDataInflationClothOffset);
            SubscribeSettingJson(_cfgDataInflationFatFold);
            SubscribeSettingJson(_cfgDataInflationFatFoldHeight);
            SubscribeSettingJson(_cfgDataInflationFatFoldGap);
            SubscribeSettingJson(_cfgDataInflationRoundness);
            SubscribeSettingJson(_cfgDataInflationDrop);
            SubscribeSettingJson(_cfgDataClothingOffsetVersion);
            SubscribeSettingJson(_cfgDataPluginVersion);

            SubscribeSettingJson(_cfgLogEnabled);
            SubscribeSettingJson(_cfgVerboseLog);

            SubscribeSettingJson(_cfgPresetSelectedSlot);
            SubscribeSettingJson(_cfgPresetName);

            SubscribeSettingJson(_cfgBellyEnabled);
            SubscribeSettingJson(_cfgBellyMinInflationSize);
            SubscribeSettingJson(_cfgBellyMaxInflationSize);
            SubscribeSettingJson(_cfgBellyDistanceCutPercent);
            SubscribeSettingJson(_cfgBellyDistanceMinMeters);
            SubscribeSettingJson(_cfgBellyDistanceMaxMeters);
            SubscribeSettingJson(_cfgBellyDistanceSmoothing);
            SubscribeSettingJson(_cfgBellyDistanceAnalyzeTurns);
            SubscribeSettingJson(_cfgBellyEaseUp);
            SubscribeSettingJson(_cfgBellyEaseDown);
        }

        private void SubscribeSettingJson<T>(ConfigEntry<T> entry)
        {
            if (entry == null)
                return;
            entry.SettingChanged += OnSettingJsonSourceChanged;
        }

        private void OnSettingJsonSourceChanged(object sender, EventArgs e)
        {
            if (_isApplyingSettingJson || _isSavingSettingJson)
                return;

            BridgeSettingSnapshot snapshot = BuildSettingSnapshotFromCurrent();
            WriteSettingSnapshotSafe(snapshot, true);
        }

        private void ApplySettingSnapshot(BridgeSettingSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            _isApplyingSettingJson = true;
            try
            {
                _cfgEnabled.Value = snapshot.GeneralEnabled;
                _cfgApplyIntervalSeconds.Value = Mathf.Clamp(Round2(snapshot.GeneralApplyIntervalSeconds), 0.1f, 5f);

                _cfgDataGameplayEnabled.Value = snapshot.DataGameplayEnabled;
                _cfgDataInflationMoveY.Value = Mathf.Clamp(Round2(snapshot.DataInflationMoveY), -5f, 5f);
                _cfgDataInflationMoveZ.Value = Mathf.Clamp(Round2(snapshot.DataInflationMoveZ), -5f, 5f);
                _cfgDataInflationStretchX.Value = Mathf.Clamp(Round2(snapshot.DataInflationStretchX), -5f, 5f);
                _cfgDataInflationStretchY.Value = Mathf.Clamp(Round2(snapshot.DataInflationStretchY), -5f, 5f);
                _cfgDataInflationShiftY.Value = Mathf.Clamp(Round2(snapshot.DataInflationShiftY), -5f, 5f);
                _cfgDataInflationShiftZ.Value = Mathf.Clamp(Round2(snapshot.DataInflationShiftZ), -5f, 5f);
                _cfgDataInflationTaperY.Value = Mathf.Clamp(Round2(snapshot.DataInflationTaperY), -5f, 5f);
                _cfgDataInflationTaperZ.Value = Mathf.Clamp(Round2(snapshot.DataInflationTaperZ), -5f, 5f);
                _cfgDataInflationMultiplier.Value = Mathf.Clamp(Round2(snapshot.DataInflationMultiplier), -5f, 5f);
                _cfgDataInflationClothOffset.Value = Mathf.Clamp(Round2(snapshot.DataInflationClothOffset), -5f, 5f);
                _cfgDataInflationFatFold.Value = Mathf.Clamp(Round2(snapshot.DataInflationFatFold), -5f, 5f);
                _cfgDataInflationFatFoldHeight.Value = Mathf.Clamp(Round2(snapshot.DataInflationFatFoldHeight), -5f, 5f);
                _cfgDataInflationFatFoldGap.Value = Mathf.Clamp(Round2(snapshot.DataInflationFatFoldGap), -5f, 5f);
                _cfgDataInflationRoundness.Value = Mathf.Clamp(Round2(snapshot.DataInflationRoundness), -5f, 5f);
                _cfgDataInflationDrop.Value = Mathf.Clamp(Round2(snapshot.DataInflationDrop), -5f, 5f);
                _cfgDataClothingOffsetVersion.Value = Mathf.Clamp(snapshot.DataClothingOffsetVersion, 0, 9);
                _cfgDataPluginVersion.Value = snapshot.DataPluginVersion ?? string.Empty;

                _cfgLogEnabled.Value = snapshot.LoggingEnableLog;
                _cfgVerboseLog.Value = snapshot.LoggingVerboseLog;

                _cfgPresetSelectedSlot.Value = Mathf.Clamp(snapshot.PresetSelectedSlot, 1, 20);
                _cfgPresetName.Value = snapshot.PresetName ?? string.Empty;

                _cfgBellyEnabled.Value = snapshot.BellyEnabled;
                _cfgBellyMinInflationSize.Value = Mathf.Clamp(Round2(snapshot.BellyMinInflationSize), 0f, 40f);
                _cfgBellyMaxInflationSize.Value = Mathf.Clamp(Round2(snapshot.BellyMaxInflationSize), 0f, 40f);
                _cfgBellyDistanceCutPercent.Value = Mathf.Clamp01(Round2(snapshot.BellyDistanceCutPercent));
                _cfgBellyDistanceMinMeters.Value = Mathf.Clamp(Round2(snapshot.BellyDistanceMinMeters), 0f, 2f);
                _cfgBellyDistanceMaxMeters.Value = Mathf.Clamp(Round2(snapshot.BellyDistanceMaxMeters), 0f, 2f);
                _cfgBellyDistanceSmoothing.Value = Mathf.Clamp01(Round2(snapshot.BellyDistanceSmoothing));
                _cfgBellyDistanceAnalyzeTurns.Value = Mathf.Clamp(snapshot.BellyDistanceAnalyzeTurns, 1, 20);
                _cfgBellyEaseUp.Value = NormalizeEaseName(snapshot.BellyEaseUp, "easeOut");
                _cfgBellyEaseDown.Value = NormalizeEaseName(snapshot.BellyEaseDown, "easeIn");
            }
            finally
            {
                _isApplyingSettingJson = false;
            }
        }

        private BridgeSettingSnapshot BuildSettingSnapshotFromCurrent()
        {
            return new BridgeSettingSnapshot
            {
                GeneralEnabled = _cfgEnabled.Value,
                GeneralApplyIntervalSeconds = Round2(_cfgApplyIntervalSeconds.Value),

                DataGameplayEnabled = _cfgDataGameplayEnabled.Value,
                DataInflationMoveY = Round2(_cfgDataInflationMoveY.Value),
                DataInflationMoveZ = Round2(_cfgDataInflationMoveZ.Value),
                DataInflationStretchX = Round2(_cfgDataInflationStretchX.Value),
                DataInflationStretchY = Round2(_cfgDataInflationStretchY.Value),
                DataInflationShiftY = Round2(_cfgDataInflationShiftY.Value),
                DataInflationShiftZ = Round2(_cfgDataInflationShiftZ.Value),
                DataInflationTaperY = Round2(_cfgDataInflationTaperY.Value),
                DataInflationTaperZ = Round2(_cfgDataInflationTaperZ.Value),
                DataInflationMultiplier = Round2(_cfgDataInflationMultiplier.Value),
                DataInflationClothOffset = Round2(_cfgDataInflationClothOffset.Value),
                DataInflationFatFold = Round2(_cfgDataInflationFatFold.Value),
                DataInflationFatFoldHeight = Round2(_cfgDataInflationFatFoldHeight.Value),
                DataInflationFatFoldGap = Round2(_cfgDataInflationFatFoldGap.Value),
                DataInflationRoundness = Round2(_cfgDataInflationRoundness.Value),
                DataInflationDrop = Round2(_cfgDataInflationDrop.Value),
                DataClothingOffsetVersion = _cfgDataClothingOffsetVersion.Value,
                DataPluginVersion = _cfgDataPluginVersion.Value ?? string.Empty,

                LoggingEnableLog = _cfgLogEnabled.Value,
                LoggingVerboseLog = _cfgVerboseLog.Value,

                PresetSelectedSlot = _cfgPresetSelectedSlot != null ? _cfgPresetSelectedSlot.Value : 1,
                PresetName = _cfgPresetName != null ? (_cfgPresetName.Value ?? string.Empty) : string.Empty,

                BellyEnabled = _cfgBellyEnabled != null && _cfgBellyEnabled.Value,
                BellyMinInflationSize = _cfgBellyMinInflationSize != null ? Round2(_cfgBellyMinInflationSize.Value) : 0f,
                BellyMaxInflationSize = _cfgBellyMaxInflationSize != null ? Round2(_cfgBellyMaxInflationSize.Value) : 5f,
                BellyDistanceCutPercent = _cfgBellyDistanceCutPercent != null ? Round2(_cfgBellyDistanceCutPercent.Value) : 0.9f,
                BellyDistanceMinMeters = _cfgBellyDistanceMinMeters != null ? Round2(_cfgBellyDistanceMinMeters.Value) : 0.04f,
                BellyDistanceMaxMeters = _cfgBellyDistanceMaxMeters != null ? Round2(_cfgBellyDistanceMaxMeters.Value) : 0.8f,
                BellyDistanceSmoothing = _cfgBellyDistanceSmoothing != null ? Round2(_cfgBellyDistanceSmoothing.Value) : 0.8f,
                BellyDistanceAnalyzeTurns = _cfgBellyDistanceAnalyzeTurns != null ? _cfgBellyDistanceAnalyzeTurns.Value : 10,
                BellyEaseUp = _cfgBellyEaseUp != null ? NormalizeEaseName(_cfgBellyEaseUp.Value, "easeOut") : "easeOut",
                BellyEaseDown = _cfgBellyEaseDown != null ? NormalizeEaseName(_cfgBellyEaseDown.Value, "easeIn") : "easeIn"
            };
        }

        private static BridgeSettingSnapshot BuildHardcodedDefaultSettingSnapshot()
        {
            return new BridgeSettingSnapshot
            {
                GeneralEnabled = true,
                GeneralApplyIntervalSeconds = 0.5f,

                DataGameplayEnabled = true,
                DataInflationMoveY = 0f,
                DataInflationMoveZ = 0f,
                DataInflationStretchX = 0f,
                DataInflationStretchY = 0f,
                DataInflationShiftY = 0f,
                DataInflationShiftZ = 0f,
                DataInflationTaperY = 0f,
                DataInflationTaperZ = 0f,
                DataInflationMultiplier = 0f,
                DataInflationClothOffset = 0f,
                DataInflationFatFold = 0f,
                DataInflationFatFoldHeight = 0f,
                DataInflationFatFoldGap = 0f,
                DataInflationRoundness = 0f,
                DataInflationDrop = 0f,
                DataClothingOffsetVersion = 1,
                DataPluginVersion = string.Empty,

                LoggingEnableLog = false,
                LoggingVerboseLog = false,

                PresetSelectedSlot = 1,
                PresetName = string.Empty,

                BellyEnabled = true,
                BellyMinInflationSize = 0f,
                BellyMaxInflationSize = 5f,
                BellyDistanceCutPercent = 0.9f,
                BellyDistanceMinMeters = 0.04f,
                BellyDistanceMaxMeters = 0.8f,
                BellyDistanceSmoothing = 0.8f,
                BellyDistanceAnalyzeTurns = 10,
                BellyEaseUp = "easeOut",
                BellyEaseDown = "easeIn"
            };
        }

        private void WriteSettingSnapshotSafe(BridgeSettingSnapshot snapshot, bool saveCfg)
        {
            if (snapshot == null || string.IsNullOrEmpty(_settingJsonPath))
                return;

            try
            {
                _isSavingSettingJson = true;

                string dir = Path.GetDirectoryName(_settingJsonPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                WriteSettingSnapshot(_settingJsonPath, snapshot);
                if (saveCfg)
                    SaveConfigSafely();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[setting] save failed path=" + _settingJsonPath + " err=" + ex.Message);
            }
            finally
            {
                _isSavingSettingJson = false;
            }
        }

        private static BridgeSettingSnapshot ReadSettingSnapshot(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return new BridgeSettingSnapshot();

            var serializer = new DataContractJsonSerializer(typeof(BridgeSettingSnapshot));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                object obj = serializer.ReadObject(ms);
                return obj as BridgeSettingSnapshot ?? new BridgeSettingSnapshot();
            }
        }

        private static void WriteSettingSnapshot(string path, BridgeSettingSnapshot snapshot)
        {
            var serializer = new DataContractJsonSerializer(typeof(BridgeSettingSnapshot));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, snapshot);
                string json = Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
        }

        private void SaveConfigSafely()
        {
            try
            {
                Config.Save();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[setting] cfg save failed err=" + ex.Message);
            }
        }

        private static float Round2(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}
