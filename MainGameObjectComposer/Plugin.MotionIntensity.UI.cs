using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private PresetEntry _expandedMotionPreset;
        private string _expandedMotionObjectId;
        private readonly Dictionary<string, string> _motionFieldEdits = new Dictionary<string, string>();

        private void DrawMotionIntensityPresetEditor(PresetEntry preset)
        {
            if (preset == null) return;
            if (GUILayout.Button((_expandedMotionPreset == preset ? "▼ " : "▶ ") + "発話によるドーナツ倍率"))
                _expandedMotionPreset = _expandedMotionPreset == preset ? null : preset;
            if (_expandedMotionPreset != preset) return;

            GUILayout.Label("倍率を適用する回転オブジェクトを選択（複数可）。未設定の段階はF1の共通倍率を使用。");
            GUILayout.Label("現在の段階: " + MotionLevelLabels[_motionIntensityLevel]
                + (ReferenceEquals(_activePreset, preset) ? "（この体位に反映中）" : "（この体位は未適用）"));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < MotionLevelLabels.Length; i++)
            {
                int levelIndex = i;
                if (GUILayout.Button((i == _motionIntensityLevel ? "●" : "○") + MotionLevelLabels[i]))
                    TrySetMotionIntensityLevel(levelIndex);
            }
            GUILayout.EndHorizontal();
            List<ManagedObjectData> objects = DecodeObjects(preset.loop1Json);
            bool found = false;
            foreach (ManagedObjectData obj in objects)
            {
                if (obj == null || !obj.isRotationObject || string.IsNullOrEmpty(obj.id)) continue;
                found = true;
                if (preset.motionIntensityTargetIds == null) preset.motionIntensityTargetIds = new List<string>();
                bool selected = preset.motionIntensityTargetIds.Contains(obj.id);
                GUILayout.BeginHorizontal();
                bool next = GUILayout.Toggle(selected, obj.name + " (" + obj.id.Substring(0, Math.Min(8, obj.id.Length)) + ")");
                if (next != selected)
                {
                    if (next) preset.motionIntensityTargetIds.Add(obj.id);
                    else preset.motionIntensityTargetIds.Remove(obj.id);
                    SavePresets();
                }
                if (next && GUILayout.Button(_expandedMotionObjectId == obj.id ? "閉じる" : "倍率", GUILayout.Width(55f)))
                    _expandedMotionObjectId = _expandedMotionObjectId == obj.id ? null : obj.id;
                GUILayout.EndHorizontal();

                if (next && _expandedMotionObjectId == obj.id)
                {
                    for (int level = 0; level < MotionLevelLabels.Length; level++)
                        DrawMotionIntensityRule(preset, obj.id, level);
                }
            }
            if (!found) GUILayout.Label("L1に回転オブジェクトがありません。");
        }

        private void DrawMotionIntensityRule(PresetEntry preset, string id, int level)
        {
            MotionIntensityRule rule = FindMotionRule(preset, id, level);
            bool hasOverride = rule != null;
            float commonRx = _motionCommonRx[level] != null ? _motionCommonRx[level].Value : DefaultMotionRx[level];
            float commonRz = _motionCommonRz[level] != null ? _motionCommonRz[level].Value : DefaultMotionRz[level];
            GUILayout.BeginVertical("box");
            bool nextOverride = GUILayout.Toggle(hasOverride, MotionLevelLabels[level] + " 個別設定");
            if (nextOverride != hasOverride)
            {
                if (nextOverride)
                {
                    rule = new MotionIntensityRule
                    {
                        objectId = id, level = level,
                        l1Rx = commonRx, l1Rz = commonRz, l2Rx = commonRx, l2Rz = commonRz
                    };
                    SetMotionRule(preset, rule);
                }
                else RemoveMotionRule(preset, id, level);
            }
            if (nextOverride && rule != null)
            {
                bool changed = false;
                bool axes = GUILayout.Toggle(rule.splitAxes, "Rx/Rzを分ける");
                bool loops = GUILayout.Toggle(rule.splitLoops, "L1/L2を分ける");
                if (axes != rule.splitAxes) { rule.splitAxes = axes; changed = true; }
                if (loops != rule.splitLoops)
                {
                    if (loops)
                    {
                        rule.l2Rx = rule.l1Rx;
                        rule.l2Rz = rule.l1Rz;
                    }
                    rule.splitLoops = loops;
                    changed = true;
                }
                changed |= DrawMotionMultiplierRow(preset, id, level, "L1", ref rule.l1Rx, ref rule.l1Rz, rule.splitAxes);
                if (rule.splitLoops)
                    changed |= DrawMotionMultiplierRow(preset, id, level, "L2", ref rule.l2Rx, ref rule.l2Rz, rule.splitAxes);
                if (changed) SetMotionRule(preset, rule);
            }
            else GUILayout.Label("共通: Rx " + commonRx.ToString("F2", CultureInfo.InvariantCulture)
                + "倍 / Rz " + commonRz.ToString("F2", CultureInfo.InvariantCulture) + "倍");
            DrawMotionPositionRule(preset, id, level);
            GUILayout.EndVertical();
        }

        private void DrawMotionPositionRule(PresetEntry preset, string id, int level)
        {
            MotionPositionRule rule = FindMotionPositionRule(preset, id, level);
            bool enabled = rule != null;
            bool nextEnabled = GUILayout.Toggle(enabled, "座標差分を設定 (ローカルm)");
            if (nextEnabled != enabled)
            {
                if (nextEnabled)
                {
                    rule = new MotionPositionRule { objectId = id, level = level };
                    SetMotionPositionRule(preset, rule);
                }
                else RemoveMotionPositionRule(preset, id, level);
            }
            if (!nextEnabled || rule == null) return;

            bool changed = false;
            bool splitLoops = GUILayout.Toggle(rule.splitLoops, "座標をL1/L2で分ける");
            if (splitLoops != rule.splitLoops)
            {
                if (splitLoops) rule.l2Offset = rule.l1Offset;
                rule.splitLoops = splitLoops;
                changed = true;
            }
            Vector3 l1 = rule.l1Offset;
            changed |= DrawMotionOffsetRow(preset, id, level, "L1", ref l1);
            rule.l1Offset = l1;
            if (rule.splitLoops)
            {
                Vector3 l2 = rule.l2Offset;
                changed |= DrawMotionOffsetRow(preset, id, level, "L2", ref l2);
                rule.l2Offset = l2;
            }
            if (changed) SetMotionPositionRule(preset, rule);
        }

        private bool DrawMotionOffsetRow(PresetEntry preset, string id, int level, string slot, ref Vector3 offset)
        {
            bool changed = false;
            GUILayout.Label(slot + " 座標差分（±3m）");
            changed |= DrawMotionOffsetField(preset, id, level, slot + "X", "X", ref offset.x);
            changed |= DrawMotionOffsetField(preset, id, level, slot + "Y", "Y", ref offset.y);
            changed |= DrawMotionOffsetField(preset, id, level, slot + "Z", "Z", ref offset.z);
            return changed;
        }

        private bool DrawMotionOffsetField(PresetEntry preset, string id, int level, string part, string label, ref float value)
        {
            string key = "MotionOffset_" + RuntimeHelpers.GetHashCode(preset) + "_" + id + "_" + level + "_" + part;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(32f));
            float display = Mathf.Clamp(value, -3f, 3f);
            float slider = GUILayout.HorizontalSlider(display, -3f, 3f, GUILayout.ExpandWidth(true));
            bool sliderChanged = Mathf.Abs(slider - display) > 0.00001f;
            if (sliderChanged)
            {
                value = slider;
                _motionFieldEdits.Remove(key);
            }
            GUI.SetNextControlName(key);
            bool focused = GUI.GetNameOfFocusedControl() == key;
            string text = focused && _motionFieldEdits.TryGetValue(key, out string edit)
                ? edit : value.ToString("F3", CultureInfo.InvariantCulture);
            string next = GUILayout.TextField(text, GUILayout.Width(70f));
            bool textChanged = false;
            if (GUI.GetNameOfFocusedControl() == key)
            {
                _motionFieldEdits[key] = next;
                if (next != text && float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                    && parsed >= -3f && parsed <= 3f && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                {
                    value = parsed;
                    textChanged = true;
                }
            }
            else _motionFieldEdits.Remove(key);
            GUILayout.EndHorizontal();
            return sliderChanged || textChanged;
        }

        private bool DrawMotionMultiplierRow(PresetEntry preset, string id, int level, string slot,
            ref float rx, ref float rz, bool splitAxes)
        {
            bool changed = false;
            changed |= DrawMotionMultiplierField(preset, id, level, slot + (splitAxes ? " Rx" : " 共通"), slot + "Rx", ref rx);
            if (splitAxes)
                changed |= DrawMotionMultiplierField(preset, id, level, slot + " Rz", slot + "Rz", ref rz);
            return changed;
        }

        private bool DrawMotionMultiplierField(PresetEntry preset, string id, int level, string label, string part, ref float value)
        {
            string key = "MotionMultiplier_" + RuntimeHelpers.GetHashCode(preset) + "_" + id + "_" + level + "_" + part;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(55f));
            float display = Mathf.Clamp(value, 0.05f, 10f);
            float slider = GUILayout.HorizontalSlider(display, 0.05f, 10f, GUILayout.ExpandWidth(true));
            bool sliderChanged = Mathf.Abs(slider - display) > 0.00001f;
            if (sliderChanged)
            {
                value = slider;
                _motionFieldEdits.Remove(key);
            }
            GUI.SetNextControlName(key);
            bool focused = GUI.GetNameOfFocusedControl() == key;
            string text = focused && _motionFieldEdits.TryGetValue(key, out string edit)
                ? edit : value.ToString("F2", CultureInfo.InvariantCulture);
            string next = GUILayout.TextField(text, GUILayout.Width(66f));
            bool textChanged = false;
            if (GUI.GetNameOfFocusedControl() == key)
            {
                _motionFieldEdits[key] = next;
                if (next != text && float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                    && parsed >= 0.05f && parsed <= 10f && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                {
                    value = parsed;
                    textChanged = true;
                }
            }
            else _motionFieldEdits.Remove(key);
            GUILayout.EndHorizontal();
            return sliderChanged || textChanged;
        }

    }
}
