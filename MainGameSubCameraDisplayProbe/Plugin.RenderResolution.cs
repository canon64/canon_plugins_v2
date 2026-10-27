using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private void DrawRenderResolutionUi()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Resolution", GUILayout.Width(90f));
            string label = GetCurrentRenderResolutionLabel();
            if (GUILayout.Button((_renderResolutionDropdownOpen ? "▲ " : "▼ ") + label, GUILayout.Height(24f)))
                _renderResolutionDropdownOpen = !_renderResolutionDropdownOpen;
            GUILayout.EndHorizontal();

            if (_renderResolutionDropdownOpen)
                DrawRenderResolutionDropdown();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Custom", GUILayout.Width(90f));
            _customRenderPresetNameBuffer = GUILayout.TextField(_customRenderPresetNameBuffer ?? string.Empty, GUILayout.Width(110f));
            _customRenderWidth = DrawIntField("render.custom.width", "W", _customRenderWidth, 64, 4096);
            _customRenderHeight = DrawIntField("render.custom.height", "H", _customRenderHeight, 64, 4096);
            if (GUILayout.Button("適用", GUILayout.Width(54f), GUILayout.Height(22f)))
                ApplyRenderResolution(_customRenderWidth, _customRenderHeight, "custom");
            if (GUILayout.Button("追加", GUILayout.Width(54f), GUILayout.Height(22f)))
                AddCustomRenderResolutionPreset();
            GUILayout.EndHorizontal();
        }

        private void DrawRenderResolutionDropdown()
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("固定プリセット");
            for (int i = 0; i < BuiltInRenderResolutionPresets.Length; i++)
                DrawRenderResolutionPresetButton(BuiltInRenderResolutionPresets[i], "builtin");

            RenderResolutionPreset[] customPresets = _settings.RenderCustomPresets ?? new RenderResolutionPreset[0];
            if (customPresets.Length > 0)
            {
                GUILayout.Space(4f);
                GUILayout.Label("Custom");
                for (int i = 0; i < customPresets.Length; i++)
                    DrawRenderResolutionPresetButton(customPresets[i], "custom-preset");
            }

            if (!TryFindCurrentRenderResolutionPreset(out _))
            {
                GUILayout.Space(4f);
                GUILayout.Label("現在: Custom " + _settings.RenderWidth + "x" + _settings.RenderHeight);
            }
            GUILayout.EndVertical();
        }

        private void DrawRenderResolutionPresetButton(RenderResolutionPreset preset, string source)
        {
            if (preset == null)
                return;

            bool selected = preset.Width == _settings.RenderWidth && preset.Height == _settings.RenderHeight;
            string prefix = selected ? "● " : "  ";
            if (GUILayout.Button(prefix + FormatRenderResolutionPreset(preset), GUILayout.Height(22f)))
                ApplyRenderResolution(preset.Width, preset.Height, source + ":" + preset.Name);
        }

        private int DrawIntField(string key, string label, int value, int min, int max)
        {
            GUILayout.Label(label, GUILayout.Width(14f));
            string raw = DrawNamedNumericField(key, value, 60f);
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return Mathf.Clamp(parsed, min, max);
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedFloat))
                return Mathf.Clamp(Mathf.RoundToInt(parsedFloat), min, max);
            return value;
        }

        private void AddCustomRenderResolutionPreset()
        {
            int width = Mathf.Clamp(_customRenderWidth, 64, 4096);
            int height = Mathf.Clamp(_customRenderHeight, 64, 4096);
            string name = string.IsNullOrWhiteSpace(_customRenderPresetNameBuffer)
                ? width + "x" + height
                : _customRenderPresetNameBuffer.Trim();

            List<RenderResolutionPreset> presets = new List<RenderResolutionPreset>(_settings.RenderCustomPresets ?? new RenderResolutionPreset[0]);
            RenderResolutionPreset next = new RenderResolutionPreset { Name = name, Width = width, Height = height };
            int replaceIndex = presets.FindIndex(p => p != null && string.Equals(p.Name ?? string.Empty, name, StringComparison.Ordinal));
            if (replaceIndex >= 0)
                presets[replaceIndex] = next;
            else
                presets.Add(next);

            _settings.RenderCustomPresets = presets.ToArray();
            ApplyRenderResolution(width, height, "custom-preset:" + name);
            SetStatus("解像度プリセット追加: " + name);
        }

        private void ApplyRenderResolution(int width, int height, string source)
        {
            int nextWidth = Mathf.Clamp(width, 64, 4096);
            int nextHeight = Mathf.Clamp(height, 64, 4096);
            bool changed = _settings.RenderWidth != nextWidth || _settings.RenderHeight != nextHeight;

            _settings.RenderWidth = nextWidth;
            _settings.RenderHeight = nextHeight;
            _settings.DisplayWidth = SettingsStore.CalculateDisplayWidth(_settings);
            _customRenderWidth = nextWidth;
            _customRenderHeight = nextHeight;
            SetNumericBuffer("render.custom.width", nextWidth);
            SetNumericBuffer("render.custom.height", nextHeight);

            if (changed)
            {
                RecreateRenderTexture(source);
                SetStatus("解像度: " + nextWidth + "x" + nextHeight);
                LogInfo("render resolution changed source=" + source + " size=" + nextWidth + "x" + nextHeight);
            }
            else
            {
                ApplyDisplaySettings();
            }

            SaveSettings();
            _renderResolutionDropdownOpen = false;
        }

        private string GetCurrentRenderResolutionLabel()
        {
            if (TryFindCurrentRenderResolutionPreset(out RenderResolutionPreset preset))
                return FormatRenderResolutionPreset(preset);

            return "Custom " + _settings.RenderWidth + "x" + _settings.RenderHeight;
        }

        private bool TryFindCurrentRenderResolutionPreset(out RenderResolutionPreset preset)
        {
            for (int i = 0; i < BuiltInRenderResolutionPresets.Length; i++)
            {
                preset = BuiltInRenderResolutionPresets[i];
                if (preset.Width == _settings.RenderWidth && preset.Height == _settings.RenderHeight)
                    return true;
            }

            RenderResolutionPreset[] customPresets = _settings.RenderCustomPresets ?? new RenderResolutionPreset[0];
            for (int i = 0; i < customPresets.Length; i++)
            {
                preset = customPresets[i];
                if (preset != null && preset.Width == _settings.RenderWidth && preset.Height == _settings.RenderHeight)
                    return true;
            }

            preset = null;
            return false;
        }

        private static string FormatRenderResolutionPreset(RenderResolutionPreset preset)
        {
            if (preset == null)
                return "Custom";

            string name = string.IsNullOrWhiteSpace(preset.Name) ? "Custom" : preset.Name.Trim();
            return name + " (" + preset.Width + "x" + preset.Height + ")";
        }
    }
}
