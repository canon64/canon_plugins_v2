using System.Collections.Generic;
using UnityEngine;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        private readonly List<Light>  _nativeLights       = new List<Light>();
        private readonly List<float>  _nativeLightsBases  = new List<float>(); // 元のintensity
        private readonly List<Color>  _nativeLightsColors = new List<Color>(); // 元のcolor
        private float _nativeIntensityLoopPhase;
        private float _nativeRainbowHue;

        private void CacheNativeLights()
        {
            _nativeLights.Clear();
            _nativeLightsBases.Clear();
            _nativeLightsColors.Clear();

            Light[] all = FindObjectsOfType<Light>();
            foreach (var l in all)
            {
                // 自分が生成したライトは除外
                bool ours = false;
                foreach (var e in _lightEntries)
                    if (e.Light == l) { ours = true; break; }
                if (ours) continue;

                _nativeLights.Add(l);
                _nativeLightsBases.Add(l.intensity);
                _nativeLightsColors.Add(l.color);

                // 詳細ログ（服・髪への反映調査用）
                _log.Info($"[NativeLights] #{_nativeLights.Count} " +
                    $"name={l.gameObject.name} " +
                    $"path={GetGameObjectPath(l.gameObject)} " +
                    $"type={l.type} " +
                    $"intensity={l.intensity:F2} " +
                    $"range={l.range:F1} " +
                    $"spotAngle={l.spotAngle:F1} " +
                    $"color={l.color} " +
                    $"renderMode={l.renderMode} " +
                    $"cullingMask={l.cullingMask} " +
                    $"shadows={l.shadows} " +
                    $"shadowStrength={l.shadowStrength:F2} " +
                    $"enabled={l.enabled} " +
                    $"layer={l.gameObject.layer} " +
                    $"pos={l.transform.position}");
            }
            _log.Info($"[NativeLights] キャッシュ完了 count={_nativeLights.Count}");
        }

        private void ClearNativeLightsOverride()
        {
            // 元のintensity・colorに戻す
            for (int i = 0; i < _nativeLights.Count; i++)
            {
                if (_nativeLights[i] == null) continue;
                _nativeLights[i].intensity = _nativeLightsBases[i];
                if (i < _nativeLightsColors.Count)
                    _nativeLights[i].color = _nativeLightsColors[i];
            }
            _nativeLights.Clear();
            _nativeLightsBases.Clear();
            _nativeLightsColors.Clear();
        }

        internal void ApplyNativeLightOverride()
        {
            if (!_settings.NativeLight.OverrideEnabled) return;

            float scale = _settings.NativeLight.IntensityScale;
            for (int i = 0; i < _nativeLights.Count; i++)
            {
                if (_nativeLights[i] == null) continue;
                _nativeLights[i].intensity = _nativeLightsBases[i] * scale;
            }
        }

        internal void UpdateNativeLightLoop()
        {
            var nl = _settings.NativeLight;
            if (!nl.OverrideEnabled) return;
            if (_nativeLights.Count == 0) return;

            float dt  = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            bool needsBeat = nl.IntensityLoop.Enabled || nl.Rainbow.Enabled || nl.Strobe.Enabled;
            float beatLoopHz = 0f;
            float zoneSpeed01 = 0f;
            bool hasBeatLoopHz = needsBeat && TryGetBeatLinkedLoopHz(out beatLoopHz);
            bool hasZoneSpeed  = needsBeat && TryGetBeatZoneSpeed01(out zoneSpeed01);

            // 強度ループ
            float loopScale = 1f;
            if (nl.IntensityLoop.Enabled)
            {
                if (nl.IntensityLoop.BeatFollow)
                {
                    float s = hasZoneSpeed ? zoneSpeed01 : 0f;
                    loopScale = Mathf.Lerp(nl.IntensityLoop.MinValue, nl.IntensityLoop.MaxValue, s);
                }
                else
                {
                    if (nl.IntensityLoop.VideoLink)
                    {
                        if (hasBeatLoopHz && beatLoopHz > 0f)
                            _nativeIntensityLoopPhase += beatLoopHz * dt;
                    }
                    else if (nl.IntensityLoop.SpeedHz > 0f)
                    {
                        _nativeIntensityLoopPhase += nl.IntensityLoop.SpeedHz * dt;
                    }
                    float lt = (Mathf.Sin(_nativeIntensityLoopPhase * Mathf.PI * 2f) + 1f) * 0.5f;
                    loopScale = Mathf.Lerp(nl.IntensityLoop.MinValue, nl.IntensityLoop.MaxValue, lt);
                }
            }

            // レインボー
            Color rainbowColor = Color.white;
            bool applyRainbow = nl.Rainbow.Enabled;
            if (applyRainbow)
            {
                float cycleSpeed = ResolveRainbowCycleSpeed(nl.Rainbow, hasBeatLoopHz, beatLoopHz, hasZoneSpeed, zoneSpeed01);
                _nativeRainbowHue = (_nativeRainbowHue + cycleSpeed * dt) % 1f;
                rainbowColor = Color.HSVToRGB(_nativeRainbowHue, 1f, 1f);
            }

            // ストロボ周波数・ON比率（毎フレーム同じ値を使う）
            float strobeFreq = nl.Strobe.Enabled
                ? ResolveStrobeFrequency(nl.Strobe, hasBeatLoopHz, beatLoopHz, hasZoneSpeed, zoneSpeed01)
                : 0f;
            float strobeDuty = nl.Strobe.Enabled
                ? ResolveStrobeDutyRatio(nl.Strobe, hasBeatLoopHz, beatLoopHz, hasZoneSpeed, zoneSpeed01, now)
                : 0.5f;

            for (int i = 0; i < _nativeLights.Count; i++)
            {
                if (_nativeLights[i] == null) continue;

                float intensity = _nativeLightsBases[i] * nl.IntensityScale * loopScale;

                if (nl.Strobe.Enabled && strobeFreq > 0f)
                {
                    float phase = (now * strobeFreq) % 1f;
                    intensity = phase < strobeDuty ? intensity : 0f;
                }

                if (_nativeLights[i].intensity != intensity)
                    _nativeLights[i].intensity = intensity;

                if (applyRainbow)
                {
                    if (_nativeLights[i].color != rainbowColor)
                        _nativeLights[i].color = rainbowColor;
                }
                else if (i < _nativeLightsColors.Count)
                {
                    if (_nativeLights[i].color != _nativeLightsColors[i])
                        _nativeLights[i].color = _nativeLightsColors[i];
                }
            }
        }

        private static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            Transform t = go.transform.parent;
            int depth = 0;
            while (t != null && depth < 5)
            {
                path = t.name + "/" + path;
                t = t.parent;
                depth++;
            }
            return path;
        }

        private void ApplyBeatZoneToNativeLights(BeatZone zone)
        {
            if (!_settings.NativeLight.OverrideEnabled) return;

            var beat = _settings.NativeLight.Beat;
            string presetId = zone == BeatZone.Low  ? beat.LowPresetId
                            : zone == BeatZone.Mid  ? beat.MidPresetId
                                                    : beat.HighPresetId;
            if (!string.IsNullOrEmpty(presetId))
                ApplyPresetToNativeLight(presetId);
            else
                ApplyNativeLightOverride();
        }
    }
}
