using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameObjectComposer
{
    [Serializable]
    internal sealed class MotionPositionRule
    {
        public string objectId;
        public int level;
        public bool splitLoops;
        // Local offsets in metres. Preset L1/L2 positions remain the source of truth.
        public Vector3 l1Offset;
        public Vector3 l2Offset;
    }

    public sealed partial class Plugin
    {
        private readonly Dictionary<string, Vector3> _appliedMotionPositionOffsets =
            new Dictionary<string, Vector3>(StringComparer.Ordinal);

        private MotionPositionRule FindMotionPositionRule(PresetEntry preset, string id, int level)
        {
            if (preset == null || preset.motionPositionRulesJson == null) return null;
            foreach (string json in preset.motionPositionRulesJson)
            {
                if (string.IsNullOrEmpty(json)) continue;
                MotionPositionRule rule;
                try { rule = JsonUtility.FromJson<MotionPositionRule>(json); }
                catch { continue; }
                if (rule != null && rule.level == level && string.Equals(rule.objectId, id, StringComparison.Ordinal))
                    return rule;
            }
            return null;
        }

        private Vector3 GetEffectiveMotionPositionOffset(string id)
        {
            if (_activePreset == null || !IsMotionTarget(_activePreset, id)) return Vector3.zero;
            MotionPositionRule rule = FindMotionPositionRule(_activePreset, id, _motionIntensityLevel);
            if (rule == null) return Vector3.zero;
            bool blend = rule.splitLoops && _activePreset.useLoopSplit && _activeL2ById != null
                && _activeL2ById.ContainsKey(id);
            if (!blend) return rule.l1Offset;
            float t = _runtime != null && _runtime.Flags != null ? Mathf.Clamp01(_runtime.Flags.motion) : 0f;
            return Vector3.Lerp(rule.l1Offset, rule.l2Offset, t);
        }

        private Vector3 GetAppliedMotionPositionOffset(string id)
        {
            return !string.IsNullOrEmpty(id) && _appliedMotionPositionOffsets.TryGetValue(id, out Vector3 offset)
                ? offset : Vector3.zero;
        }

        private void SetAppliedMotionPositionOffset(string id, Vector3 offset)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (offset.sqrMagnitude < 1e-12f) _appliedMotionPositionOffsets.Remove(id);
            else _appliedMotionPositionOffsets[id] = offset;
        }

        private void ApplyMotionPositionOffset(RuntimeObjectRef rr)
        {
            Vector3 offset = GetEffectiveMotionPositionOffset(rr.Data.id);
            offset *= 1f - MovementBlendFor(rr.Data.id);
            Vector3 target = rr.Data.localPosition + offset;
            if (rr.GameObject.transform.localPosition != target)
                rr.GameObject.transform.localPosition = target;
            SetAppliedMotionPositionOffset(rr.Data.id, offset);
        }

        private void ClearAppliedMotionPositionOffsets()
        {
            foreach (KeyValuePair<string, Vector3> kv in _appliedMotionPositionOffsets)
            {
                RuntimeObjectRef rr = FindRuntimeById(kv.Key);
                if (rr != null && rr.GameObject != null)
                    rr.GameObject.transform.localPosition -= kv.Value;
            }
            _appliedMotionPositionOffsets.Clear();
        }

        private void SetMotionPositionRule(PresetEntry preset, MotionPositionRule rule)
        {
            if (preset.motionPositionRulesJson == null) preset.motionPositionRulesJson = new List<string>();
            RemoveMotionPositionRule(preset, rule.objectId, rule.level, false);
            preset.motionPositionRulesJson.Add(JsonUtility.ToJson(rule, false));
            SaveMotionPresetMutation();
        }

        private void RemoveMotionPositionRule(PresetEntry preset, string id, int level, bool save = true)
        {
            if (preset.motionPositionRulesJson == null) return;
            preset.motionPositionRulesJson.RemoveAll(json =>
            {
                try
                {
                    MotionPositionRule rule = JsonUtility.FromJson<MotionPositionRule>(json);
                    return rule != null && rule.level == level && string.Equals(rule.objectId, id, StringComparison.Ordinal);
                }
                catch { return false; }
            });
            if (save) SaveMotionPresetMutation();
        }
    }
}
