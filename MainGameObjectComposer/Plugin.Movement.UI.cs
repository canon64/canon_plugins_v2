using System;
using System.Globalization;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private PresetEntry _expandedMovementPreset;
        private string _expandedMovementObject;
        private readonly System.Collections.Generic.Dictionary<string, string> _movementNumberEdits =
            new System.Collections.Generic.Dictionary<string, string>();

        private void DrawMovementEditor(PresetEntry preset)
        {
            EnsureMovementPreset(preset);
            bool next = GUILayout.Toggle(preset.movementEnabled, "Grok moovmentタグで腰の動きを連動する");
            if (next != preset.movementEnabled)
            {
                preset.movementEnabled = next;
                SavePresets();
                LogMovement("enabled=" + next + " preset=" + preset.displayName);
            }
            if (GUILayout.Button((_expandedMovementPreset == preset ? "▼ " : "▶ ") + "0・100の姿勢を設定"))
                _expandedMovementPreset = _expandedMovementPreset == preset ? null : preset;
            if (_expandedMovementPreset != preset) return;

            GUILayout.Label("受信値: " + (_movementLine.Score >= 0 ? _movementLine.Score.ToString() : "未受信")
                + " / " + (_movementLine.SpeechPriority ? "発話指定を優先中（指定行＋次の行）" : "タグ連動")
                + (ReferenceEquals(_activePreset, preset) ? " / 適用中" : " / このプリセットは未適用"));
            GUILayout.Label("Rx/Rz未指定時: 0 = " + preset.movementZeroMultiplier.ToString("0.##")
                + "倍 / 100 = " + preset.movementFullMultiplier.ToString("0.##") + "倍。その他は変更した項目だけ反映。");
            bool defaultsChanged = DrawMovementSetting("最小倍率", ref preset.movementZeroMultiplier, 0.01f, 10f)
                | DrawMovementSetting("最大倍率", ref preset.movementFullMultiplier, 0.01f, 10f)
                | DrawMovementSetting("移行秒数", ref preset.movementTransitionSeconds, 0.01f, 5f);
            if (defaultsChanged) SaveMotionPresetMutation();

            if (_movementEditPreset != null)
            {
                GUILayout.Label("編集中: " + _movementEditPreset.displayName + " / L" + _movementEditSlot
                    + " / 強度" + (_movementEditFull ? "100" : "0"));
                GUILayout.Label("通常の移動・回転などで姿勢を調整し、下の保存ボタンで確定します。");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("変更した姿勢を保存して戻る")) EndMovementEdit(true);
                if (GUILayout.Button("キャンセルして戻る")) EndMovementEdit(false);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("姿勢を見ながら設定：編集ボタン → 通常の移動・回転操作 → 保存して戻る。");
                if (preset.movementTargetIds.Count == 0)
                    GUILayout.Label("先に下の対象オブジェクトにチェックを入れてください。");
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && preset.movementTargetIds.Count > 0;
                DrawMovementPoseButtons(preset, 1);
                if (preset.useLoopSplit) DrawMovementPoseButtons(preset, 2);
                GUI.enabled = previousEnabled;
            }
            foreach (ManagedObjectData obj in DecodeObjects(preset.loop1Json))
            {
                if (!obj.isRotationObject || string.IsNullOrEmpty(obj.id)) continue;
                GUILayout.BeginHorizontal();
                bool selected = preset.movementTargetIds.Contains(obj.id);
                bool chosen = GUILayout.Toggle(selected, obj.name ?? obj.id);
                if (chosen != selected && _movementEditPreset == null)
                {
                    if (chosen) preset.movementTargetIds.Add(obj.id); else preset.movementTargetIds.Remove(obj.id);
                    SavePresets();
                }
                if (chosen && GUILayout.Button("項目ごとの設定", GUILayout.Width(140f)))
                    _expandedMovementObject = _expandedMovementObject == obj.id ? null : obj.id;
                GUILayout.EndHorizontal();
                if (!chosen || _expandedMovementObject != obj.id) continue;
                foreach (MovementField field in MovementField.All) DrawMovementField(preset, obj.id, field);
            }
        }

        private void DrawMovementPoseButtons(PresetEntry preset, int slot)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("L" + slot + "：強度0の姿勢を編集")) BeginMovementEdit(preset, slot, false);
            if (GUILayout.Button("L" + slot + "：強度100の姿勢を編集")) BeginMovementEdit(preset, slot, true);
            GUILayout.EndHorizontal();
        }

        private bool DrawMovementSetting(string label, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(80f));
            float changed = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString("0.##"), GUILayout.Width(45f));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(changed - value) < 0.00001f) return false;
            value = (float)Math.Round(changed, 2);
            return true;
        }

        private void DrawMovementField(PresetEntry preset, string id, MovementField field)
        {
            var rule = FindMovementField(preset, id, field.Key);
            bool enabled = rule != null ? rule.enabled : field.Radius;
            bool next = GUILayout.Toggle(enabled, field.Label + "を連動");
            if (next != enabled)
            {
                if (rule == null) rule = new MovementFieldRule { objectId = id, field = field.Key };
                rule.enabled = next;
                SaveMovementField(preset, rule);
            }
            if (!next) return;
            if (rule == null) rule = new MovementFieldRule { objectId = id, field = field.Key };
            for (int slot = 1; slot <= (preset.useLoopSplit ? 2 : 1); slot++)
            {
                DrawMovementEndpoint(preset, rule, field, slot, false);
                DrawMovementEndpoint(preset, rule, field, slot, true);
            }
        }

        private void DrawMovementEndpoint(PresetEntry preset, MovementFieldRule rule, MovementField field, int slot, bool full)
        {
            GUILayout.BeginHorizontal();
            string key = "movement_" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(preset)
                + "_" + rule.objectId + "_" + field.Key + "_" + slot + "_" + full;
            bool has = rule.Has(slot, full);
            bool next = GUILayout.Toggle(has, new GUIContent("L" + slot + " / 強度" + (full ? "100" : "0"),
                "この強度の値を指定。チェックを外すと既定値に戻ります。スライダー操作でも指定できます。"), GUILayout.Width(115f));
            var original = DecodeObjects(slot == 2 ? preset.loop2Json : preset.loop1Json).Find(x => x.id == rule.objectId);
            float fallback = original == null ? 0f : field.Read(original);
            if (field.Radius) fallback *= full ? preset.movementFullMultiplier : preset.movementZeroMultiplier;
            if (next != has)
            {
                if (next) rule.Set(slot, full, fallback); else rule.Clear(slot, full);
                SaveMovementField(preset, rule);
                _movementNumberEdits.Remove(key);
            }
            float current = rule.Has(slot, full) ? rule.Get(slot, full) : fallback;
            GetMovementSliderRange(field, out float min, out float max);
            // Clamp only the thumb. Opening this panel must not overwrite typed values outside its range.
            float display = Mathf.Clamp(current, min, max);
            float slider = GUILayout.HorizontalSlider(display, min, max, GUILayout.MinWidth(100f), GUILayout.ExpandWidth(true));
            if (!Mathf.Approximately(slider, display))
            {
                current = (float)Math.Round(slider, field.Key == "tube" ? 4 : 3);
                rule.Set(slot, full, current);
                SaveMovementField(preset, rule);
                _movementNumberEdits.Remove(key);
            }
            GUI.SetNextControlName(key);
            string value = GUI.GetNameOfFocusedControl() == key && _movementNumberEdits.TryGetValue(key, out var edit)
                ? edit : current.ToString("0.####", CultureInfo.InvariantCulture);
            string text = GUILayout.TextField(value, GUILayout.Width(85f));
            if (GUI.GetNameOfFocusedControl() == key)
            {
                _movementNumberEdits[key] = text;
                if (text != value && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
                    && !float.IsNaN(number) && !float.IsInfinity(number) && Mathf.Abs(number) < 10000f
                    && (!field.Radius || number > 0f))
                { rule.Set(slot, full, number); SaveMovementField(preset, rule); }
            }
            else _movementNumberEdits.Remove(key);
            GUILayout.Label(rule.Has(slot, full) ? "指定" : "既定", GUILayout.Width(32f));
            GUILayout.EndHorizontal();
        }

        private static void GetMovementSliderRange(MovementField field, out float min, out float max)
        {
            // Match the normal object editor's working ranges; numeric input retains its wider range.
            if (field.Angle) { min = -180f; max = 180f; }
            else if (field.Radius) { min = 0.001f; max = 2f; }
            else if (field.Key == "tube") { min = 0.001f; max = 0.1f; }
            else if (field.Key == "speed") { min = -5f; max = 5f; }
            else if (field.Key == "animSpeed") { min = 0.1f; max = 10f; }
            else if (field.Key == "phase" || field.Key == "animPhase") { min = -1f; max = 1f; }
            else if (field.Key == "sx" || field.Key == "sy" || field.Key == "sz") { min = 0.001f; max = 1f; }
            else { min = -0.5f; max = 0.5f; }
        }
    }
}
