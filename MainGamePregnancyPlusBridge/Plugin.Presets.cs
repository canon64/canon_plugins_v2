using System;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGamePregnancyPlusBridge
{
    public sealed partial class Plugin
    {
        private ConfigEntry<int> _cfgPresetSelectedSlot;
        private ConfigEntry<string> _cfgPresetName;
        private ConfigEntry<bool> _cfgPresetSaveNow;
        private ConfigEntry<bool> _cfgPresetLoadNow;
        private ConfigEntry<bool> _cfgPresetResetNow;

        private PresetStore _presetStore;
        private bool _presetCommandGuard;

        private string _presetPopupMessage = string.Empty;
        private float _presetPopupUntilUnscaled;
        private Color _presetPopupColor = new Color(0.15f, 0.75f, 0.25f, 0.92f);
        private GUIStyle _presetPopupStyle;

        private void InitializePresetSystem(string pluginDir)
        {
            string presetPath = System.IO.Path.Combine(pluginDir, "MainGamePregnancyPlusBridgePresets.json");
            _presetStore = new PresetStore(presetPath, LogInfo, LogWarn);

            _cfgPresetSelectedSlot = Config.Bind(
                "20.Preset",
                "SelectedSlot",
                1,
                new ConfigDescription(
                    "対象プリセットスロット番号",
                    new AcceptableValueRange<int>(1, 20),
                    new ConfigurationManager.ConfigurationManagerAttributes { Order = 899 }));

            _cfgPresetName = Config.Bind(
                "20.Preset",
                "PresetName",
                string.Empty,
                new ConfigDescription(
                    "保存/読込に使うプリセット名（空欄可）",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes { Order = 898 }));

            _cfgPresetSaveNow = Config.Bind(
                "20.Preset",
                "SavePresetNow",
                false,
                new ConfigDescription(
                    "選択スロットへ保存（ボタン）",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 897,
                        HideDefaultButton = true,
                        CustomDrawer = DrawPresetSaveButton
                    }));

            _cfgPresetLoadNow = Config.Bind(
                "20.Preset",
                "LoadPresetNow",
                false,
                new ConfigDescription(
                    "選択スロットを読込（ボタン）",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 896,
                        HideDefaultButton = true,
                        CustomDrawer = DrawPresetLoadButton
                    }));

            _cfgPresetResetNow = Config.Bind(
                "20.Preset",
                "ResetToZeroNow",
                false,
                new ConfigDescription(
                    "全パラメータを0にリセット（ボタン）",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 895,
                        HideDefaultButton = true,
                        CustomDrawer = DrawPresetResetButton
                    }));

            // Fallback: if someone toggles these from raw config, still execute once.
            _cfgPresetSaveNow.SettingChanged += OnPresetSaveRequested;
            _cfgPresetLoadNow.SettingChanged += OnPresetLoadRequested;
            _cfgPresetResetNow.SettingChanged += OnPresetResetRequested;

            if (_presetStore.TryGet(_cfgPresetSelectedSlot.Value, out PregnancyPlusPreset existing))
            {
                if (!string.IsNullOrWhiteSpace(existing.Name))
                    _cfgPresetName.Value = existing.Name;
            }
        }

        private void OnPresetSaveRequested(object sender, EventArgs e)
        {
            if (_presetCommandGuard || _cfgPresetSaveNow == null || !_cfgPresetSaveNow.Value)
                return;

            try
            {
                ExecutePresetSave();
            }
            finally
            {
                ResetPresetTriggerEntry(_cfgPresetSaveNow);
            }
        }

        private void OnPresetLoadRequested(object sender, EventArgs e)
        {
            if (_presetCommandGuard || _cfgPresetLoadNow == null || !_cfgPresetLoadNow.Value)
                return;

            try
            {
                ExecutePresetLoad();
            }
            finally
            {
                ResetPresetTriggerEntry(_cfgPresetLoadNow);
            }
        }

        private void OnPresetResetRequested(object sender, EventArgs e)
        {
            if (_presetCommandGuard || _cfgPresetResetNow == null || !_cfgPresetResetNow.Value)
                return;

            try
            {
                ExecutePresetReset();
            }
            finally
            {
                ResetPresetTriggerEntry(_cfgPresetResetNow);
            }
        }

        private void ExecutePresetReset()
        {
            if (_presetCommandGuard)
                return;

            _presetCommandGuard = true;
            try
            {
                ResetAllToZero();
            }
            finally
            {
                _presetCommandGuard = false;
            }
        }

        private void ResetAllToZero()
        {
            _cfgDataGameplayEnabled.Value = true;
            _cfgDataInflationMoveY.Value = 0f;
            _cfgDataInflationMoveZ.Value = 0f;
            _cfgDataInflationStretchX.Value = 0f;
            _cfgDataInflationStretchY.Value = 0f;
            _cfgDataInflationShiftY.Value = 0f;
            _cfgDataInflationShiftZ.Value = 0f;
            _cfgDataInflationTaperY.Value = 0f;
            _cfgDataInflationTaperZ.Value = 0f;
            _cfgDataInflationMultiplier.Value = 0f;
            _cfgDataInflationClothOffset.Value = 0f;
            _cfgDataInflationFatFold.Value = 0f;
            _cfgDataInflationFatFoldHeight.Value = 0f;
            _cfgDataInflationFatFoldGap.Value = 0f;
            _cfgDataInflationRoundness.Value = 0f;
            _cfgDataInflationDrop.Value = 0f;
            _dirty = true;
            ShowPresetPopup("全パラメータを0にリセットしました", false);
            LogInfo("preset reset to zero");
        }

        private void DrawPresetSaveButton(ConfigEntryBase entryBase)
        {
            if (GUILayout.Button("SAVE PRESET", GUILayout.MinWidth(120f)))
                ExecutePresetSave();
        }

        private void DrawPresetLoadButton(ConfigEntryBase entryBase)
        {
            if (GUILayout.Button("LOAD PRESET", GUILayout.MinWidth(120f)))
                ExecutePresetLoad();
        }

        private void DrawPresetResetButton(ConfigEntryBase entryBase)
        {
            if (GUILayout.Button("全パラメータを0にリセット", GUILayout.MinWidth(160f)))
                ExecutePresetReset();
        }

        private void ExecutePresetSave()
        {
            if (_presetCommandGuard)
                return;

            _presetCommandGuard = true;
            try
            {
                SaveCurrentToPreset();
            }
            finally
            {
                _presetCommandGuard = false;
            }
        }

        private void ExecutePresetLoad()
        {
            if (_presetCommandGuard)
                return;

            _presetCommandGuard = true;
            try
            {
                LoadPresetToCurrent();
            }
            finally
            {
                _presetCommandGuard = false;
            }
        }

        private void ResetPresetTriggerEntry(ConfigEntry<bool> triggerEntry)
        {
            if (triggerEntry == null)
                return;

            _presetCommandGuard = true;
            try
            {
                triggerEntry.Value = false;
            }
            finally
            {
                _presetCommandGuard = false;
            }
        }

        private void SaveCurrentToPreset()
        {
            int slot = Mathf.Clamp(_cfgPresetSelectedSlot.Value, 1, 20);

            string name = (_cfgPresetName.Value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                if (_presetStore.TryGet(slot, out PregnancyPlusPreset oldPreset) && !string.IsNullOrWhiteSpace(oldPreset.Name))
                    name = oldPreset.Name;
                else
                    name = "Preset " + slot.ToString("00");
            }

            PregnancyPlusPreset preset = new PregnancyPlusPreset
            {
                Slot = slot,
                Name = name,
                GameplayEnabled = _cfgDataGameplayEnabled.Value,
                InflationMoveY = _cfgDataInflationMoveY.Value,
                InflationMoveZ = _cfgDataInflationMoveZ.Value,
                InflationStretchX = _cfgDataInflationStretchX.Value,
                InflationStretchY = _cfgDataInflationStretchY.Value,
                InflationShiftY = _cfgDataInflationShiftY.Value,
                InflationShiftZ = _cfgDataInflationShiftZ.Value,
                InflationTaperY = _cfgDataInflationTaperY.Value,
                InflationTaperZ = _cfgDataInflationTaperZ.Value,
                InflationMultiplier = _cfgDataInflationMultiplier.Value,
                InflationClothOffset = _cfgDataInflationClothOffset.Value,
                InflationFatFold = _cfgDataInflationFatFold.Value,
                InflationFatFoldHeight = _cfgDataInflationFatFoldHeight.Value,
                InflationFatFoldGap = _cfgDataInflationFatFoldGap.Value,
                InflationRoundness = _cfgDataInflationRoundness.Value,
                InflationDrop = _cfgDataInflationDrop.Value,
                ClothingOffsetVersion = _cfgDataClothingOffsetVersion.Value,
                PluginVersion = string.IsNullOrWhiteSpace(_cfgDataPluginVersion.Value) ? Version : _cfgDataPluginVersion.Value
            };

            _presetStore.Upsert(preset);
            _presetStore.Save();

            _cfgPresetName.Value = preset.Name;
            LogInfo("preset saved slot=" + slot + " name=" + preset.Name);
            ShowPresetPopup("プリセット保存: [" + slot + "] " + preset.Name, false);
        }

        private void LoadPresetToCurrent()
        {
            int slot = Mathf.Clamp(_cfgPresetSelectedSlot.Value, 1, 20);
            if (!_presetStore.TryGet(slot, out PregnancyPlusPreset preset))
            {
                string failText = "プリセット読込失敗: スロット" + slot + "は空です";
                LogWarn("preset load failed: slot " + slot + " is empty");
                ShowPresetPopup(failText, true);
                return;
            }

            _cfgDataGameplayEnabled.Value = preset.GameplayEnabled;
            _cfgDataInflationMoveY.Value = preset.InflationMoveY;
            _cfgDataInflationMoveZ.Value = preset.InflationMoveZ;
            _cfgDataInflationStretchX.Value = preset.InflationStretchX;
            _cfgDataInflationStretchY.Value = preset.InflationStretchY;
            _cfgDataInflationShiftY.Value = preset.InflationShiftY;
            _cfgDataInflationShiftZ.Value = preset.InflationShiftZ;
            _cfgDataInflationTaperY.Value = preset.InflationTaperY;
            _cfgDataInflationTaperZ.Value = preset.InflationTaperZ;
            _cfgDataInflationMultiplier.Value = preset.InflationMultiplier;
            _cfgDataInflationClothOffset.Value = preset.InflationClothOffset;
            _cfgDataInflationFatFold.Value = preset.InflationFatFold;
            _cfgDataInflationFatFoldHeight.Value = preset.InflationFatFoldHeight;
            _cfgDataInflationFatFoldGap.Value = preset.InflationFatFoldGap;
            _cfgDataInflationRoundness.Value = preset.InflationRoundness;
            _cfgDataInflationDrop.Value = preset.InflationDrop;
            _cfgDataClothingOffsetVersion.Value = preset.ClothingOffsetVersion;
            _cfgDataPluginVersion.Value = preset.PluginVersion ?? string.Empty;
            _cfgPresetName.Value = preset.Name ?? string.Empty;

            _dirty = true;
            LogInfo("preset loaded slot=" + slot + " name=" + (preset.Name ?? string.Empty));
            ShowPresetPopup("プリセット読込: [" + slot + "] " + (_cfgPresetName.Value ?? string.Empty), false);
        }

        private void ShowPresetPopup(string message, bool isError)
        {
            _presetPopupMessage = message ?? string.Empty;
            _presetPopupUntilUnscaled = Time.unscaledTime + 2.0f;
            _presetPopupColor = isError
                ? new Color(0.90f, 0.25f, 0.25f, 0.92f)
                : new Color(0.15f, 0.75f, 0.25f, 0.92f);
        }

        private void EnsurePresetPopupStyle()
        {
            if (_presetPopupStyle != null)
                return;

            _presetPopupStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                wordWrap = true,
                padding = new RectOffset(14, 14, 8, 8)
            };
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_presetPopupMessage))
                return;
            if (Time.unscaledTime > _presetPopupUntilUnscaled)
                return;

            EnsurePresetPopupStyle();

            float width = Mathf.Min(Screen.width * 0.6f, 560f);
            var rect = new Rect((Screen.width - width) * 0.5f, 24f, width, 42f);
            Color prevColor = GUI.color;
            GUI.color = _presetPopupColor;
            GUI.Box(rect, _presetPopupMessage, _presetPopupStyle);
            GUI.color = prevColor;
        }
    }
}
