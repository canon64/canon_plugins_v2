using BepInEx;
using BepInEx.Configuration;
using System;
using System.Reflection;

namespace MainGameSpeedLimitBreak
{
    // Compatibility surface only. Speed logic lives in MainGameRhythmSync.
    [BepInPlugin("com.kks.maingame.speedlimitbreak", "MainGameSpeedLimitBreak Bridge", "1.0.0")]
    [BepInDependency("com.kks.maingame.rhythmsync")]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static object Settings => CoreType?.GetProperty("Settings", AnyStatic)?.GetValue(null, null);

        private static readonly BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type CoreType => Type.GetType("MainGameSpeedLimitBreak.Plugin, MainGameRhythmSync", false);
        private static object Core => CoreType?.GetProperty("Instance", AnyStatic)?.GetValue(null, null);

        private ConfigEntry<bool> _cfgEnableVideoTimeSpeedCues;
        private ConfigEntry<bool> _cfgForceVanillaSpeed;
        private ConfigEntry<bool> _cfgShowBpmUi;

        private void Awake()
        {
            Instance = this;
            ResolveConfigFields();
        }

        private void ResolveConfigFields()
        {
            object core = Core;
            Type type = CoreType;
            if (core == null || type == null) return;
            _cfgEnableVideoTimeSpeedCues = type.GetField("_cfgEnableVideoTimeSpeedCues", AnyInstance)?.GetValue(core) as ConfigEntry<bool>;
            _cfgForceVanillaSpeed = type.GetField("_cfgForceVanillaSpeed", AnyInstance)?.GetValue(core) as ConfigEntry<bool>;
            _cfgShowBpmUi = type.GetField("_cfgShowBpmUi", AnyInstance)?.GetValue(core) as ConfigEntry<bool>;
        }

        public static void ApplyTapBpm(float bpmMax)
        {
            CoreType?.GetMethod("ApplyTapBpm", AnyStatic)?.Invoke(null, new object[] { bpmMax });
        }

        internal static float BpmFromGauge(float gauge01)
        {
            Type mainType = Type.GetType("MainGameRhythmSync.Plugin, MainGameRhythmSync", false);
            object main = mainType?.GetProperty("Instance", AnyStatic)?.GetValue(null, null);
            object active = mainType?.GetProperty("IsPhaseSyncActive", AnyInstance)?.GetValue(main, null);
            if (main != null && active is bool isActive && isActive)
            {
                object value = mainType.GetProperty("CurrentCycleBpm", AnyInstance)?.GetValue(main, null);
                if (value is float bpm && bpm > 0f) return bpm;
            }
            object fallback = CoreType?.GetMethod("BpmFromGauge", AnyStatic)?.Invoke(null, new object[] { gauge01 });
            return fallback is float f ? f : -1f;
        }

        private void ShowUiNotice(string text)
        {
            CoreType?.GetMethod("ShowUiNotice", AnyInstance)?.Invoke(Core, new object[] { text });
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
