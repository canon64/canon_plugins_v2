using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private const string BetterPenetrationGuid = "animal42069.KKSbetterpenetration";
        private ConfigEntry<float> _betterPenetrationTargetEntry;
        private ConfigFile _betterPenetrationConfig;
        private float _betterPenetrationOriginalTarget;
        private bool _betterPenetrationOverrideActive;
        private readonly Dictionary<string, string> _betterPenetrationTargetEdits = new Dictionary<string, string>();

        private bool TryResolveBetterPenetrationTarget()
        {
            if (_betterPenetrationTargetEntry != null && _betterPenetrationConfig != null) return true;
            if (Chainloader.PluginInfos == null ||
                !Chainloader.PluginInfos.TryGetValue(BetterPenetrationGuid, out var info) || info.Instance == null)
                return false;
            ConfigFile config = info.Instance.Config;
            if (!config.TryGetEntry<float>("Female Options", "Target Offset: Vagina Target", out var entry))
                return false;
            _betterPenetrationConfig = config;
            _betterPenetrationTargetEntry = entry;
            return true;
        }

        private void SetBetterPenetrationTargetWithoutSaving(float value)
        {
            if (_betterPenetrationTargetEntry == null || _betterPenetrationConfig == null) return;
            if (Mathf.Approximately(_betterPenetrationTargetEntry.Value, value)) return;
            bool saveOnSet = _betterPenetrationConfig.SaveOnConfigSet;
            try
            {
                _betterPenetrationConfig.SaveOnConfigSet = false;
                _betterPenetrationTargetEntry.Value = value;
            }
            finally
            {
                _betterPenetrationConfig.SaveOnConfigSet = saveOnSet;
            }
        }

        private void SyncBetterPenetrationTarget()
        {
            PresetEntry preset = _activePreset;
            if (preset == null || !preset.betterPenetrationTargetEnabled)
            {
                RestoreBetterPenetrationTarget();
                return;
            }
            if (!TryResolveBetterPenetrationTarget()) return;
            if (!_betterPenetrationOverrideActive)
            {
                _betterPenetrationOriginalTarget = _betterPenetrationTargetEntry.Value;
                _betterPenetrationOverrideActive = true;
            }
            SetBetterPenetrationTargetWithoutSaving(preset.betterPenetrationVaginaTarget);
        }

        private void RestoreBetterPenetrationTarget()
        {
            if (!_betterPenetrationOverrideActive) return;
            SetBetterPenetrationTargetWithoutSaving(_betterPenetrationOriginalTarget);
            _betterPenetrationOverrideActive = false;
        }

        private void DrawBetterPenetrationPresetEditor(PresetEntry preset)
        {
            bool enabled = GUILayout.Toggle(preset.betterPenetrationTargetEnabled,
                "Better Penetration: Vagina Target をこのプリセットで指定");
            if (enabled != preset.betterPenetrationTargetEnabled)
            {
                preset.betterPenetrationTargetConfigured = true;
                preset.betterPenetrationTargetEnabled = enabled;
                SavePresets();
                if (ReferenceEquals(_activePreset, preset)) SyncBetterPenetrationTarget();
            }
            if (!enabled) return;

            string key = "BpVaginaTarget_" + RuntimeHelpers.GetHashCode(preset);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Vagina Target", GUILayout.Width(105f));
            float display = Mathf.Clamp(preset.betterPenetrationVaginaTarget, -3f, 3f);
            float slider = GUILayout.HorizontalSlider(display, -3f, 3f, GUILayout.ExpandWidth(true));
            bool changed = false;
            if (Mathf.Abs(slider - display) > 0.00001f)
            {
                preset.betterPenetrationVaginaTarget = slider;
                _betterPenetrationTargetEdits.Remove(key);
                changed = true;
            }
            GUI.SetNextControlName(key);
            bool focused = GUI.GetNameOfFocusedControl() == key;
            string text = focused && _betterPenetrationTargetEdits.TryGetValue(key, out string edit)
                ? edit : preset.betterPenetrationVaginaTarget.ToString("F3", CultureInfo.InvariantCulture);
            string next = GUILayout.TextField(text, GUILayout.Width(72f));
            if (GUI.GetNameOfFocusedControl() == key)
            {
                _betterPenetrationTargetEdits[key] = next;
                if (next != text && float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                    && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                {
                    preset.betterPenetrationVaginaTarget = parsed;
                    changed = true;
                }
            }
            else _betterPenetrationTargetEdits.Remove(key);
            GUILayout.EndHorizontal();
            if (changed)
            {
                SaveMotionPresetMutation();
                if (ReferenceEquals(_activePreset, preset)) SyncBetterPenetrationTarget();
            }
        }
    }
}
