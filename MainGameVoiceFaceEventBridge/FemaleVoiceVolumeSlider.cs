using BepInEx.Configuration;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    internal static class FemaleVoiceVolumeSlider
    {
        internal static void Draw(ConfigEntryBase entryBase, float fallbackVolume)
        {
            var entry = (ConfigEntry<float>)entryBase;
            float current = Mathf.Clamp(entry.Value < 0f ? fallbackVolume : entry.Value,
                0f, PluginSettings.MaxFemalePlaybackVolume);

            GUILayout.BeginHorizontal();
            GUILayout.Label("0%", GUILayout.Width(28f));
            float next = GUILayout.HorizontalSlider(current, 0f, PluginSettings.MaxFemalePlaybackVolume,
                GUILayout.ExpandWidth(true));
            GUILayout.Label("500%", GUILayout.Width(42f));
            if (!Mathf.Approximately(next, current))
                entry.Value = Mathf.Round(next * 100f) / 100f;
            float displayed = entry.Value < 0f ? fallbackVolume : entry.Value;
            GUILayout.Label(Mathf.RoundToInt(displayed * 100f) + "%", GUILayout.Width(52f));
            GUILayout.EndHorizontal();
        }
    }
}
