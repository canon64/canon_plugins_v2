using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private PresetEntry _movementEditPreset, _movementReturnPreset;
        private Dictionary<string, ManagedObjectData> _movementEditInitial;
        private List<string> _movementReturnLayout;
        private string _movementReturnSelection;
        private int _movementEditSlot;
        private bool _movementEditFull;

        private void BeginMovementEdit(PresetEntry preset, int slot, bool full)
        {
            EnsureMovementPreset(preset);
            if (_movementEditPreset != null || preset.movementTargetIds.Count == 0) return;
            List<ManagedObjectData> data = DecodeObjects(slot == 2 ? preset.loop2Json : preset.loop1Json);
            if (data.Count == 0) { LogWarn("movement edit: 保存済みのL" + slot + "がありません"); return; }
            SyncAllDataFromRuntime();
            _movementReturnLayout = EncodeObjects(CloneObjectList(_objects));
            _movementReturnPreset = _activePreset;
            _movementReturnSelection = _selectedId;
            _movementEditPreset = preset;
            _movementEditSlot = slot;
            _movementEditFull = full;
            _activePreset = null;
            RebuildActivePresetCache();
            ClearAppliedMotionPositionOffsets();
            foreach (ManagedObjectData obj in data)
            {
                if (!obj.isRotationObject || !preset.movementTargetIds.Contains(obj.id)) continue;
                foreach (MovementField field in MovementField.All)
                {
                    var rule = FindMovementField(preset, obj.id, field.Key);
                    if (rule != null && !rule.enabled) continue;
                    if (!field.Radius && (rule == null || !rule.Has(slot, full))) continue;
                    field.Write(obj, MovementFieldRule.Endpoint(rule, slot, full, field.Read(obj), field.Radius,
                        preset.movementZeroMultiplier, preset.movementFullMultiplier));
                }
            }
            _movementEditInitial = BuildIdMap(CloneObjectList(data));
            ApplyLayoutData(new ObjectLayoutFile { objectsJson = EncodeObjects(data) }, rebuildRuntime: true);
            _selectedId = preset.movementTargetIds[0];
            _selectionDirty = true;
            RaiseManagedObjectListChanged();
            LogMovement("edit begin preset=" + preset.displayName + " slot=" + slot + " endpoint=" + (full ? 100 : 0));
        }

        private void EndMovementEdit(bool capture)
        {
            PresetEntry preset = _movementEditPreset;
            if (preset == null) return;
            int changed = 0;
            if (capture)
            {
                // Editors/gizmos already update Data. Do not capture transforms
                // driven by an orbit or animation as if the user had moved them.
                foreach (ManagedObjectData obj in _objects)
                {
                    if (!obj.isRotationObject || !preset.movementTargetIds.Contains(obj.id)
                        || !_movementEditInitial.TryGetValue(obj.id, out var initial)) continue;
                    foreach (MovementField field in MovementField.All)
                    {
                        float value = field.Read(obj);
                        if (!field.Different(field.Read(initial), value)) continue;
                        var rule = FindMovementField(preset, obj.id, field.Key)
                            ?? new MovementFieldRule { objectId = obj.id, field = field.Key };
                        rule.enabled = true;
                        rule.Set(_movementEditSlot, _movementEditFull, value);
                        SaveMovementField(preset, rule, false);
                        changed++;
                    }
                }
                SavePresets();
            }
            _movementEditPreset = null;
            ApplyLayoutData(new ObjectLayoutFile { objectsJson = _movementReturnLayout }, rebuildRuntime: true);
            _activePreset = _movementReturnPreset;
            _selectedId = _movementReturnSelection;
            _selectionDirty = true;
            RebuildActivePresetCache();
            RaiseManagedObjectListChanged();
            SaveLayoutIfNeeded();
            _movementReturnLayout = null;
            _movementEditInitial = null;
            _movementReturnPreset = null;
            LogMovement("edit " + (capture ? "captured" : "cancelled") + " changedFields=" + changed);
        }
    }
}
