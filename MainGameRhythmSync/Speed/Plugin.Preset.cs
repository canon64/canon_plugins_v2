using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        private void SavePresetFromInput()
        {
            var s = Settings;
            if (s == null)
                return;
            EnsureAppliedBpmRangeInitialized(s);

            string animationName;
            if (!TryGetCurrentAnimationName(out animationName))
            {
                animationName = (_presetNameInput ?? string.Empty).Trim();
            }
            if (string.IsNullOrWhiteSpace(animationName))
            {
                animationName = BuildNextDefaultPresetName();
            }

            string userLabel = (_presetNameInput ?? string.Empty).Trim();
            string labelBase = BuildPresetLabelBase(userLabel, animationName);
            string folder = BuildAnimationFolderName(animationName);
            BpmReferenceMode modeForPreset = ClassifyBpmReferenceModeForPreset(animationName);
            GetStoredCalibrationPair(s, modeForPreset, out float baseMin, out float baseMax);
            float appliedMin = Mathf.Max(0f, s.AppliedBpmMin);
            float appliedMax = Mathf.Max(appliedMin + 0.0001f, s.AppliedBpmMax);
            string saveName = BuildUniquePresetName(s, BuildPresetDisplayName(labelBase, appliedMax, appliedMin));

            s.BpmPresets.Add(new BpmPreset
            {
                Folder = folder,
                AnimationName = animationName,
                AppliedBpmMin = appliedMin,
                AppliedBpmMax = appliedMax,
                BaseBpmMin = baseMin,
                BaseBpmMax = baseMax,
                Name = saveName,
                Bpm = appliedMax,
                AppliedBpm = appliedMax,
                ReferenceBpmMin = baseMin,
                ReferenceBpmMax = baseMax
            });
            LogInfo(
                $"preset added: name={saveName} folder={folder} animation={animationName} " +
                $"applied=[{appliedMin:0.##}/{appliedMax:0.##}] " +
                $"base=[{baseMin:0.##}/{baseMax:0.##}] mode={GetBpmReferenceModeLabel(modeForPreset)}");

            SaveSettings("save preset");
            _presetNameInput = saveName;
            ShowUiNotice($"プリセット保存: {saveName}");
        }

        private void ApplyPreset(int index)
        {
            ApplyPreset(index, saveSettings: true, reason: "apply preset");
        }

        private void ApplyPreset(int index, bool saveSettings, string reason)
        {
            var s = Settings;
            if (s == null || index < 0 || index >= s.BpmPresets.Count)
                return;

            var p = s.BpmPresets[index];
            if (p == null)
                return;

            float baseMin = Mathf.Max(0f, p.BaseBpmMin);
            float baseMax = Mathf.Max(1f, p.BaseBpmMax);
            if (baseMin <= 0f && p.ReferenceBpmMin > 0f)
            {
                baseMin = p.ReferenceBpmMin;
            }
            if (baseMax <= 1f && p.ReferenceBpmMax > 1f)
            {
                baseMax = p.ReferenceBpmMax;
            }
            if (baseMax <= baseMin)
                baseMax = baseMin + 1f;

            BpmReferenceMode modeForPreset = ClassifyBpmReferenceModeForPreset(p.AnimationName);
            _activeBpmReferenceMode = modeForPreset;
            SaveCalibrationForModeAndApplyWorking(
                s,
                modeForPreset,
                baseMin,
                baseMax,
                pushConfigEntries: true);

            float appliedMax = Mathf.Max(0.0001f, p.AppliedBpmMax > 0f ? p.AppliedBpmMax : p.AppliedBpm);
            float appliedMin = Mathf.Max(0f, p.AppliedBpmMin);
            if (appliedMin >= appliedMax)
            {
                appliedMin = Mathf.Max(0f, appliedMax * 0.25f);
            }

            DisableForceVanillaForCustomApply("apply preset");
            s.AppliedBpmMin = appliedMin;
            s.AppliedBpmMax = appliedMax;
            ApplyAppliedRangeToTargetSpeeds(s, appliedMin, appliedMax);

            if (saveSettings)
            {
                SaveSettings(reason);
            }
            SyncUiFromSettings();
            _appliedBpmMinInput = appliedMin.ToString("0.##", CultureInfo.InvariantCulture);
            _appliedBpmMaxInput = appliedMax.ToString("0.##", CultureInfo.InvariantCulture);
            _presetNameInput = p.Name;
            UpdateLastAppliedPresetOrderIndex(index);

            LogInfo(
                $"preset apply: folder={GetPresetFolder(p)} animation={p.AnimationName} " +
                $"applied=[{appliedMin:0.##}/{appliedMax:0.##}] " +
                $"base=[{baseMin:0.##}/{baseMax:0.##}] mode={GetBpmReferenceModeLabel(modeForPreset)} " +
                $"target=[{s.TargetMinSpeed:0.###}/{s.TargetMaxSpeed:0.###}]");
            ShowUiNotice($"プリセット適用: {p.Name}");
        }

        private void ApplyPresetBaseOnly(int index)
        {
            var s = Settings;
            if (s == null || index < 0 || index >= s.BpmPresets.Count)
                return;

            var p = s.BpmPresets[index];
            if (p == null)
                return;

            float baseMin = Mathf.Max(0f, p.BaseBpmMin);
            float baseMax = Mathf.Max(1f, p.BaseBpmMax);
            if (baseMin <= 0f && p.ReferenceBpmMin > 0f)
            {
                baseMin = p.ReferenceBpmMin;
            }
            if (baseMax <= 1f && p.ReferenceBpmMax > 1f)
            {
                baseMax = p.ReferenceBpmMax;
            }
            if (baseMax <= baseMin)
                baseMax = baseMin + 1f;

            BpmReferenceMode modeForPreset = ClassifyBpmReferenceModeForPreset(p.AnimationName);
            _activeBpmReferenceMode = modeForPreset;
            SaveCalibrationForModeAndApplyWorking(
                s,
                modeForPreset,
                baseMin,
                baseMax,
                pushConfigEntries: true);

            float sourceMin = s.SourceMinSpeed;
            float sourceMax = Mathf.Max(sourceMin + 0.0001f, s.SourceMaxSpeed);
            s.TargetMinSpeed = sourceMin;
            s.TargetMaxSpeed = sourceMax;

            float appliedMin = Mathf.Max(0f, EstimateBpmFromMappedSpeed(sourceMin));
            float appliedMax = Mathf.Max(appliedMin + 0.0001f, EstimateBpmFromMappedSpeed(sourceMax));
            s.AppliedBpmMin = appliedMin;
            s.AppliedBpmMax = appliedMax;

            SetForceVanillaSpeed(true, "apply preset base only", saveSettings: false, notice: null);
            SaveSettings("apply preset base only");
            SyncUiFromSettings();
            _appliedBpmMinInput = appliedMin.ToString("0.##", CultureInfo.InvariantCulture);
            _appliedBpmMaxInput = appliedMax.ToString("0.##", CultureInfo.InvariantCulture);
            _presetNameInput = p.Name;
            UpdateLastAppliedPresetOrderIndex(index);

            LogInfo(
                $"preset base apply: folder={GetPresetFolder(p)} animation={p.AnimationName} " +
                $"base=[{baseMin:0.##}/{baseMax:0.##}] mode={GetBpmReferenceModeLabel(modeForPreset)} " +
                $"target=[{s.TargetMinSpeed:0.###}/{s.TargetMaxSpeed:0.###}] " +
                $"forceVanilla={s.ForceVanillaSpeed}");
            ShowUiNotice($"Base loaded (vanilla): {p.Name}");
        }
        private void DeletePreset(int index)
        {
            var s = Settings;
            if (s == null || index < 0 || index >= s.BpmPresets.Count)
                return;

            var p = s.BpmPresets[index];
            string name = p?.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = p?.AnimationName ?? "unknown";
            s.BpmPresets.RemoveAt(index);
            _lastAppliedPresetOrderIndex = -1;
            SaveSettings("delete preset");
            ShowUiNotice($"プリセット削除: {name}");
            LogInfo("preset deleted: " + name);
        }

        private List<int> BuildSortedPresetIndices()
        {
            var s = Settings;
            var result = new List<int>();
            if (s == null || s.BpmPresets == null || s.BpmPresets.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < s.BpmPresets.Count; i++)
            {
                if (s.BpmPresets[i] == null)
                {
                    continue;
                }

                result.Add(i);
            }

            result.Sort((a, b) =>
            {
                var pa = s.BpmPresets[a];
                var pb = s.BpmPresets[b];
                string fa = GetPresetFolder(pa);
                string fb = GetPresetFolder(pb);
                int fc = string.Compare(fa, fb, StringComparison.OrdinalIgnoreCase);
                if (fc != 0) return fc;

                int ac = string.Compare(pa.AnimationName, pb.AnimationName, StringComparison.OrdinalIgnoreCase);
                if (ac != 0) return ac;

                int maxCmp = pa.AppliedBpmMax.CompareTo(pb.AppliedBpmMax);
                if (maxCmp != 0) return maxCmp;
                return pa.AppliedBpmMin.CompareTo(pb.AppliedBpmMin);
            });

            return result;
        }

        private void UpdateLastAppliedPresetOrderIndex(int presetIndex)
        {
            var sorted = BuildSortedPresetIndices();
            _lastAppliedPresetOrderIndex = sorted.IndexOf(presetIndex);
        }

        private static string GetPresetFolder(BpmPreset preset)
        {
            if (preset == null)
                return "unknown";

            if (!string.IsNullOrWhiteSpace(preset.Folder))
                return preset.Folder;

            return BuildAnimationFolderName(preset.AnimationName);
        }

        private static string BuildPresetDisplayName(string animationName, float maxBpm, float minBpm)
        {
            string anim = string.IsNullOrWhiteSpace(animationName) ? "unknown" : animationName.Trim();
            string max = FormatBpmToken(maxBpm);
            string min = FormatBpmToken(minBpm);
            return $"{anim} {max}-{min}";
        }

        private static string BuildUniquePresetName(PluginSettings settings, string desiredName)
        {
            string baseName = string.IsNullOrWhiteSpace(desiredName) ? "BPM" : desiredName.Trim();
            if (settings == null || settings.BpmPresets == null || settings.BpmPresets.Count == 0)
                return baseName;

            bool Exists(string name) =>
                settings.BpmPresets.Exists(p =>
                    p != null &&
                    !string.IsNullOrWhiteSpace(p.Name) &&
                    string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

            if (!Exists(baseName))
                return baseName;

            for (int i = 2; i <= 999; i++)
            {
                string candidate = $"{baseName} ({i})";
                if (!Exists(candidate))
                    return candidate;
            }

            return $"{baseName} ({DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture)})";
        }

        private static string BuildPresetLabelBase(string userLabel, string animationName)
        {
            string fallback = string.IsNullOrWhiteSpace(animationName) ? "unknown" : animationName.Trim();
            if (string.IsNullOrWhiteSpace(userLabel))
                return fallback;

            string trimmed = userLabel.Trim();
            int lastSpace = trimmed.LastIndexOf(' ');
            if (lastSpace <= 0)
                return trimmed;

            string suffix = trimmed.Substring(lastSpace + 1);
            int dash = suffix.IndexOf('-');
            if (dash <= 0 || dash >= suffix.Length - 1)
                return trimmed;

            string left = suffix.Substring(0, dash);
            string right = suffix.Substring(dash + 1);
            if (TryParseBpmToken(left) && TryParseBpmToken(right))
            {
                return trimmed.Substring(0, lastSpace).Trim();
            }

            return trimmed;
        }

        private static bool TryParseBpmToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Trim().Replace(',', '.');
            return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        private static string FormatBpmToken(float bpm)
        {
            float v = Mathf.Round(bpm * 10f) / 10f;
            if (Mathf.Abs(v - Mathf.Round(v)) < 0.01f)
                return Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);

            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private string BuildNextDefaultPresetName()
        {
            var s = Settings;
            if (s == null)
                return "BPM 01";

            if (TryGetCurrentAnimationName(out var animationName))
            {
                EnsureAppliedBpmRangeInitialized(s);
                return BuildPresetDisplayName(animationName, s.AppliedBpmMax, s.AppliedBpmMin);
            }

            for (int i = 1; i <= 999; i++)
            {
                string name = $"BPM {i:00}";
                bool exists = s.BpmPresets.Exists(p => p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    return name;
                }
            }

            return "BPM";
        }

        private static bool TryParsePositiveFloat(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Trim().Replace(',', '.');
            bool ok = float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return ok && value > 0f;
        }

        private static bool TryParseNonNegativeFloat(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Trim().Replace(',', '.');
            bool ok = float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return ok && value >= 0f;
        }

        private void SaveSettings(string reason)
        {
            try
            {
                SettingsStore.Save(SettingsPath, Settings);
                LogInfo("settings saved (" + reason + ")");
            }
            catch (Exception ex)
            {
                LogError("settings save failed: " + ex.Message);
            }
        }
    }
}
