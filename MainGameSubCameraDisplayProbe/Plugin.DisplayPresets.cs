using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private void SaveCurrentDisplayPreset()
        {
            EnsureProbe();
            PersistTransformsToSettings();

            string name = string.IsNullOrWhiteSpace(_displayPresetNameBuffer)
                ? "Display " + ((_settings.DisplayPresets?.Length ?? 0) + 1)
                : _displayPresetNameBuffer.Trim();
            name = MakeUniqueDisplayPresetName(name, null);

            DisplayPreset preset = BuildDisplayPreset(name);
            List<DisplayPreset> presets = new List<DisplayPreset>(_settings.DisplayPresets ?? new DisplayPreset[0]);
            presets.Add(preset);
            _settings.DisplayPresets = presets.ToArray();
            _activeDisplayPresetName = preset.Name ?? string.Empty;
            SaveSettings();
            SetStatus("ディスプレイ保存: " + name);
        }

        private DisplayPreset BuildDisplayPreset(string name)
        {
            Vector3 position = new Vector3(_settings.DisplayPosX, _settings.DisplayPosY, _settings.DisplayPosZ);
            Vector3 rotation = new Vector3(_settings.DisplayRotX, _settings.DisplayRotY, _settings.DisplayRotZ);
            if (_displayAnchorObject != null)
            {
                position = _displayAnchorObject.transform.position;
                rotation = _displayAnchorObject.transform.rotation.eulerAngles;
            }

            return new DisplayPreset
            {
                Name = name,
                Position = ToArray(position),
                Rotation = ToArray(NormalizeEuler(rotation)),
                Width = SettingsStore.CalculateDisplayWidth(_settings),
                Height = _settings.DisplayHeight,
                UsePoseOverrides = _settings.SaveDisplayPoseOverrides,
                PoseOverrides = new DisplayPoseOverride[0]
            };
        }

        private void LoadDisplayPreset(DisplayPreset preset)
        {
            if (preset == null)
                return;

            EnsureProbe();
            DisplayPreset resolved = ResolveDisplayPresetForCurrentPose(preset);
            Vector3 pos = ToVector3(resolved.Position, _displayAnchorObject != null
                ? _displayAnchorObject.transform.position
                : new Vector3(_settings.DisplayPosX, _settings.DisplayPosY, _settings.DisplayPosZ));
            Vector3 rot = ToVector3(resolved.Rotation, _displayAnchorObject != null
                ? _displayAnchorObject.transform.rotation.eulerAngles
                : new Vector3(_settings.DisplayRotX, _settings.DisplayRotY, _settings.DisplayRotZ));
            _settings.DisplayHeight = Round2(Mathf.Max(0.1f, resolved.Height));
            _settings.DisplayWidth = SettingsStore.CalculateDisplayWidth(_settings);
            _activeDisplayPresetName = preset.Name ?? string.Empty;
            _displayPresetNameBuffer = _activeDisplayPresetName;
            if (_displayAnchorObject != null)
                _displayAnchorObject.transform.SetPositionAndRotation(pos, Quaternion.Euler(rot));
            CaptureTransformsToSettings();
            ApplyDisplaySettings();
            SetStatus("ディスプレイ呼び出し: " + _activeDisplayPresetName);
        }

        private void OverwriteActiveDisplayPreset()
        {
            string name = (_displayPresetNameBuffer ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                SetStatus("ディスプレイ上書き名なし");
                return;
            }

            DisplayPreset preset = FindDisplayPresetByName(name);
            if (preset == null)
            {
                SetStatus("ディスプレイ上書き対象なし");
                return;
            }

            if (preset.UsePoseOverrides && TryGetCurrentPoseInfo(out string poseKey, out string poseDisplayName))
            {
                PersistTransformsToSettings();
                CapturePoseOverrideToDisplayPreset(preset, poseKey, poseDisplayName);
                SaveSettings();
                SetStatus("ディスプレイ体位上書き: " + (preset.Name ?? string.Empty) + " / " + poseDisplayName);
                return;
            }

            PersistTransformsToSettings();
            DisplayPreset next = BuildDisplayPreset(preset.Name ?? _activeDisplayPresetName);
            preset.Position = next.Position;
            preset.Rotation = next.Rotation;
            preset.Width = next.Width;
            preset.Height = next.Height;
            SaveSettings();
            SetStatus("ディスプレイ上書き: " + (preset.Name ?? string.Empty));
        }

        private void RemoveDisplayPresetAt(int index)
        {
            DisplayPreset[] source = _settings.DisplayPresets ?? new DisplayPreset[0];
            if (index < 0 || index >= source.Length)
                return;

            string removed = source[index]?.Name ?? string.Empty;
            List<DisplayPreset> next = new List<DisplayPreset>(source);
            next.RemoveAt(index);
            _settings.DisplayPresets = next.ToArray();
            if (string.Equals(removed, _activeDisplayPresetName, StringComparison.Ordinal))
                _activeDisplayPresetName = string.Empty;
            SaveSettings();
            SetStatus("ディスプレイ削除: " + removed);
        }

        private DisplayPreset FindActiveDisplayPreset()
        {
            if (string.IsNullOrWhiteSpace(_activeDisplayPresetName))
                return null;

            DisplayPreset[] presets = _settings.DisplayPresets ?? new DisplayPreset[0];
            for (int i = 0; i < presets.Length; i++)
            {
                DisplayPreset preset = presets[i];
                if (preset != null && string.Equals(preset.Name ?? string.Empty, _activeDisplayPresetName, StringComparison.Ordinal))
                    return preset;
            }

            return null;
        }

        private DisplayPreset FindDisplayPresetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            DisplayPreset[] presets = _settings.DisplayPresets ?? new DisplayPreset[0];
            for (int i = 0; i < presets.Length; i++)
            {
                DisplayPreset preset = presets[i];
                if (preset != null && string.Equals(preset.Name ?? string.Empty, name, StringComparison.Ordinal))
                    return preset;
            }

            return null;
        }

        private DisplayPreset ResolveDisplayPresetForCurrentPose(DisplayPreset preset)
        {
            if (preset == null || !preset.UsePoseOverrides)
                return preset;

            if (!TryGetCurrentPoseInfo(out string key, out _))
                return preset;

            DisplayPoseOverride pose = FindDisplayPoseOverride(preset, key);
            if (pose == null)
                return preset;

            return new DisplayPreset
            {
                Name = preset.Name,
                Position = CloneArray(pose.Position, preset.Position),
                Rotation = CloneArray(pose.Rotation, preset.Rotation),
                Width = pose.Width > 0f ? pose.Width : preset.Width,
                Height = pose.Height > 0f ? pose.Height : preset.Height,
                UsePoseOverrides = preset.UsePoseOverrides,
                PoseOverrides = preset.PoseOverrides
            };
        }

        private void CapturePoseOverrideToDisplayPreset(DisplayPreset preset, string key, string displayName)
        {
            if (preset == null || string.IsNullOrWhiteSpace(key))
                return;

            DisplayPoseOverride pose = FindDisplayPoseOverride(preset, key);
            if (pose == null)
            {
                List<DisplayPoseOverride> list = new List<DisplayPoseOverride>(preset.PoseOverrides ?? new DisplayPoseOverride[0]);
                pose = new DisplayPoseOverride { Key = key };
                list.Add(pose);
                preset.PoseOverrides = list.ToArray();
            }

            DisplayPreset current = BuildDisplayPreset(preset.Name ?? string.Empty);
            pose.DisplayName = displayName ?? string.Empty;
            pose.Position = CloneArray(current.Position, null);
            pose.Rotation = CloneArray(current.Rotation, null);
            pose.Width = current.Width;
            pose.Height = current.Height;
        }

        private DisplayPoseOverride FindDisplayPoseOverride(DisplayPreset preset, string key)
        {
            if (preset == null || string.IsNullOrWhiteSpace(key))
                return null;

            DisplayPoseOverride[] overrides = preset.PoseOverrides ?? new DisplayPoseOverride[0];
            for (int i = 0; i < overrides.Length; i++)
            {
                DisplayPoseOverride entry = overrides[i];
                if (entry != null && string.Equals(entry.Key, key, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        private string MakeUniqueDisplayPresetName(string baseName, DisplayPreset self)
        {
            string seed = string.IsNullOrWhiteSpace(baseName) ? "Display" : baseName.Trim();
            string candidate = seed;
            int suffix = 2;
            while (HasDisplayPresetNameConflict(candidate, self))
            {
                candidate = seed + " " + suffix.ToString();
                suffix++;
            }

            return candidate;
        }

        private bool HasDisplayPresetNameConflict(string name, DisplayPreset self)
        {
            DisplayPreset[] presets = _settings.DisplayPresets ?? new DisplayPreset[0];
            for (int i = 0; i < presets.Length; i++)
            {
                DisplayPreset preset = presets[i];
                if (preset == null || ReferenceEquals(preset, self))
                    continue;
                if (string.Equals(preset.Name ?? string.Empty, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private bool IsActiveDisplayPreset(DisplayPreset preset)
        {
            return preset != null
                && !string.IsNullOrWhiteSpace(_activeDisplayPresetName)
                && string.Equals(preset.Name ?? string.Empty, _activeDisplayPresetName, StringComparison.Ordinal);
        }

        private static string GetDisplayPresetDisplayName(DisplayPreset preset, int index)
        {
            string name = string.IsNullOrWhiteSpace(preset?.Name) ? "Display " + (index + 1) : preset.Name;
            return (preset != null && preset.UsePoseOverrides ? "[表示][体位] " : "[表示] ") + name;
        }

        private int CountPoseAwareDisplayPresets()
        {
            DisplayPreset[] presets = _settings.DisplayPresets ?? new DisplayPreset[0];
            int count = 0;
            for (int i = 0; i < presets.Length; i++)
            {
                if (presets[i] != null && presets[i].UsePoseOverrides)
                    count++;
            }

            return count;
        }
    }
}
