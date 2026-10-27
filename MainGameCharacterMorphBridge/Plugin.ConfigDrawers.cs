using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameCharacterMorphBridge
{
    public sealed partial class Plugin
    {
        private sealed class FloatSliderState
        {
            internal bool Dragging;
            internal float StartValue;
            internal float PreviewValue;
            internal float LastAppliedValue = float.NaN;
        }

        private sealed class IntSliderState
        {
            internal bool Dragging;
            internal int StartValue;
            internal int PreviewValue;
            internal int LastAppliedValue = int.MinValue;
        }

        private readonly Dictionary<string, FloatSliderState> _floatSliderStates =
            new Dictionary<string, FloatSliderState>(StringComparer.Ordinal);

        private readonly Dictionary<string, IntSliderState> _intSliderStates =
            new Dictionary<string, IntSliderState>(StringComparer.Ordinal);

        private ConfigDescription BuildFloatSliderDescription(
            string description,
            int order,
            float min,
            float max,
            string format,
            Action<ConfigEntryBase> drawer)
        {
            return new ConfigDescription(
                description,
                new AcceptableValueRange<float>(min, max),
                new ConfigurationManager.ConfigurationManagerAttributes
                {
                    Order = order,
                    CustomDrawer = drawer
                });
        }

        private ConfigDescription BuildIntSliderDescription(
            string description,
            int order,
            int min,
            int max,
            Action<ConfigEntryBase> drawer)
        {
            return new ConfigDescription(
                description,
                new AcceptableValueRange<int>(min, max),
                new ConfigurationManager.ConfigurationManagerAttributes
                {
                    Order = order,
                    CustomDrawer = drawer
                });
        }

        /// <summary>一覧など、値を持たず描画だけしたい項目用。設定名と既定値ボタンは隠す。</summary>
        private ConfigDescription BuildListDescription(string description, int order, Action<ConfigEntryBase> drawer)
        {
            return new ConfigDescription(
                description,
                null,
                new ConfigurationManager.ConfigurationManagerAttributes
                {
                    Order = order,
                    HideSettingName = true,
                    HideDefaultButton = true,
                    CustomDrawer = drawer
                });
        }

        private ConfigDescription BuildButtonDescription(string label, int order, Action<ConfigEntryBase> onClick)
        {
            var attrs = new ConfigurationManager.ConfigurationManagerAttributes
            {
                Order = order,
                HideSettingName = true,
                HideDefaultButton = true
            };
            attrs.CustomDrawer = entry =>
            {
                bool prev = GUI.enabled;
                GUI.enabled = IsEnabled();
                if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
                    onClick(entry);
                GUI.enabled = prev;
            };
            return new ConfigDescription(string.Empty, null, attrs);
        }

        private void DrawTargetFemaleIndexSlider(ConfigEntryBase entry)
        {
            DrawIntCommitSlider(entry, 0, 1, (value, writeLog) =>
            {
                _settings.TargetFemaleIndex = value;
                _originalSnapshot = null;
                _targetSnapshot = null;
                _originalFemaleIndex = -1;
                _targetFemaleIndex = -1;
                SetEntryValue(_cfgTargetFemaleIndex, value);
                if (writeLog)
                    LogExecution("target female index committed: " + value);
            });
        }

        private void DrawBlendSlider(ConfigEntryBase entry)
        {
            DrawFloatCommitSlider(entry, 0f, 1f, "0.00", (value, writeLog) =>
            {
                if (writeLog)
                    SetEntryValue(_cfgBlend, value);
                ApplyBlend(value, TargetFemaleIndex(), "config slider", writeLog);
            });
        }

        private void DrawHeightSlider(ConfigEntryBase entry)
        {
            DrawFloatCommitSlider(entry, 0f, 1f, "0.00", (value, writeLog) =>
            {
                if (writeLog)
                    SetEntryValue(_cfgHeight, value);
                SetBodyShape(0, value, TargetFemaleIndex(), "config height slider", writeLog);
            });
        }

        private void DrawBreastSlider(ConfigEntryBase entry)
        {
            DrawFloatCommitSlider(entry, 0f, 1f, "0.00", (value, writeLog) =>
            {
                if (writeLog)
                    SetEntryValue(_cfgBreast, value);
                SetBodyShape(4, value, TargetFemaleIndex(), "config breast slider", writeLog);
            });
        }

        private void DrawBodyShapeIndexSlider(ConfigEntryBase entry)
        {
            DrawIntCommitSlider(entry, 0, 43, (value, writeLog) =>
            {
                _settings.BodyShapeIndex = value;
                if (writeLog)
                {
                    SetEntryValue(_cfgBodyShapeIndex, value);
                    LogExecution("body shape index committed: " + value);
                }
            });
        }

        private void DrawBodyShapeValueSlider(ConfigEntryBase entry)
        {
            DrawFloatCommitSlider(entry, 0f, 1f, "0.00", (value, writeLog) =>
            {
                if (writeLog)
                    SetEntryValue(_cfgBodyShapeValue, value);
                SetBodyShape(_settings.BodyShapeIndex, value, TargetFemaleIndex(), "config body shape slider", writeLog);
            });
        }

        private void DrawFaceShapeIndexSlider(ConfigEntryBase entry)
        {
            DrawIntCommitSlider(entry, 0, 51, (value, writeLog) =>
            {
                _settings.FaceShapeIndex = value;
                if (writeLog)
                {
                    SetEntryValue(_cfgFaceShapeIndex, value);
                    LogExecution("face shape index committed: " + value);
                }
            });
        }

        private void DrawFaceShapeValueSlider(ConfigEntryBase entry)
        {
            DrawFloatCommitSlider(entry, 0f, 1f, "0.00", (value, writeLog) =>
            {
                if (writeLog)
                    SetEntryValue(_cfgFaceShapeValue, value);
                SetFaceShape(_settings.FaceShapeIndex, value, TargetFemaleIndex(), "config face shape slider", writeLog);
            });
        }

        private void DrawFloatCommitSlider(
            ConfigEntryBase entryBase,
            float min,
            float max,
            string format,
            Action<float, bool> commit)
        {
            var entry = entryBase as ConfigEntry<float>;
            if (entry == null)
                return;

            string key = EntryKey(entryBase);
            FloatSliderState state;
            if (!_floatSliderStates.TryGetValue(key, out state))
            {
                state = new FloatSliderState { PreviewValue = entry.Value };
                _floatSliderStates[key] = state;
            }

            GUILayout.BeginHorizontal();
            Rect rect = GUILayoutUtility.GetRect(130f, 18f, GUILayout.ExpandWidth(true));
            Event ev = Event.current;
            float current = PluginSettings.Clamp01(entry.Value);
            float shown = state.Dragging ? state.PreviewValue : current;
            float sliderValue = PluginSettings.Round2(Mathf.Clamp(GUI.HorizontalSlider(rect, shown, min, max), min, max));

            if (!state.Dragging)
            {
                state.PreviewValue = current;
                state.LastAppliedValue = current;
            }

            if (ev.type == EventType.MouseDown && ev.button == 0 && rect.Contains(ev.mousePosition))
            {
                state.Dragging = true;
                state.StartValue = current;
                state.PreviewValue = sliderValue;
                state.LastAppliedValue = float.NaN;
                LogExecution("slider drag start: " + entry.Definition.Key + "=" + state.StartValue.ToString(format));
            }

            if (state.Dragging && Math.Abs(sliderValue - state.PreviewValue) > 0.0001f)
                state.PreviewValue = sliderValue;

            if (state.Dragging && Math.Abs(sliderValue - state.LastAppliedValue) > 0.0001f)
            {
                state.LastAppliedValue = sliderValue;
                commit(sliderValue, false);
            }

            if (state.Dragging && (ev.rawType == EventType.MouseUp || ev.type == EventType.MouseUp))
            {
                float finalValue = PluginSettings.Round2(Mathf.Clamp(state.PreviewValue, min, max));
                state.Dragging = false;
                state.PreviewValue = finalValue;
                state.LastAppliedValue = finalValue;
                LogExecution("slider drag release: " + entry.Definition.Key + "=" + finalValue.ToString(format));
                commit(finalValue, true);
            }

            float labelValue = state.Dragging ? state.PreviewValue : current;
            GUILayout.Label(labelValue.ToString(format), GUILayout.Width(52f));
            GUILayout.EndHorizontal();
        }

        private void DrawIntCommitSlider(ConfigEntryBase entryBase, int min, int max, Action<int, bool> commit)
        {
            var entry = entryBase as ConfigEntry<int>;
            if (entry == null)
                return;

            string key = EntryKey(entryBase);
            IntSliderState state;
            if (!_intSliderStates.TryGetValue(key, out state))
            {
                state = new IntSliderState { PreviewValue = entry.Value };
                _intSliderStates[key] = state;
            }

            GUILayout.BeginHorizontal();
            Rect rect = GUILayoutUtility.GetRect(130f, 18f, GUILayout.ExpandWidth(true));
            Event ev = Event.current;
            int current = PluginSettings.ClampInt(entry.Value, min, max);
            int shown = state.Dragging ? state.PreviewValue : current;
            int sliderValue = PluginSettings.ClampInt(Mathf.RoundToInt(GUI.HorizontalSlider(rect, shown, min, max)), min, max);

            if (!state.Dragging)
            {
                state.PreviewValue = current;
                state.LastAppliedValue = current;
            }

            if (ev.type == EventType.MouseDown && ev.button == 0 && rect.Contains(ev.mousePosition))
            {
                state.Dragging = true;
                state.StartValue = current;
                state.PreviewValue = sliderValue;
                state.LastAppliedValue = int.MinValue;
                LogExecution("slider drag start: " + entry.Definition.Key + "=" + state.StartValue);
            }

            if (state.Dragging && sliderValue != state.PreviewValue)
                state.PreviewValue = sliderValue;

            if (state.Dragging && sliderValue != state.LastAppliedValue)
            {
                state.LastAppliedValue = sliderValue;
                commit(sliderValue, false);
            }

            if (state.Dragging && (ev.rawType == EventType.MouseUp || ev.type == EventType.MouseUp))
            {
                int finalValue = PluginSettings.ClampInt(state.PreviewValue, min, max);
                state.Dragging = false;
                state.PreviewValue = finalValue;
                state.LastAppliedValue = finalValue;
                LogExecution("slider drag release: " + entry.Definition.Key + "=" + finalValue);
                commit(finalValue, true);
            }

            int labelValue = state.Dragging ? state.PreviewValue : current;
            GUILayout.Label(labelValue.ToString(), GUILayout.Width(52f));
            GUILayout.EndHorizontal();
        }

        private static string EntryKey(ConfigEntryBase entry)
        {
            return entry.Definition.Section + "/" + entry.Definition.Key;
        }

        private void SetEntryValue(ConfigEntry<float> entry, float value)
        {
            if (entry == null)
                return;

            float rounded = PluginSettings.Round2(PluginSettings.Clamp01(value));
            if (Math.Abs(entry.Value - rounded) > 0.0001f)
                entry.Value = rounded;
        }

        private void SetEntryValue(ConfigEntry<int> entry, int value)
        {
            if (entry == null || entry.Value == value)
                return;

            entry.Value = value;
        }
    }
}
