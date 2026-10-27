using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace MainGameBlankMapAdd.Integrations
{
    internal sealed class IntegrationRegistry
    {
        private readonly Action<string> _info;
        private readonly Action<string> _warning;
        private bool _subCameraApiMissingLogged;
        private delegate bool SubCameraTextureGetter(out RenderTexture renderTexture, out string reason);
        private SubCameraTextureGetter _subCameraTextureGetter;
        private float _nextSubCameraApiResolveTime;

        internal IntegrationRegistry(Action<string> info, Action<string> warning)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _warning = warning ?? throw new ArgumentNullException(nameof(warning));
        }

        internal sealed class SpeedLimitBreakSnapshot
        {
            public bool ForceVanillaSpeed;
            public bool EnableVideoTimeSpeedCues;
            public float AppliedBpmMax;
        }

        internal sealed class BeatSyncSnapshot
        {
            public bool Enabled;
            public int Bpm;
            public bool AutoMotionSwitch;
            public bool AutoThreshold;
            public float LowThreshold;
            public float HighThreshold;
            public float LowIntensity;
            public float MidIntensity;
            public float HighIntensity;
            public float SmoothTime;
            public float StrongMotionBeats;
            public float WeakMotionBeats;
            public float LowPassHz;
            public bool VerboseLog;
        }

        internal bool TryGetSpeedLimitBreakSnapshot(out SpeedLimitBreakSnapshot snapshot)
        {
            snapshot = null;
            try
            {
                Type pluginType = Type.GetType("MainGameSpeedLimitBreak.Plugin, MainGameSpeedLimitBreak");
                if (pluginType == null)
                    return false;

                PropertyInfo settingsProp = pluginType.GetProperty(
                    "Settings",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object settings = settingsProp?.GetValue(null, null);
                if (settings == null)
                    return false;

                Type st = settings.GetType();
                bool forceVanilla = GetFieldValue<bool>(st, settings, "ForceVanillaSpeed");
                bool timeline = GetFieldValue<bool>(st, settings, "EnableVideoTimeSpeedCues");
                float bpmMax = GetFieldValue<float>(st, settings, "AppliedBpmMax");
                if (float.IsNaN(bpmMax) || float.IsInfinity(bpmMax) || bpmMax <= 0f)
                    bpmMax = 120f;

                snapshot = new SpeedLimitBreakSnapshot
                {
                    ForceVanillaSpeed = forceVanilla,
                    EnableVideoTimeSpeedCues = timeline,
                    AppliedBpmMax = UnityEngine.Mathf.Clamp(bpmMax, 1f, 999f)
                };
                return true;
            }
            catch (Exception ex)
            {
                _warning($"speed-limit-break snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplySpeedLimitBreakSnapshot(SpeedLimitBreakSnapshot snapshot, string reason)
        {
            if (snapshot == null)
                return false;

            try
            {
                Type pluginType = Type.GetType("MainGameSpeedLimitBreak.Plugin, MainGameSpeedLimitBreak");
                if (pluginType == null)
                    return false;

                PropertyInfo instanceProp = pluginType.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object instance = instanceProp?.GetValue(null, null);
                if (instance == null)
                    return false;

                // Apply BPM first (this may auto-disable force-vanilla in SpeedLimitBreak internally).
                MethodInfo applyTap = pluginType.GetMethod(
                    "ApplyTapBpm",
                    BindingFlags.Static | BindingFlags.Public);
                if (applyTap != null)
                {
                    float bpmMax = UnityEngine.Mathf.Clamp(snapshot.AppliedBpmMax, 1f, 999f);
                    applyTap.Invoke(null, new object[] { bpmMax });
                }

                Type instType = instance.GetType();
                SetConfigEntryBool(instType, instance, "_cfgEnableVideoTimeSpeedCues", snapshot.EnableVideoTimeSpeedCues);
                SetConfigEntryBool(instType, instance, "_cfgForceVanillaSpeed", snapshot.ForceVanillaSpeed);

                _info(
                    $"speed-limit-break applied reason={reason} " +
                    $"fv={snapshot.ForceVanillaSpeed} tl={snapshot.EnableVideoTimeSpeedCues} bpmMax={snapshot.AppliedBpmMax:0.##}");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"speed-limit-break apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetBeatSyncSnapshot(out BeatSyncSnapshot snapshot)
        {
            snapshot = null;
            try
            {
                Type pluginType = Type.GetType("MainGameBeatSyncSpeed.Plugin, MainGameBeatSyncSpeed");
                if (pluginType == null)
                    return false;

                PropertyInfo instanceProp = pluginType.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object instance = instanceProp?.GetValue(null, null);
                if (instance == null)
                    return false;

                Type instType = instance.GetType();
                bool enabled = GetConfigEntryBool(instType, instance, "_cfgEnabled", true);
                int bpm = GetConfigEntryInt(instType, instance, "_cfgBpm", 128);
                bool autoMotion = GetConfigEntryBool(instType, instance, "_cfgAutoMotionSwitch", true);
                bool autoThreshold = GetConfigEntryBool(instType, instance, "_cfgAutoThreshold", true);
                float lowThreshold = GetConfigEntryFloat(instType, instance, "_cfgLowThreshold", 0.3f);
                float highThreshold = GetConfigEntryFloat(instType, instance, "_cfgHighThreshold", 0.7f);
                float lowIntensity = GetConfigEntryFloat(instType, instance, "_cfgLowIntensity", 0.25f);
                float midIntensity = GetConfigEntryFloat(instType, instance, "_cfgMidIntensity", 0.5f);
                float highIntensity = GetConfigEntryFloat(instType, instance, "_cfgHighIntensity", 1f);
                float smoothTime = GetConfigEntryFloat(instType, instance, "_cfgSmoothTime", 0.5f);
                float strongBeats = GetConfigEntryFloat(instType, instance, "_cfgStrongMotionBeats", 4f);
                float weakBeats = GetConfigEntryFloat(instType, instance, "_cfgWeakMotionBeats", 4f);
                float lowPassHz = GetConfigEntryFloat(instType, instance, "_cfgLowPassHz", 150f);
                bool verboseLog = GetConfigEntryBool(instType, instance, "_cfgVerboseLog", false);

                lowThreshold = UnityEngine.Mathf.Clamp01(lowThreshold);
                highThreshold = UnityEngine.Mathf.Clamp(highThreshold, lowThreshold + 0.01f, 1f);
                lowIntensity = UnityEngine.Mathf.Clamp01(lowIntensity);
                midIntensity = UnityEngine.Mathf.Clamp01(midIntensity);
                highIntensity = UnityEngine.Mathf.Clamp01(highIntensity);
                smoothTime = UnityEngine.Mathf.Clamp(smoothTime, 0f, 2f);
                strongBeats = UnityEngine.Mathf.Clamp(strongBeats, 0.5f, 64f);
                weakBeats = UnityEngine.Mathf.Clamp(weakBeats, 0.5f, 64f);
                lowPassHz = UnityEngine.Mathf.Clamp(lowPassHz, 50f, 500f);

                snapshot = new BeatSyncSnapshot
                {
                    Enabled = enabled,
                    Bpm = UnityEngine.Mathf.Clamp(bpm, 1, 999),
                    AutoMotionSwitch = autoMotion,
                    AutoThreshold = autoThreshold,
                    LowThreshold = lowThreshold,
                    HighThreshold = highThreshold,
                    LowIntensity = lowIntensity,
                    MidIntensity = midIntensity,
                    HighIntensity = highIntensity,
                    SmoothTime = smoothTime,
                    StrongMotionBeats = strongBeats,
                    WeakMotionBeats = weakBeats,
                    LowPassHz = lowPassHz,
                    VerboseLog = verboseLog
                };
                return true;
            }
            catch (Exception ex)
            {
                _warning($"beat-sync snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyBeatSyncSnapshot(BeatSyncSnapshot snapshot, string reason)
        {
            return TryApplyBeatSyncSnapshot(
                snapshot,
                reason,
                applyEnabled: true,
                preferSavedSongBpm: false,
                applyGlobalAutoMotionSwitch: true,
                applyGlobalAutoThreshold: true,
                applyGlobalVerboseLog: true);
        }

        internal bool TryApplyBeatSyncSnapshot(
            BeatSyncSnapshot snapshot,
            string reason,
            bool applyEnabled,
            bool preferSavedSongBpm,
            bool applyGlobalAutoMotionSwitch,
            bool applyGlobalAutoThreshold,
            bool applyGlobalVerboseLog)
        {
            if (snapshot == null)
                return false;

            try
            {
                Type pluginType = Type.GetType("MainGameBeatSyncSpeed.Plugin, MainGameBeatSyncSpeed");
                if (pluginType == null)
                    return false;

                PropertyInfo instanceProp = pluginType.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object instance = instanceProp?.GetValue(null, null);
                if (instance == null)
                    return false;

                Type instType = instance.GetType();
                int bpm = UnityEngine.Mathf.Clamp(snapshot.Bpm, 1, 999);
                float lowThreshold = UnityEngine.Mathf.Clamp01(snapshot.LowThreshold);
                float highThreshold = UnityEngine.Mathf.Clamp(snapshot.HighThreshold, lowThreshold + 0.01f, 1f);
                float lowIntensity = UnityEngine.Mathf.Clamp01(snapshot.LowIntensity);
                float midIntensity = UnityEngine.Mathf.Clamp01(snapshot.MidIntensity);
                float highIntensity = UnityEngine.Mathf.Clamp01(snapshot.HighIntensity);
                float smoothTime = UnityEngine.Mathf.Clamp(snapshot.SmoothTime, 0f, 2f);
                float strongBeats = UnityEngine.Mathf.Clamp(snapshot.StrongMotionBeats, 0.5f, 64f);
                float weakBeats = UnityEngine.Mathf.Clamp(snapshot.WeakMotionBeats, 0.5f, 64f);
                float lowPassHz = UnityEngine.Mathf.Clamp(snapshot.LowPassHz, 50f, 500f);

                if (applyEnabled)
                    SetConfigEntryBool(instType, instance, "_cfgEnabled", snapshot.Enabled);
                if (applyGlobalAutoMotionSwitch)
                    SetConfigEntryBool(instType, instance, "_cfgAutoMotionSwitch", snapshot.AutoMotionSwitch);
                if (applyGlobalAutoThreshold)
                    SetConfigEntryBool(instType, instance, "_cfgAutoThreshold", snapshot.AutoThreshold);
                SetConfigEntryFloat(instType, instance, "_cfgLowThreshold", lowThreshold);
                SetConfigEntryFloat(instType, instance, "_cfgHighThreshold", highThreshold);
                SetConfigEntryFloat(instType, instance, "_cfgLowIntensity", lowIntensity);
                SetConfigEntryFloat(instType, instance, "_cfgMidIntensity", midIntensity);
                SetConfigEntryFloat(instType, instance, "_cfgHighIntensity", highIntensity);
                SetConfigEntryFloat(instType, instance, "_cfgSmoothTime", smoothTime);
                SetConfigEntryFloat(instType, instance, "_cfgStrongMotionBeats", strongBeats);
                SetConfigEntryFloat(instType, instance, "_cfgWeakMotionBeats", weakBeats);
                SetConfigEntryFloat(instType, instance, "_cfgLowPassHz", lowPassHz);
                if (applyGlobalVerboseLog)
                    SetConfigEntryBool(instType, instance, "_cfgVerboseLog", snapshot.VerboseLog);

                int songMapBpm = 0;
                bool loadedFromSongMap = preferSavedSongBpm &&
                    TryApplyBeatSyncSavedSongBpm(instType, instance, out songMapBpm);
                int appliedBpm = bpm;
                if (loadedFromSongMap)
                {
                    appliedBpm = UnityEngine.Mathf.Clamp(songMapBpm, 1, 999);
                }
                else
                {
                    SetConfigEntryInt(instType, instance, "_cfgBpm", bpm);
                }

                _info(
                    $"beat-sync applied reason={reason} " +
                    $"enabled={(applyEnabled ? snapshot.Enabled.ToString() : "unchanged")} bpm={appliedBpm} bpmSrc={(loadedFromSongMap ? "song-map" : "profile")} autoMotion={snapshot.AutoMotionSwitch} " +
                    $"autoThreshold={snapshot.AutoThreshold} low/high={lowThreshold:0.###}/{highThreshold:0.###}");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"beat-sync apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetHipHijackUiVisible(out bool visible)
        {
            visible = false;
            try
            {
                Type pluginType = Type.GetType("MainGirlHipHijack.Plugin, MainGirlHipHijack");
                if (pluginType == null)
                    return false;

                PropertyInfo instanceProp = pluginType.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object instance = instanceProp?.GetValue(null, null);
                if (instance == null)
                    return false;

                Type instType = instance.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                FieldInfo cfgUiField = instType.GetField("_cfgUiVisible", flags);
                if (cfgUiField != null)
                {
                    visible = GetConfigEntryBool(instType, instance, "_cfgUiVisible", false);
                    return true;
                }

                FieldInfo settingsField = instType.GetField("_settings", flags);
                object settings = settingsField?.GetValue(instance);
                if (settings == null)
                    return false;

                FieldInfo uiVisibleField = settings.GetType().GetField("UiVisible", flags);
                if (uiVisibleField == null || uiVisibleField.FieldType != typeof(bool))
                    return false;

                visible = (bool)uiVisibleField.GetValue(settings);
                return true;
            }
            catch (Exception ex)
            {
                _warning($"hip-hijack ui snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyHipHijackUiVisible(bool visible, string reason)
        {
            try
            {
                Type pluginType = Type.GetType("MainGirlHipHijack.Plugin, MainGirlHipHijack");
                if (pluginType == null)
                    return false;

                PropertyInfo instanceProp = pluginType.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object instance = instanceProp?.GetValue(null, null);
                if (instance == null)
                    return false;

                Type instType = instance.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                FieldInfo cfgUiField = instType.GetField("_cfgUiVisible", flags);
                if (cfgUiField != null)
                {
                    SetConfigEntryBool(instType, instance, "_cfgUiVisible", visible);
                    _info($"hip-hijack ui applied reason={reason} visible={visible}");
                    return true;
                }

                FieldInfo settingsField = instType.GetField("_settings", flags);
                object settings = settingsField?.GetValue(instance);
                if (settings == null)
                    return false;

                FieldInfo uiVisibleField = settings.GetType().GetField("UiVisible", flags);
                if (uiVisibleField == null || uiVisibleField.FieldType != typeof(bool))
                    return false;

                uiVisibleField.SetValue(settings, visible);
                _info($"hip-hijack ui applied via settings reason={reason} visible={visible}");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"hip-hijack ui apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetAfterimageEnabled(out bool enabled)
        {
            enabled = false;
            try
            {
                if (!TryGetAfterimagePluginInstance(out object instance, out Type instType))
                    return false;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                FieldInfo cfgEnabledField = instType.GetField("_cfgEnabled", flags);
                if (cfgEnabledField != null)
                {
                    enabled = GetConfigEntryBool(instType, instance, "_cfgEnabled", false);
                    return true;
                }

                FieldInfo settingsField = instType.GetField("_settings", flags);
                object settings = settingsField?.GetValue(instance);
                if (settings == null)
                    return false;

                FieldInfo enabledField = settings.GetType().GetField("Enabled", flags);
                if (enabledField == null || enabledField.FieldType != typeof(bool))
                    return false;

                enabled = (bool)enabledField.GetValue(settings);
                return true;
            }
            catch (Exception ex)
            {
                _warning($"afterimage snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyAfterimageEnabled(bool enabled, string reason)
        {
            try
            {
                if (!TryGetAfterimagePluginInstance(out object instance, out Type instType))
                    return false;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                FieldInfo cfgEnabledField = instType.GetField("_cfgEnabled", flags);
                if (cfgEnabledField != null)
                {
                    SetConfigEntryBool(instType, instance, "_cfgEnabled", enabled);
                    _info($"afterimage applied reason={reason} enabled={enabled}");
                    return true;
                }

                FieldInfo settingsField = instType.GetField("_settings", flags);
                object settings = settingsField?.GetValue(instance);
                if (settings == null)
                    return false;

                FieldInfo enabledField = settings.GetType().GetField("Enabled", flags);
                if (enabledField == null || enabledField.FieldType != typeof(bool))
                    return false;

                enabledField.SetValue(settings, enabled);
                _info($"afterimage applied via settings reason={reason} enabled={enabled}");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"afterimage apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetAfterimagePluginInstance(out object instance, out Type instanceType)
        {
            instance = null;
            instanceType = null;

            try
            {
                if (Chainloader.PluginInfos != null &&
                    Chainloader.PluginInfos.TryGetValue("com.kks.maingame.simpleafterimage", out var pluginInfo))
                {
                    instance = pluginInfo?.Instance;
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch
            {
                // fallback below
            }

            Type pluginType = ResolveAfterimagePluginType();
            if (pluginType == null)
                return false;

            try
            {
                UnityEngine.Object[] instances = UnityEngine.Object.FindObjectsOfType(pluginType);
                if (instances != null && instances.Length > 0)
                {
                    instance = instances[0];
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch
            {
                // fallthrough
            }

            return false;
        }

        // ── MainGameClubLights UI表示 ─────────────────────────────────

        internal bool TryGetClubLightsUiVisible(out bool visible)
        {
            visible = false;
            try
            {
                if (!TryGetClubLightsPluginInstance(out object instance, out Type instType))
                    return false;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                bool pluginEnabled = true;
                FieldInfo cfgEnabledField = instType.GetField("_cfgEnabled", flags);
                if (cfgEnabledField != null)
                    pluginEnabled = GetConfigEntryBool(instType, instance, "_cfgEnabled", true);

                FieldInfo cfgUiField = instType.GetField("_cfgUiVisible", flags);
                if (cfgUiField != null)
                {
                    visible = pluginEnabled && GetConfigEntryBool(instType, instance, "_cfgUiVisible", false);
                    return true;
                }

                FieldInfo settingsField = instType.GetField("_settings", flags);
                object settings = settingsField?.GetValue(instance);
                if (settings == null)
                    return false;

                FieldInfo uiVisibleField = settings.GetType().GetField("UiVisible", flags);
                if (uiVisibleField == null || uiVisibleField.FieldType != typeof(bool))
                    return false;

                visible = pluginEnabled && (bool)uiVisibleField.GetValue(settings);
                return true;
            }
            catch (Exception ex)
            {
                _warning($"club-lights ui snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyClubLightsUiVisible(bool visible, string reason)
        {
            try
            {
                if (!TryGetClubLightsPluginInstance(out object instance, out Type instType))
                    return false;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                bool enabledBefore = GetConfigEntryBool(instType, instance, "_cfgEnabled", true);

                // UIをONにする時は、Plugin有効フラグも同時にONにする。
                if (visible)
                {
                    SetConfigEntryBool(instType, instance, "_cfgEnabled", true);
                    if (!enabledBefore)
                        _info($"club-lights auto-enable reason={reason} enabledBefore={enabledBefore} enabledAfter=true");
                }
                bool enabledAfter = GetConfigEntryBool(instType, instance, "_cfgEnabled", true);

                FieldInfo cfgUiField = instType.GetField("_cfgUiVisible", flags);
                if (cfgUiField != null)
                {
                    SetConfigEntryBool(instType, instance, "_cfgUiVisible", visible);
                    _info($"club-lights ui applied reason={reason} visible={visible} pluginEnabled={enabledAfter}");
                    return true;
                }

                FieldInfo settingsField = instType.GetField("_settings", flags);
                object settings = settingsField?.GetValue(instance);
                if (settings == null)
                    return false;

                FieldInfo uiVisibleField = settings.GetType().GetField("UiVisible", flags);
                if (uiVisibleField == null || uiVisibleField.FieldType != typeof(bool))
                    return false;

                uiVisibleField.SetValue(settings, visible);
                _info($"club-lights ui applied via settings reason={reason} visible={visible} pluginEnabled={enabledAfter}");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"club-lights ui apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetClubLightsPluginInstance(out object instance, out Type instanceType)
        {
            instance = null;
            instanceType = null;
            try
            {
                if (Chainloader.PluginInfos != null &&
                    Chainloader.PluginInfos.TryGetValue("com.kks.maingame.clublights", out var pluginInfo))
                {
                    instance = pluginInfo?.Instance;
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch
            {
                // fallback below
            }

            Type pluginType = Type.GetType("MainGameClubLights.Plugin, MainGameClubLights");
            if (pluginType == null)
                return false;

            try
            {
                PropertyInfo instanceProp = pluginType.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object staticInstance = instanceProp?.GetValue(null, null);
                if (staticInstance != null)
                {
                    instance = staticInstance;
                    instanceType = staticInstance.GetType();
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                UnityEngine.Object[] instances = UnityEngine.Object.FindObjectsOfType(pluginType);
                if (instances != null && instances.Length > 0)
                {
                    instance = instances[0];
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        // ── MainGameDollMode 人形モード ───────────────────────────────

        internal bool TryGetDollModeEnabled(out bool enabled)
        {
            enabled = false;
            try
            {
                Type pluginType = Type.GetType("MainGameDollMode.Plugin, MainGameDollMode");
                if (pluginType == null)
                    return false;

                MethodInfo getter = pluginType.GetMethod(
                    "IsDollModeEnabled",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                if (getter == null || getter.ReturnType != typeof(bool))
                    return false;

                object raw = getter.Invoke(null, null);
                if (raw is bool b)
                {
                    enabled = b;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _warning($"doll-mode snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyDollModeEnabled(bool enabled, string reason)
        {
            try
            {
                Type pluginType = Type.GetType("MainGameDollMode.Plugin, MainGameDollMode");
                if (pluginType == null)
                    return false;

                MethodInfo setterWithSource = pluginType.GetMethod(
                    "SetDollModeEnabled",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(bool), typeof(string) },
                    null);
                if (setterWithSource != null && setterWithSource.ReturnType == typeof(bool))
                {
                    object raw = setterWithSource.Invoke(null, new object[] { enabled, "blankmapadd:" + reason });
                    bool ok = raw is bool b && b;
                    if (ok)
                    {
                        _info($"doll-mode applied reason={reason} enabled={enabled} mode=with-source");
                        return true;
                    }
                }

                MethodInfo setter = pluginType.GetMethod(
                    "SetDollModeEnabled",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(bool) },
                    null);
                if (setter == null || setter.ReturnType != typeof(bool))
                    return false;

                object rawFallback = setter.Invoke(null, new object[] { enabled });
                bool okFallback = rawFallback is bool fb && fb;
                if (okFallback)
                {
                    _info($"doll-mode applied reason={reason} enabled={enabled} mode=default");
                }
                else
                {
                    _warning($"doll-mode apply rejected reason={reason} enabled={enabled}");
                }

                return okFallback;
            }
            catch (Exception ex)
            {
                _warning($"doll-mode apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        // ── VoiceFaceEventBridge 体位変更 ─────────────────────────────

        internal bool TryGetVfebPoseChangeEnabled(out bool enabled)
        {
            enabled = false;
            try
            {
                if (!TryGetVfebPluginInstance(out object instance, out Type instType))
                    return false;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                FieldInfo field = instType.GetField("_poseChangeEnabled", flags);
                if (field == null || field.FieldType != typeof(bool))
                    return false;

                enabled = (bool)field.GetValue(instance);
                return true;
            }
            catch (Exception ex)
            {
                _warning($"vfeb pose-change get failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyVfebPoseChangeEnabled(bool enabled, string reason)
        {
            try
            {
                if (!TryGetVfebPluginInstance(out object instance, out Type instType))
                    return false;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // _poseChangeEnabled フィールド
                FieldInfo field = instType.GetField("_poseChangeEnabled", flags);
                if (field != null && field.FieldType == typeof(bool))
                    field.SetValue(instance, enabled);

                // _cfgPoseChangeEnabled ConfigEntry
                SetConfigEntryBool(instType, instance, "_cfgPoseChangeEnabled", enabled);

                _info($"vfeb pose-change applied reason={reason} enabled={enabled}");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"vfeb pose-change apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        // ── VoiceFaceEventBridge 状況文送信 ─────────────────────────────

        internal bool TryGetVfebScenarioTextAutoSendEnabled(out bool enabled)
        {
            enabled = false;
            try
            {
                if (!TryGetVfebPluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo getter = instType.GetMethod(
                    "TryGetScenarioTextAutoSendEnabled",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(bool).MakeByRefType() },
                    null);
                if (getter == null || getter.ReturnType != typeof(bool))
                    return false;

                object[] args = { false };
                object raw = getter.Invoke(null, args);
                bool ok = raw is bool b && b;
                if (!ok)
                    return false;

                enabled = args[0] is bool value && value;
                return true;
            }
            catch (Exception ex)
            {
                _warning($"vfeb scenario auto get failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyVfebScenarioTextAutoSendEnabled(bool enabled, string reason)
        {
            try
            {
                if (!TryGetVfebPluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo setter = instType.GetMethod(
                    "TrySetScenarioTextAutoSendEnabled",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(bool), typeof(string), typeof(string).MakeByRefType() },
                    null);
                if (setter == null || setter.ReturnType != typeof(bool))
                    return false;

                object[] args = { enabled, reason, null };
                object raw = setter.Invoke(null, args);
                bool ok = raw is bool b && b;
                string detail = args[2] as string;
                if (ok)
                {
                    _info($"vfeb scenario auto applied reason={reason} enabled={enabled}");
                }
                else
                {
                    _warning($"vfeb scenario auto rejected reason={reason} enabled={enabled} detail={detail}");
                }

                return ok;
            }
            catch (Exception ex)
            {
                _warning($"vfeb scenario auto apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TrySendVfebScenarioTextManualNow(string reason)
        {
            try
            {
                if (!TryGetVfebPluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo sender = instType.GetMethod(
                    "TrySendScenarioTextManualNow",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(string), typeof(string).MakeByRefType() },
                    null);
                if (sender == null || sender.ReturnType != typeof(bool))
                    return false;

                object[] args = { reason, null };
                object raw = sender.Invoke(null, args);
                bool ok = raw is bool b && b;
                string detail = args[1] as string;
                if (ok)
                {
                    _info($"vfeb scenario manual sent reason={reason}");
                }
                else
                {
                    _warning($"vfeb scenario manual rejected reason={reason} detail={detail}");
                }

                return ok;
            }
            catch (Exception ex)
            {
                _warning($"vfeb scenario manual send failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        // ── MainGameCameraControl UI表示 ──────────────────────────────

        internal bool TryGetCameraControlUiVisible(out bool visible)
        {
            visible = false;
            try
            {
                if (!TryGetCameraControlPluginInstance(out object instance, out Type instType))
                    return false;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo cfgUiField = instType.GetField("_cfgUiVisible", flags);
                if (cfgUiField != null)
                {
                    visible = GetConfigEntryBool(instType, instance, "_cfgUiVisible", true);
                    return true;
                }

                MethodInfo getter = instType.GetMethod("TryGetUiVisible", BindingFlags.Static | BindingFlags.Public);
                if (getter != null && getter.ReturnType == typeof(bool))
                {
                    object[] args = { false };
                    object raw = getter.Invoke(null, args);
                    bool ok = raw is bool b && b;
                    if (ok)
                    {
                        visible = args[0] is bool v && v;
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _warning($"camera ui snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyCameraControlUiVisible(bool visible, string reason)
        {
            try
            {
                if (!TryGetCameraControlPluginInstance(out object instance, out Type instType))
                    return false;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo cfgUiField = instType.GetField("_cfgUiVisible", flags);
                if (cfgUiField != null)
                {
                    SetConfigEntryBool(instType, instance, "_cfgUiVisible", visible);
                }

                MethodInfo setter = instType.GetMethod("TrySetUiVisible", BindingFlags.Static | BindingFlags.Public);
                if (setter != null && setter.ReturnType == typeof(bool))
                {
                    object raw = setter.Invoke(null, new object[] { visible });
                    if (raw is bool b && b)
                    {
                        _info($"camera ui applied reason={reason} visible={visible} mode=api");
                        return true;
                    }
                }

                _info($"camera ui applied reason={reason} visible={visible} mode=field");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"camera ui apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetCameraControlPluginInstance(out object instance, out Type instanceType)
        {
            instance = null;
            instanceType = null;
            try
            {
                if (Chainloader.PluginInfos != null &&
                    Chainloader.PluginInfos.TryGetValue("com.kks.maingame.cameracontrol", out var pluginInfo))
                {
                    instance = pluginInfo?.Instance;
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ── MainGameSubCameraDisplayProbe UI表示 ─────────────────────

        internal bool TryGetSubCameraDisplayProbeUiVisible(out bool visible)
        {
            visible = false;
            try
            {
                if (!TryGetSubCameraDisplayProbePluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo getter = instType.GetMethod("TryGetUiVisible", BindingFlags.Static | BindingFlags.Public);
                if (getter != null && getter.ReturnType == typeof(bool))
                {
                    object[] args = { false };
                    object raw = getter.Invoke(null, args);
                    bool ok = raw is bool b && b;
                    if (ok)
                    {
                        visible = args[0] is bool v && v;
                        return true;
                    }
                }

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo cfgShowField = instType.GetField("_cfgShowWindow", flags);
                if (cfgShowField != null)
                {
                    visible = GetConfigEntryBool(instType, instance, "_cfgShowWindow", false);
                    return true;
                }

                FieldInfo cfgUiField = instType.GetField("_cfgUiVisible", flags);
                if (cfgUiField != null)
                {
                    visible = GetConfigEntryBool(instType, instance, "_cfgUiVisible", false);
                    return true;
                }

                FieldInfo settingsField = instType.GetField("_settings", flags);
                object settings = settingsField?.GetValue(instance);
                FieldInfo uiVisibleField = settings?.GetType().GetField("UiVisible", flags);
                if (uiVisibleField == null || uiVisibleField.FieldType != typeof(bool))
                    return false;

                visible = (bool)uiVisibleField.GetValue(settings);
                return true;
            }
            catch (Exception ex)
            {
                _warning($"subcam ui snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplySubCameraDisplayProbeUiVisible(bool visible, string reason)
        {
            try
            {
                if (!TryGetSubCameraDisplayProbePluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo setter = instType.GetMethod("TrySetUiVisible", BindingFlags.Static | BindingFlags.Public);
                if (setter != null && setter.ReturnType == typeof(bool))
                {
                    object raw = setter.Invoke(null, new object[] { visible });
                    if (raw is bool b && b)
                    {
                        _info($"subcam ui applied reason={reason} visible={visible} mode=api");
                        return true;
                    }
                }

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo cfgShowField = instType.GetField("_cfgShowWindow", flags);
                if (cfgShowField != null)
                {
                    SetConfigEntryBool(instType, instance, "_cfgShowWindow", visible);
                    _info($"subcam ui applied reason={reason} visible={visible} mode=field-show-window");
                    return true;
                }

                FieldInfo cfgUiField = instType.GetField("_cfgUiVisible", flags);
                if (cfgUiField != null)
                {
                    SetConfigEntryBool(instType, instance, "_cfgUiVisible", visible);
                    _info($"subcam ui applied reason={reason} visible={visible} mode=field-ui-visible");
                    return true;
                }

                FieldInfo settingsField = instType.GetField("_settings", flags);
                object settings = settingsField?.GetValue(instance);
                FieldInfo uiVisibleField = settings?.GetType().GetField("UiVisible", flags);
                if (uiVisibleField == null || uiVisibleField.FieldType != typeof(bool))
                    return false;

                uiVisibleField.SetValue(settings, visible);
                _info($"subcam ui applied reason={reason} visible={visible} mode=settings-field");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"subcam ui apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetSubCameraDisplayProbePluginInstance(out object instance, out Type instanceType)
        {
            instance = null;
            instanceType = null;
            try
            {
                if (Chainloader.PluginInfos != null &&
                    Chainloader.PluginInfos.TryGetValue("com.kks.maingame.subcameradisplayprobe", out var pluginInfo))
                {
                    instance = pluginInfo?.Instance;
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ── MainGameObjectComposer UI表示 ─────────────────────────────

        internal bool TryGetObjectComposerPluginInstance(out object instance, out Type instanceType)
        {
            instance = null;
            instanceType = null;
            try
            {
                if (Chainloader.PluginInfos != null &&
                    Chainloader.PluginInfos.TryGetValue("com.kks.main.gameobjectcomposer", out var pluginInfo))
                {
                    instance = pluginInfo?.Instance;
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        internal bool TryGetObjectComposerUiVisible(out bool visible)
        {
            visible = false;
            try
            {
                if (!TryGetObjectComposerPluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo getter = instType.GetMethod("TryGetUiVisible", BindingFlags.Static | BindingFlags.Public);
                if (getter != null && getter.ReturnType == typeof(bool))
                {
                    object[] args = { false };
                    object raw = getter.Invoke(null, args);
                    if (raw is bool ok && ok)
                    {
                        visible = args[0] is bool v && v;
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _warning($"object-composer ui snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyObjectComposerUiVisible(bool visible, string reason)
        {
            try
            {
                if (!TryGetObjectComposerPluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo setter = instType.GetMethod("TrySetUiVisible", BindingFlags.Static | BindingFlags.Public);
                if (setter != null && setter.ReturnType == typeof(bool))
                {
                    object raw = setter.Invoke(null, new object[] { visible });
                    if (raw is bool b && b)
                    {
                        _info($"object-composer ui applied reason={reason} visible={visible} mode=api");
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _warning($"object-composer ui apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        // ── MainGameAutoHVoice UI表示 ────────────────────────────────

        internal bool TryGetAutoHVoiceUiVisible(out bool visible)
        {
            visible = false;
            try
            {
                if (!TryGetAutoHVoicePluginInstance(out object instance, out Type instType))
                    return false;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo cfgUiField = instType.GetField("_cfgUiVisible", flags);
                if (cfgUiField != null)
                {
                    visible = GetConfigEntryBool(instType, instance, "_cfgUiVisible", true);
                    return true;
                }

                MethodInfo getter = instType.GetMethod("TryGetUiVisible", BindingFlags.Static | BindingFlags.Public);
                if (getter != null && getter.ReturnType == typeof(bool))
                {
                    object[] args = { false };
                    object raw = getter.Invoke(null, args);
                    bool ok = raw is bool b && b;
                    if (ok)
                    {
                        visible = args[0] is bool v && v;
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _warning($"auto-hvoice ui snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyAutoHVoiceUiVisible(bool visible, string reason)
        {
            try
            {
                if (!TryGetAutoHVoicePluginInstance(out object instance, out Type instType))
                    return false;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo cfgUiField = instType.GetField("_cfgUiVisible", flags);
                if (cfgUiField != null)
                {
                    SetConfigEntryBool(instType, instance, "_cfgUiVisible", visible);
                }

                MethodInfo setter = instType.GetMethod("TrySetUiVisible", BindingFlags.Static | BindingFlags.Public);
                if (setter != null && setter.ReturnType == typeof(bool))
                {
                    object raw = setter.Invoke(null, new object[] { visible });
                    if (raw is bool b && b)
                    {
                        _info($"auto-hvoice ui applied reason={reason} visible={visible} mode=api");
                        return true;
                    }
                }

                _info($"auto-hvoice ui applied reason={reason} visible={visible} mode=field");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"auto-hvoice ui apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetAutoHVoicePluginInstance(out object instance, out Type instanceType)
        {
            instance = null;
            instanceType = null;
            try
            {
                if (Chainloader.PluginInfos != null &&
                    Chainloader.PluginInfos.TryGetValue("com.kks.maingame.autohvoice", out var pluginInfo))
                {
                    instance = pluginInfo?.Instance;
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        internal bool TryGetVfebPluginInstance(out object instance, out Type instanceType)
        {
            instance = null;
            instanceType = null;
            try
            {
                if (Chainloader.PluginInfos != null &&
                    Chainloader.PluginInfos.TryGetValue("com.kks.maingame.voicefaceeventbridge", out var pluginInfo))
                {
                    instance = pluginInfo?.Instance;
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        internal static Type ResolveAfterimagePluginType()
        {
            Type pluginType = Type.GetType("SimpleAfterimage.Plugin, SimpleAfterimage");
            if (pluginType != null)
                return pluginType;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (assemblies == null || assemblies.Length == 0)
                return null;

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly asm = assemblies[i];
                if (asm == null)
                    continue;

                string asmName = asm.GetName().Name;
                if (!string.Equals(asmName, "SimpleAfterimage", StringComparison.Ordinal))
                    continue;

                Type t = asm.GetType("SimpleAfterimage.Plugin", false);
                if (t != null)
                    return t;
            }

            return assemblies
                .Select(a => a?.GetType("SimpleAfterimage.Plugin", false))
                .FirstOrDefault(t => t != null);
        }

        internal bool TryApplyBeatSyncSavedSongBpm(Type instType, object instance, out int bpm)
        {
            bpm = 0;
            if (instType == null || instance == null)
                return false;

            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                FieldInfo currentSongKeyField = instType.GetField("_currentSongPathKey", flags);
                currentSongKeyField?.SetValue(instance, null);

                FieldInfo nextPollField = instType.GetField("_nextSongPathPollTime", flags);
                if (nextPollField != null)
                {
                    nextPollField.SetValue(instance, 0f);
                }

                MethodInfo refreshMethod = instType.GetMethod("RefreshSongBpmAutoLoad", flags);
                if (refreshMethod == null)
                    return false;

                refreshMethod.Invoke(instance, null);

                string key = currentSongKeyField?.GetValue(instance) as string;
                if (string.IsNullOrWhiteSpace(key))
                    return false;

                FieldInfo mapField = instType.GetField("_songBpmByPath", flags);
                if (mapField == null)
                    return false;

                var map = mapField.GetValue(instance) as IDictionary;
                if (map == null || !map.Contains(key))
                    return false;

                object raw = map[key];
                if (raw == null)
                    return false;

                bpm = UnityEngine.Mathf.Clamp(Convert.ToInt32(raw), 1, 999);
                SetConfigEntryInt(instType, instance, "_cfgBpm", bpm);
                return true;
            }
            catch (Exception ex)
            {
                _warning($"beat-sync song-map apply failed: {ex.Message}");
                return false;
            }
        }

        internal sealed class OverlayBeatSnapshot
        {
            public int Bpm;
            public float RawTarget;
            public float LowIntensity;
            public float MidIntensity;
            public float HighIntensity;
        }

        internal bool TryGetOverlayBeatSnapshot(out OverlayBeatSnapshot snapshot)
        {
            snapshot = null;
            try
            {
                Type pluginType = Type.GetType("MainGameBeatSyncSpeed.Plugin, MainGameBeatSyncSpeed");
                if (pluginType == null) return false;
                PropertyInfo instanceProperty = pluginType.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object instance = instanceProperty?.GetValue(null, null);
                if (instance == null) return false;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo rawField = pluginType.GetField("_currentZoneRawTarget", flags);
                object rawValue = rawField?.GetValue(instance);
                if (!(rawValue is float rawTarget) || rawTarget < 0f) return false;

                snapshot = new OverlayBeatSnapshot
                {
                    Bpm = GetConfigEntryInt(pluginType, instance, "_cfgBpm", 0),
                    RawTarget = rawTarget,
                    LowIntensity = GetConfigEntryFloat(pluginType, instance, "_cfgLowIntensity", float.NaN),
                    MidIntensity = GetConfigEntryFloat(pluginType, instance, "_cfgMidIntensity", float.NaN),
                    HighIntensity = GetConfigEntryFloat(pluginType, instance, "_cfgHighIntensity", float.NaN)
                };
                return snapshot.Bpm > 0
                    && !float.IsNaN(snapshot.LowIntensity)
                    && !float.IsNaN(snapshot.MidIntensity)
                    && !float.IsNaN(snapshot.HighIntensity);
            }
            catch
            {
                snapshot = null;
                return false;
            }
        }

        internal bool TryGetSubCameraRenderTexture(out RenderTexture renderTexture, out string reason)
        {
            renderTexture = null;
            reason = string.Empty;
            try
            {
                if (_subCameraTextureGetter == null)
                {
                    if (Time.unscaledTime < _nextSubCameraApiResolveTime)
                    {
                        reason = "api_unavailable";
                        return false;
                    }
                    _nextSubCameraApiResolveTime = Time.unscaledTime + 1f;
                    Type apiType = Type.GetType(
                        "MainGameSubCameraDisplayProbe.MainGameSubCameraDisplayProbeApi, MainGameSubCameraDisplayProbe",
                        throwOnError: false);
                    if (apiType == null)
                    {
                        reason = "api_type_not_found";
                        if (!_subCameraApiMissingLogged)
                        {
                            _info("[overlay] subcamera api type not found");
                            _subCameraApiMissingLogged = true;
                        }
                        return false;
                    }

                    MethodInfo method = apiType.GetMethod("TryGetRenderTexture", BindingFlags.Public | BindingFlags.Static);
                    if (method == null)
                    {
                        reason = "api_method_not_found";
                        if (!_subCameraApiMissingLogged)
                        {
                            _info("[overlay] subcamera TryGetRenderTexture method not found");
                            _subCameraApiMissingLogged = true;
                        }
                        return false;
                    }
                    _subCameraTextureGetter = (SubCameraTextureGetter)Delegate.CreateDelegate(
                        typeof(SubCameraTextureGetter), method);
                }

                _subCameraApiMissingLogged = false;
                return _subCameraTextureGetter(out renderTexture, out reason) && renderTexture != null;
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                return false;
            }
        }

        internal void ResetSubCameraProbeLogState()
        {
            _subCameraApiMissingLogged = false;
        }

        // ── MainGameVrScreenEffectProbe VR画面効果 ──────────────────

        internal bool TryGetVrScreenEffectEnabled(out bool enabled)
        {
            enabled = false;
            try
            {
                if (!TryGetVrScreenEffectPluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo getter = instType.GetMethod("TryGetEnabled", BindingFlags.Static | BindingFlags.Public);
                if (getter == null || getter.ReturnType != typeof(bool))
                    return false;

                object[] args = { false };
                object raw = getter.Invoke(null, args);
                if (raw is bool ok && ok)
                {
                    enabled = args[0] is bool v && v;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _warning($"vr-screen-effect snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyVrScreenEffectEnabled(bool enabled, string reason)
        {
            try
            {
                if (!TryGetVrScreenEffectPluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo setter = instType.GetMethod("TrySetEnabled", BindingFlags.Static | BindingFlags.Public);
                if (setter == null || setter.ReturnType != typeof(bool))
                    return false;

                object raw = setter.Invoke(null, new object[] { enabled });
                if (raw is bool ok && ok)
                {
                    _info($"vr-screen-effect applied reason={reason} enabled={enabled}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _warning($"vr-screen-effect apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetVrScreenEffectUiVisible(out bool visible)
        {
            visible = false;
            try
            {
                if (!TryGetVrScreenEffectPluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo getter = instType.GetMethod("TryGetUiVisible", BindingFlags.Static | BindingFlags.Public);
                if (getter == null || getter.ReturnType != typeof(bool))
                    return false;

                object[] args = { false };
                object raw = getter.Invoke(null, args);
                if (raw is bool ok && ok)
                {
                    visible = args[0] is bool v && v;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _warning($"vr-screen-effect ui snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyVrScreenEffectUiVisible(bool visible, string reason)
        {
            try
            {
                if (!TryGetVrScreenEffectPluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo setter = instType.GetMethod("TrySetUiVisible", BindingFlags.Static | BindingFlags.Public);
                if (setter == null || setter.ReturnType != typeof(bool))
                    return false;

                object raw = setter.Invoke(null, new object[] { visible });
                if (raw is bool ok && ok)
                {
                    _info($"vr-screen-effect ui applied reason={reason} visible={visible}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _warning($"vr-screen-effect ui apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetVrScreenEffectPluginInstance(out object instance, out Type instanceType)
        {
            instance = null;
            instanceType = null;
            try
            {
                if (Chainloader.PluginInfos != null &&
                    Chainloader.PluginInfos.TryGetValue("com.kks.maingame.vrscreeneffectprobe", out var pluginInfo))
                {
                    instance = pluginInfo?.Instance;
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ── MainGameUnifiedUi UI表示 ────────────────────────────────

        internal bool TryGetUnifiedUiVisible(out bool visible)
        {
            visible = false;
            try
            {
                if (!TryGetUnifiedUiPluginInstance(out object instance, out Type instType))
                    return false;

                PropertyInfo visibleProp = instType.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (visibleProp != null && visibleProp.PropertyType == typeof(bool))
                {
                    visible = (bool)visibleProp.GetValue(instance, null);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _warning($"unified ui snapshot failed: {ex.Message}");
                return false;
            }
        }

        internal bool TryApplyUnifiedUiVisible(bool visible, string reason)
        {
            try
            {
                if (!TryGetUnifiedUiPluginInstance(out object instance, out Type instType))
                    return false;

                MethodInfo setter = instType.GetMethod("SetVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (setter == null)
                    return false;

                setter.Invoke(instance, new object[] { visible });
                _info($"unified ui applied reason={reason} visible={visible}");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"unified ui apply failed reason={reason} error={ex.Message}");
                return false;
            }
        }

        internal bool TryGetUnifiedUiPluginInstance(out object instance, out Type instanceType)
        {
            instance = null;
            instanceType = null;
            try
            {
                if (Chainloader.PluginInfos != null &&
                    Chainloader.PluginInfos.TryGetValue("com.kks.maingame.unifiedui", out var pluginInfo))
                {
                    instance = pluginInfo?.Instance;
                    if (instance != null)
                    {
                        instanceType = instance.GetType();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        internal static T GetFieldValue<T>(Type ownerType, object owner, string fieldName)
        {
            FieldInfo fi = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi == null)
                return default(T);
            object value = fi.GetValue(owner);
            if (value is T t)
                return t;
            return default(T);
        }

        internal static bool GetConfigEntryBool(Type ownerType, object owner, string fieldName, bool fallback)
        {
            FieldInfo fi = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object entry = fi?.GetValue(owner);
            if (entry == null)
                return fallback;
            PropertyInfo valueProp = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            if (valueProp == null)
                return fallback;
            object raw = valueProp.GetValue(entry, null);
            return raw is bool b ? b : fallback;
        }

        internal static int GetConfigEntryInt(Type ownerType, object owner, string fieldName, int fallback)
        {
            FieldInfo fi = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object entry = fi?.GetValue(owner);
            if (entry == null)
                return fallback;
            PropertyInfo valueProp = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            if (valueProp == null)
                return fallback;
            object raw = valueProp.GetValue(entry, null);
            return raw is int i ? i : fallback;
        }

        internal static float GetConfigEntryFloat(Type ownerType, object owner, string fieldName, float fallback)
        {
            FieldInfo fi = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object entry = fi?.GetValue(owner);
            if (entry == null)
                return fallback;
            PropertyInfo valueProp = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            if (valueProp == null)
                return fallback;
            object raw = valueProp.GetValue(entry, null);
            return raw is float f ? f : fallback;
        }

        internal static void SetConfigEntryBool(Type ownerType, object owner, string fieldName, bool value)
        {
            FieldInfo fi = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object entry = fi?.GetValue(owner);
            if (entry == null)
                return;
            PropertyInfo valueProp = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            valueProp?.SetValue(entry, value, null);
        }

        internal static void SetConfigEntryInt(Type ownerType, object owner, string fieldName, int value)
        {
            FieldInfo fi = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object entry = fi?.GetValue(owner);
            if (entry == null)
                return;
            PropertyInfo valueProp = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            valueProp?.SetValue(entry, value, null);
        }

        internal static void SetConfigEntryFloat(Type ownerType, object owner, string fieldName, float value)
        {
            FieldInfo fi = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object entry = fi?.GetValue(owner);
            if (entry == null)
                return;
            PropertyInfo valueProp = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            valueProp?.SetValue(entry, value, null);
        }
    }
}
