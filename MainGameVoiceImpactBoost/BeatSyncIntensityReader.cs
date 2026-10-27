using System.Reflection;
using HarmonyLib;

namespace MainGameVoiceImpactBoost
{
    internal static class BeatSyncIntensityReader
    {
        private static System.Type _beatSyncType;
        private static FieldInfo _beatSyncIntensityField;
        private static bool _resolved;

        public static float Read()
        {
            if (!_resolved)
            {
                _resolved = true;
                _beatSyncType = AccessTools.TypeByName("MainGameBeatSyncSpeed.Plugin")
                    ?? System.Type.GetType("MainGameBeatSyncSpeed.Plugin, MainGameBeatSyncSpeed");
                if (_beatSyncType != null)
                {
                    _beatSyncIntensityField = _beatSyncType.GetField(
                        "CurrentIntensity01",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                }
            }

            if (_beatSyncIntensityField == null)
            {
                return -1f;
            }

            try
            {
                return (float)_beatSyncIntensityField.GetValue(null);
            }
            catch
            {
                return -1f;
            }
        }
    }
}
