using BepInEx.Configuration;
using System.Reflection;
using UnityEngine;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        private enum BeatZone { Unknown, Low, Mid, High }

        private BeatZone     _lastBeatZone  = BeatZone.Unknown;
        private System.Type  _beatPluginType;
        private FieldInfo    _beatIntensityField;
        private PropertyInfo _beatInstanceProperty;
        private FieldInfo    _beatZoneRawTargetField;
        private FieldInfo    _beatCfgBpmField;
        private FieldInfo    _beatCfgLowIntensityField;
        private FieldInfo    _beatCfgMidIntensityField;
        private FieldInfo    _beatCfgHighIntensityField;
        private bool         _beatLookupDone;
        private bool         _beatZoneSourceLogged;
        private int          _beatZoneCacheFrame = -1;
        private bool         _beatZoneCacheValid;
        private BeatZone     _beatZoneCacheZone;
        private float        _beatZoneCacheLow;
        private float        _beatZoneCacheMid;
        private float        _beatZoneCacheHigh;

        // ── BeatSync 強度取得（リフレクション） ──────────────────────────────

        private float GetBeatIntensity()
        {
            EnsureBeatLookup();

            if (_beatIntensityField == null) return -1f;
            try { return (float)_beatIntensityField.GetValue(null); }
            catch { return -1f; }
        }

        private void EnsureBeatLookup()
        {
            if (_beatLookupDone) return;
            _beatLookupDone = true;

            _beatPluginType = System.Type.GetType("MainGameBeatSyncSpeed.Plugin, MainGameBeatSyncSpeed");
            if (_beatPluginType == null)
            {
                _log.Warn("[BeatSync] Plugin型の取得失敗");
                return;
            }

            _beatIntensityField = _beatPluginType.GetField("CurrentIntensity01",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            _beatInstanceProperty = _beatPluginType.GetProperty("Instance",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            _beatZoneRawTargetField = _beatPluginType.GetField("_currentZoneRawTarget",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _beatCfgBpmField = _beatPluginType.GetField("_cfgBpm",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _beatCfgLowIntensityField = _beatPluginType.GetField("_cfgLowIntensity",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _beatCfgMidIntensityField = _beatPluginType.GetField("_cfgMidIntensity",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _beatCfgHighIntensityField = _beatPluginType.GetField("_cfgHighIntensity",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (_beatIntensityField != null)
                _log.Info("[BeatSync] CurrentIntensity01 フィールド取得成功");
            else
                _log.Warn("[BeatSync] CurrentIntensity01 フィールド取得失敗");
        }

        private object GetBeatInstance()
        {
            EnsureBeatLookup();
            if (_beatInstanceProperty == null) return null;
            try { return _beatInstanceProperty.GetValue(null, null); }
            catch { return null; }
        }

        private static bool TryReadConfigEntryFloat(object owner, FieldInfo field, out float value)
        {
            value = 0f;
            if (owner == null || field == null) return false;
            try
            {
                object entry = field.GetValue(owner);
                if (entry == null) return false;
                if (entry is ConfigEntry<float> floatEntry)
                {
                    value = floatEntry.Value;
                    return true;
                }
                var valueProp = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                if (valueProp == null) return false;
                object raw = valueProp.GetValue(entry, null);
                if (raw is float f) { value = f; return true; }
                if (raw is double d) { value = (float)d; return true; }
                return false;
            }
            catch { return false; }
        }

        private static bool TryReadConfigEntryInt(object owner, FieldInfo field, out int value)
        {
            value = 0;
            if (owner == null || field == null) return false;
            try
            {
                object entry = field.GetValue(owner);
                if (entry == null) return false;
                if (entry is ConfigEntry<int> intEntry)
                {
                    value = intEntry.Value;
                    return true;
                }
                var valueProp = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                if (valueProp == null) return false;
                object raw = valueProp.GetValue(entry, null);
                if (raw is int i) { value = i; return true; }
                if (raw is float f) { value = Mathf.RoundToInt(f); return true; }
                if (raw is double d) { value = Mathf.RoundToInt((float)d); return true; }
                return false;
            }
            catch { return false; }
        }

        private bool TryGetBeatZoneFromBeatSync(out BeatZone zone, out float lowIntensity, out float midIntensity, out float highIntensity)
        {
            int frame = Time.frameCount;
            if (_beatZoneCacheFrame != frame)
            {
                _beatZoneCacheValid = ReadBeatZoneFromBeatSync(
                    out _beatZoneCacheZone, out _beatZoneCacheLow,
                    out _beatZoneCacheMid, out _beatZoneCacheHigh);
                _beatZoneCacheFrame = frame;
            }
            zone = _beatZoneCacheZone;
            lowIntensity = _beatZoneCacheLow;
            midIntensity = _beatZoneCacheMid;
            highIntensity = _beatZoneCacheHigh;
            return _beatZoneCacheValid;
        }

        private bool ReadBeatZoneFromBeatSync(out BeatZone zone, out float lowIntensity, out float midIntensity, out float highIntensity)
        {
            zone = BeatZone.Unknown;
            lowIntensity = 0f;
            midIntensity = 0.5f;
            highIntensity = 1f;

            object inst = GetBeatInstance();
            if (inst == null || _beatZoneRawTargetField == null) return false;

            float rawTarget;
            try
            {
                object raw = _beatZoneRawTargetField.GetValue(inst);
                if (!(raw is float f) || f < 0f) return false;
                rawTarget = f;
            }
            catch { return false; }

            float low = 0f, mid = 0.5f, high = 1f;
            bool hasLow = TryReadConfigEntryFloat(inst, _beatCfgLowIntensityField, out low);
            bool hasMid = TryReadConfigEntryFloat(inst, _beatCfgMidIntensityField, out mid);
            bool hasHigh = TryReadConfigEntryFloat(inst, _beatCfgHighIntensityField, out high);
            if (!(hasLow && hasMid && hasHigh)) return false;

            lowIntensity = low;
            midIntensity = mid;
            highIntensity = high;

            float dLow = Mathf.Abs(rawTarget - low);
            float dMid = Mathf.Abs(rawTarget - mid);
            float dHigh = Mathf.Abs(rawTarget - high);
            if (dLow <= dMid && dLow <= dHigh) zone = BeatZone.Low;
            else if (dHigh <= dLow && dHigh <= dMid) zone = BeatZone.High;
            else zone = BeatZone.Mid;

            if (!_beatZoneSourceLogged)
            {
                _beatZoneSourceLogged = true;
                _log.Info("[BeatSync] ゾーン判定ソース=_currentZoneRawTarget (BeatSync準拠)");
            }
            return true;
        }

        internal static float ResolveRainbowCycleSpeed(RainbowSettings rb, bool hasBeatLoopHz, float beatLoopHz, bool hasZoneSpeed, float zoneSpeed01)
        {
            if (rb == null) return 0f;
            if (rb.BeatFollow)
                return Mathf.Lerp(rb.MinCycleSpeed, rb.MaxCycleSpeed, hasZoneSpeed ? zoneSpeed01 : 0f);
            if (rb.VideoLink)
            {
                float v = hasBeatLoopHz ? beatLoopHz : 0f;
                return Mathf.Clamp(v, rb.MinCycleSpeed, rb.MaxCycleSpeed);
            }
            return rb.CycleSpeed;
        }

        internal static float ResolveStrobeFrequency(StrobeSettings st, bool hasBeatLoopHz, float beatLoopHz, bool hasZoneSpeed, float zoneSpeed01)
        {
            if (st == null) return 0f;
            if (st.BeatFollow)
                return Mathf.Lerp(st.MinFrequencyHz, st.MaxFrequencyHz, hasZoneSpeed ? zoneSpeed01 : 0f);
            if (st.VideoLink)
            {
                float v = hasBeatLoopHz ? beatLoopHz : 0f;
                return Mathf.Clamp(v, st.MinFrequencyHz, st.MaxFrequencyHz);
            }
            return st.FrequencyHz;
        }

        internal static float ResolveStrobeDutyRatio(StrobeSettings st, bool hasBeatLoopHz, float beatLoopHz, bool hasZoneSpeed, float zoneSpeed01, float time)
        {
            if (st == null) return 0.5f;
            if (st.DutyBeatFollow)
                return Mathf.Lerp(st.MinDutyRatio, st.MaxDutyRatio, hasZoneSpeed ? zoneSpeed01 : 0f);
            if (st.DutyVideoLink)
            {
                // BPM 由来 Hz でサイン波を駆動、0..1 に正規化して lerp
                float hz = hasBeatLoopHz ? beatLoopHz : 0f;
                float wave01 = (Mathf.Sin(time * hz * Mathf.PI * 2f) + 1f) * 0.5f;
                return Mathf.Lerp(st.MinDutyRatio, st.MaxDutyRatio, wave01);
            }
            return st.DutyRatio;
        }

        internal bool TryGetBeatZoneSpeed01(out float zoneSpeed01)
        {
            zoneSpeed01 = 0f;
            if (!TryGetBeatZoneFromBeatSync(out BeatZone zone, out float lowIntensity, out float midIntensity, out float highIntensity))
                return false;
            float v = zone == BeatZone.Low  ? lowIntensity
                    : zone == BeatZone.Mid  ? midIntensity
                    : zone == BeatZone.High ? highIntensity
                                            : -1f;
            if (v < 0f) return false;
            zoneSpeed01 = Mathf.Clamp01(v);
            return true;
        }

        internal bool TryGetBeatLinkedLoopHz(out float loopHz)
        {
            loopHz = 0f;

            object inst = GetBeatInstance();
            if (inst == null) return false;
            if (!TryReadConfigEntryInt(inst, _beatCfgBpmField, out int bpm)) return false;

            if (!TryGetBeatZoneFromBeatSync(out BeatZone zone, out float lowIntensity, out float midIntensity, out float highIntensity))
                return false;

            float zoneSpeed = zone == BeatZone.Low  ? lowIntensity
                            : zone == BeatZone.Mid  ? midIntensity
                            : zone == BeatZone.High ? highIntensity
                                                    : -1f;
            if (zoneSpeed < 0f) return false;

            float beatHz = Mathf.Max(1, bpm) / 60f;
            loopHz = beatHz * Mathf.Max(0f, zoneSpeed);
            return true;
        }

        private BeatZone ResolveBeatZone(float intensity, out float lowIntensity, out float highIntensity)
        {
            if (TryGetBeatZoneFromBeatSync(out BeatZone beatSyncZone, out lowIntensity, out _, out highIntensity))
                return beatSyncZone;

            lowIntensity = 0f;
            highIntensity = 1f;
            if (intensity < _settings.BeatLowThreshold) return BeatZone.Low;
            if (intensity < _settings.BeatHighThreshold) return BeatZone.Mid;
            return BeatZone.High;
        }

        internal bool TryGetBeatLinkedDrive01(out float drive01)
        {
            drive01 = 0f;
            float intensity = GetBeatIntensity();
            if (intensity < 0f) return false;

            ResolveBeatZone(intensity, out float lowIntensity, out float highIntensity);
            drive01 = Mathf.InverseLerp(lowIntensity, highIntensity, intensity);
            drive01 = Mathf.Clamp01(drive01);
            return true;
        }

        // ── 毎フレーム ────────────────────────────────────────────────────────

        private void UpdateBeatSync()
        {
            var nativeBeat = _settings.NativeLight.Beat;
            bool hasBeatZoneConsumer = _settings.NativeLight.OverrideEnabled &&
                nativeBeat != null &&
                (!string.IsNullOrEmpty(nativeBeat.LowPresetId) ||
                 !string.IsNullOrEmpty(nativeBeat.MidPresetId) ||
                 !string.IsNullOrEmpty(nativeBeat.HighPresetId));
            if (!hasBeatZoneConsumer)
            {
                foreach (var entry in _lightEntries)
                {
                    var beat = entry.Settings.Beat;
                    if (!string.IsNullOrEmpty(beat.LowPresetId) ||
                        !string.IsNullOrEmpty(beat.MidPresetId) ||
                        !string.IsNullOrEmpty(beat.HighPresetId))
                    {
                        hasBeatZoneConsumer = true;
                        break;
                    }
                }
            }
            if (!hasBeatZoneConsumer)
            {
                _lastBeatZone = BeatZone.Unknown;
                return;
            }
            float intensity = GetBeatIntensity();
            if (intensity < 0f)
            {
                _lastBeatZone = BeatZone.Unknown;
                return;
            }

            BeatZone zone = ResolveBeatZone(intensity, out _, out _);
            if (zone == BeatZone.Unknown)
            {
                _lastBeatZone = BeatZone.Unknown;
                return;
            }

            if (zone == _lastBeatZone) return;
            _lastBeatZone = zone;

            ApplyBeatZoneToLights(zone);
            ApplyBeatZoneToNativeLights(zone);
        }

        private void ApplyBeatZoneToLights(BeatZone zone)
        {
            foreach (var entry in _lightEntries)
            {
                var beat = entry.Settings.Beat;
                string presetId = zone == BeatZone.Low  ? beat.LowPresetId
                                : zone == BeatZone.Mid  ? beat.MidPresetId
                                                        : beat.HighPresetId;
                if (!string.IsNullOrEmpty(presetId))
                    ApplyPresetToLight(entry.Settings, presetId, fromBeatSync: true, reason: $"beat-zone:{zone}");
            }
        }

        private void ResetBeatState()
        {
            _lastBeatZone = BeatZone.Unknown;
        }
    }
}
