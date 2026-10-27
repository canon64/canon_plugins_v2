using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameObjectComposer
{
    [Serializable]
    internal sealed class MotionIntensityRule
    {
        public string objectId;
        public int level;
        public bool splitAxes;
        public bool splitLoops;
        public float l1Rx = 1f;
        public float l1Rz = 1f;
        public float l2Rx = 1f;
        public float l2Rz = 1f;
    }

    public sealed partial class Plugin
    {
        private static readonly string[] MotionLevelLabels =
            { "とてもおとなしく", "おとなしく", "普通", "激しく", "すごく激しく" };
        private static readonly float[] DefaultMotionRx = { 0.60f, 0.80f, 1f, 1.50f, 1.80f };
        private static readonly float[] DefaultMotionRz = { 0.80f, 0.90f, 1f, 1.20f, 1.35f };
        private readonly ConfigEntry<float>[] _motionCommonRx = new ConfigEntry<float>[5];
        private readonly ConfigEntry<float>[] _motionCommonRz = new ConfigEntry<float>[5];
        private int _motionIntensityLevel = 2;
        private bool _motionPresetSavePending;

        private void SaveMotionPresetMutation()
        {
            if (Event.current != null &&
                (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag))
            {
                _motionPresetSavePending = true;
                return;
            }
            _motionPresetSavePending = false;
            SavePresets();
        }

        private void FlushMotionPresetSaveIfReleased()
        {
            if (!_motionPresetSavePending || Input.GetMouseButton(0)) return;
            _motionPresetSavePending = false;
            SavePresets();
        }

        private void InitializeMotionIntensityConfig()
        {
            for (int i = 0; i < MotionLevelLabels.Length; i++)
            {
                string key = (i + 1).ToString() + "_" + MotionLevelLabels[i];
                _motionCommonRx[i] = Config.Bind("動きの強さ・共通倍率", key + "_Rx", DefaultMotionRx[i],
                    new ConfigDescription("対象プリセットの共通Rx倍率。個別上書きが優先されます。", new AcceptableValueRange<float>(0.05f, 10f)));
                _motionCommonRz[i] = Config.Bind("動きの強さ・共通倍率", key + "_Rz", DefaultMotionRz[i],
                    new ConfigDescription("Rx/Rzを分けたときの共通Rz倍率。", new AcceptableValueRange<float>(0.05f, 10f)));
            }
        }

        /// <summary>VoiceFaceEventBridgeから段階だけを受け取る。0..4。</summary>
        public static bool TrySetMotionIntensityLevel(int level)
        {
            Plugin inst = _instance;
            if (inst == null || level < 0 || level >= MotionLevelLabels.Length) return false;
            inst._motionIntensityLevel = level;
            inst.LogInfo("motion intensity level=" + MotionLevelLabels[level]);
            return true;
        }

        private bool IsMotionTarget(PresetEntry preset, string id)
        {
            return preset != null && ((preset.motionIntensityTargetIds != null
                && preset.motionIntensityTargetIds.Contains(id))
                || (preset.movementEnabled && _movementLine.SpeechPriority
                    && preset.movementTargetIds != null && preset.movementTargetIds.Contains(id)));
        }

        private MotionIntensityRule FindMotionRule(PresetEntry preset, string id, int level)
        {
            if (preset == null || preset.motionIntensityRulesJson == null) return null;
            foreach (string json in preset.motionIntensityRulesJson)
            {
                if (string.IsNullOrEmpty(json)) continue;
                MotionIntensityRule rule;
                try { rule = JsonUtility.FromJson<MotionIntensityRule>(json); }
                catch { continue; }
                if (rule != null && rule.level == level && string.Equals(rule.objectId, id, StringComparison.Ordinal))
                    return rule;
            }
            return null;
        }

        private void GetMotionMultipliers(PresetEntry preset, string id, int slot, out float rx, out float rz)
        {
            int level = Mathf.Clamp(_motionIntensityLevel, 0, 4);
            float commonRx = _motionCommonRx[level] != null ? _motionCommonRx[level].Value : DefaultMotionRx[level];
            float commonRz = _motionCommonRz[level] != null ? _motionCommonRz[level].Value : DefaultMotionRz[level];
            rx = Mathf.Clamp(commonRx, 0.05f, 10f);
            rz = Mathf.Clamp(commonRz, 0.05f, 10f);
            MotionIntensityRule rule = FindMotionRule(preset, id, level);
            if (rule == null) return;
            bool second = slot == 2 && rule.splitLoops;
            rx = Mathf.Clamp(second ? rule.l2Rx : rule.l1Rx, 0.05f, 10f);
            if (rule.splitAxes)
                rz = Mathf.Clamp(second ? rule.l2Rz : rule.l1Rz, 0.05f, 10f);
            else
                rz = rx;
        }

        private void GetEffectiveOrbitRadii(ManagedObjectData data, out float rx, out float rz)
        {
            if (_movementEditPreset != null)
            {
                rx = Mathf.Max(0.001f, data.orbitRadiusX);
                rz = Mathf.Max(0.001f, data.orbitRadiusZ);
                return;
            }
            GetLegacyOrbitRadii(data, out rx, out rz);
            ApplyMovementRadii(data, ref rx, ref rz);
        }

        private void GetLegacyOrbitRadii(ManagedObjectData data, out float rx, out float rz)
        {
            rx = Mathf.Max(0.001f, data.orbitRadiusX);
            rz = Mathf.Max(0.001f, data.orbitRadiusZ);
            if (_activePreset == null || !IsMotionTarget(_activePreset, data.id)) return;
            if (_activeL1ById == null || !_activeL1ById.TryGetValue(data.id, out ManagedObjectData l1)) return;

            GetMotionMultipliers(_activePreset, data.id, 1, out float m1x, out float m1z);
            ManagedObjectData l2 = null;
            bool blend = _activePreset.useLoopSplit && _activeL2ById != null
                && _activeL2ById.TryGetValue(data.id, out l2);
            if (!blend)
            {
                rx = Mathf.Max(0.001f, l1.orbitRadiusX * m1x);
                rz = Mathf.Max(0.001f, l1.orbitRadiusZ * m1z);
                return;
            }
            GetMotionMultipliers(_activePreset, data.id, 2, out float m2x, out float m2z);
            float t = _runtime != null && _runtime.Flags != null ? Mathf.Clamp01(_runtime.Flags.motion) : 0f;
            rx = Mathf.Max(0.001f, Mathf.Lerp(l1.orbitRadiusX * m1x, l2.orbitRadiusX * m2x, t));
            rz = Mathf.Max(0.001f, Mathf.Lerp(l1.orbitRadiusZ * m1z, l2.orbitRadiusZ * m2z, t));
        }

        private void SetMotionRule(PresetEntry preset, MotionIntensityRule rule)
        {
            if (preset.motionIntensityRulesJson == null) preset.motionIntensityRulesJson = new List<string>();
            RemoveMotionRule(preset, rule.objectId, rule.level, false);
            preset.motionIntensityRulesJson.Add(JsonUtility.ToJson(rule, false));
            SaveMotionPresetMutation();
        }

        private void RemoveMotionRule(PresetEntry preset, string id, int level, bool save = true)
        {
            if (preset.motionIntensityRulesJson == null) return;
            preset.motionIntensityRulesJson.RemoveAll(json =>
            {
                try
                {
                    MotionIntensityRule rule = JsonUtility.FromJson<MotionIntensityRule>(json);
                    return rule != null && rule.level == level && string.Equals(rule.objectId, id, StringComparison.Ordinal);
                }
                catch { return false; }
            });
            if (save) SaveMotionPresetMutation();
        }
    }
}
