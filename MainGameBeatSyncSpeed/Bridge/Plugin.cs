using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MainGameBeatSyncSpeed
{
    // Compatibility surface only. All analysis and animation work lives in MainGameRhythmSync.
    [BepInPlugin("com.kks.maingame.beatsyncseed", "MainGameBeatSyncSpeed Bridge", "1.0.0")]
    [BepInDependency("com.kks.maingame.rhythmsync")]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static float CurrentIntensity01 = -1f;

        private object _core;
        private Type _coreType;
        private FieldInfo _intensityField;
        private FieldInfo _zoneRawTargetField;
        private FieldInfo _songPathKeyField;
        private FieldInfo _nextSongPathPollField;
        private float _currentZoneRawTarget = -1f;
        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<int> _cfgBpm;
        private ConfigEntry<bool> _cfgAutoMotionSwitch;
        private ConfigEntry<bool> _cfgAutoThreshold;
        private ConfigEntry<float> _cfgLowThreshold;
        private ConfigEntry<float> _cfgHighThreshold;
        private ConfigEntry<float> _cfgLowIntensity;
        private ConfigEntry<float> _cfgMidIntensity;
        private ConfigEntry<float> _cfgHighIntensity;
        private ConfigEntry<float> _cfgSmoothTime;
        private ConfigEntry<float> _cfgStrongMotionBeats;
        private ConfigEntry<float> _cfgWeakMotionBeats;
        private ConfigEntry<float> _cfgLowPassHz;
        private ConfigEntry<bool> _cfgVerboseLog;
        private Dictionary<string, int> _songBpmByPath;
        private string _currentSongPathKey;
        private float _nextSongPathPollTime;

        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private void Awake()
        {
            Instance = this;
            ResolveCore();
        }

        private void Update()
        {
            if (_core == null && !ResolveCore()) return;
            CurrentIntensity01 = ReadCachedField(_intensityField, null, -1f);
            _currentZoneRawTarget = ReadCachedField(_zoneRawTargetField, _core, -1f);
            _currentSongPathKey = ReadCachedField<string>(_songPathKeyField, _core, null);
            _nextSongPathPollTime = ReadCachedField(_nextSongPathPollField, _core, 0f);
        }

        private bool ResolveCore()
        {
            _coreType = Type.GetType("MainGameBeatSyncSpeed.Plugin, MainGameRhythmSync", false);
            _core = _coreType?.GetProperty("Instance", AnyStatic)?.GetValue(null, null);
            if (_core == null) return false;
            _intensityField = _coreType.GetField("CurrentIntensity01", AnyStatic);
            _zoneRawTargetField = _coreType.GetField("_currentZoneRawTarget", AnyInstance);
            _songPathKeyField = _coreType.GetField("_currentSongPathKey", AnyInstance);
            _nextSongPathPollField = _coreType.GetField("_nextSongPathPollTime", AnyInstance);
            _cfgEnabled = ReadField<ConfigEntry<bool>>("_cfgEnabled", null);
            _cfgBpm = ReadField<ConfigEntry<int>>("_cfgBpm", null);
            _cfgAutoMotionSwitch = ReadField<ConfigEntry<bool>>("_cfgAutoMotionSwitch", null);
            _cfgAutoThreshold = ReadField<ConfigEntry<bool>>("_cfgAutoThreshold", null);
            _cfgLowThreshold = ReadField<ConfigEntry<float>>("_cfgLowThreshold", null);
            _cfgHighThreshold = ReadField<ConfigEntry<float>>("_cfgHighThreshold", null);
            _cfgLowIntensity = ReadField<ConfigEntry<float>>("_cfgLowIntensity", null);
            _cfgMidIntensity = ReadField<ConfigEntry<float>>("_cfgMidIntensity", null);
            _cfgHighIntensity = ReadField<ConfigEntry<float>>("_cfgHighIntensity", null);
            _cfgSmoothTime = ReadField<ConfigEntry<float>>("_cfgSmoothTime", null);
            _cfgStrongMotionBeats = ReadField<ConfigEntry<float>>("_cfgStrongMotionBeats", null);
            _cfgWeakMotionBeats = ReadField<ConfigEntry<float>>("_cfgWeakMotionBeats", null);
            _cfgLowPassHz = ReadField<ConfigEntry<float>>("_cfgLowPassHz", null);
            _cfgVerboseLog = ReadField<ConfigEntry<bool>>("_cfgVerboseLog", null);
            _songBpmByPath = ReadField<Dictionary<string, int>>("_songBpmByPath", null);
            return true;
        }

        private T ReadField<T>(string name, T fallback)
        {
            object value = _coreType?.GetField(name, AnyInstance)?.GetValue(_core);
            return value is T typed ? typed : fallback;
        }

        private static T ReadCachedField<T>(FieldInfo field, object instance, T fallback)
        {
            object value = field?.GetValue(instance);
            return value is T typed ? typed : fallback;
        }

        private void RefreshSongBpmAutoLoad()
        {
            if (_core == null && !ResolveCore()) return;
            _songPathKeyField?.SetValue(_core, _currentSongPathKey);
            _nextSongPathPollField?.SetValue(_core, _nextSongPathPollTime);
            _coreType.GetMethod("RefreshSongBpmAutoLoad", AnyInstance)?.Invoke(_core, null);
            _currentSongPathKey = ReadCachedField<string>(_songPathKeyField, _core, null);
            _nextSongPathPollTime = ReadCachedField(_nextSongPathPollField, _core, 0f);
        }


        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
