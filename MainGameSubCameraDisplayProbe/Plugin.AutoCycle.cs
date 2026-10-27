using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private float _autoCycleNextAt;
        private float _autoCycleAwayUntil;
        private string _autoCycleLastPresetName = string.Empty;
        private System.Random _autoCycleRandom;

        private void UpdateAutoCycle()
        {
            if (_settings == null || !_settings.AutoCycleEnabled)
            {
                if (_autoCycleAwayUntil > 0f)
                    _autoCycleAwayUntil = 0f;
                return;
            }

            if (_rootObject == null || _cameraAnchorObject == null || _subCamera == null)
                return;

            float now = Time.unscaledTime;

            // 横槍中: 一時停止解除待ち
            if (_autoCycleAwayUntil > 0f)
            {
                if (now < _autoCycleAwayUntil)
                    return;

                _autoCycleAwayUntil = 0f;
                if (!string.IsNullOrEmpty(_autoCycleLastPresetName))
                {
                    SubCameraPreset back = FindPresetByName(_autoCycleLastPresetName);
                    if (back != null)
                    {
                        LoadPreset(back, "auto-cycle");
                        return;
                    }
                }

                // 戻り先が消えていた場合は通常タイマーへフォールバック
                _autoCycleNextAt = now + Mathf.Max(0.1f, _settings.AutoCycleIntervalSeconds);
                return;
            }

            // 通常タイマー
            if (now < _autoCycleNextAt)
                return;

            // 遷移中はタイマーをリセットして次フレーム以降に再判定
            if ((_actorLease == 0 && _transitionActive) || (_actorLease != 0 && _externalPreset != null && Time.unscaledTime < _externalPresetStarted + _externalPresetDuration))
            {
                _autoCycleNextAt = now + 0.05f;
                return;
            }

            SubCameraPreset next = SelectNextAutoCyclePreset();
            if (next == null)
            {
                _autoCycleNextAt = now + Mathf.Max(0.1f, _settings.AutoCycleIntervalSeconds);
                return;
            }

            LoadPreset(next, "auto-cycle");
        }

        private SubCameraPreset SelectNextAutoCyclePreset()
        {
            List<SubCameraPreset> targets = CollectAutoCycleTargets();
            if (targets.Count == 0)
                return null;

            if (targets.Count == 1)
                return targets[0];

            int currentIndex = -1;
            if (!string.IsNullOrEmpty(_autoCycleLastPresetName))
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    SubCameraPreset preset = targets[i];
                    if (preset != null && string.Equals(preset.Name ?? string.Empty, _autoCycleLastPresetName, StringComparison.Ordinal))
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }

            if (_settings.AutoCycleRandomOrder)
            {
                if (_autoCycleRandom == null)
                    _autoCycleRandom = new System.Random();

                int pick = currentIndex;
                int safety = 0;
                while (pick == currentIndex && safety < 8)
                {
                    pick = _autoCycleRandom.Next(targets.Count);
                    safety++;
                }
                return targets[Mathf.Clamp(pick, 0, targets.Count - 1)];
            }

            int next = currentIndex < 0 ? 0 : (currentIndex + 1) % targets.Count;
            return targets[next];
        }

        private List<SubCameraPreset> CollectAutoCycleTargets()
        {
            var list = new List<SubCameraPreset>();
            SubCameraPreset[] presets = _settings != null ? _settings.Presets : null;
            if (presets == null) return list;

            for (int i = 0; i < presets.Length; i++)
            {
                SubCameraPreset preset = presets[i];
                if (preset == null || !preset.AutoCycleInclude) continue;
                if (string.IsNullOrWhiteSpace(preset.Name)) continue;
                list.Add(preset);
            }
            return list;
        }

        private SubCameraPreset FindPresetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            SubCameraPreset[] presets = _settings != null ? _settings.Presets : null;
            if (presets == null) return null;

            for (int i = 0; i < presets.Length; i++)
            {
                SubCameraPreset preset = presets[i];
                if (preset == null) continue;
                if (string.Equals(preset.Name ?? string.Empty, name, StringComparison.Ordinal))
                    return preset;
            }
            return null;
        }

        private void NotifyAutoCycleLoadPreset(SubCameraPreset preset, string source)
        {
            if (_settings == null || preset == null) return;

            if (string.Equals(source, "auto-cycle", StringComparison.Ordinal))
            {
                _autoCycleLastPresetName = preset.Name ?? string.Empty;
                _autoCycleNextAt = Time.unscaledTime + Mathf.Max(0.1f, _settings.AutoCycleIntervalSeconds);
                return;
            }

            // 横槍 (manual / bridge / その他)
            if (!_settings.AutoCycleEnabled) return;

            float pause = Mathf.Clamp(_settings.AutoCyclePauseAfterExternalSeconds, 0f, 120f);
            if (pause <= 0.0001f)
            {
                _autoCycleAwayUntil = 0f;
                _autoCycleNextAt = Time.unscaledTime + Mathf.Max(0.1f, _settings.AutoCycleIntervalSeconds);
                return;
            }

            _autoCycleAwayUntil = Time.unscaledTime + pause;
        }
    }
}
