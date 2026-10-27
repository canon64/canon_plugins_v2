using BepInEx.Configuration;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge.Audio
{
    internal sealed class SpatialVoiceSettings
    {
        private static readonly string[] PlaybackModeLabels = { "2D（音量一定）", "3D（口元・距離減衰）" };
        internal readonly ConfigEntry<bool> Enabled;
        internal readonly ConfigEntry<float> MinDistance, MaxDistance;
        internal readonly ConfigEntry<string> Rolloff;
        internal SpatialVoiceSettings(ConfigFile config)
        {
            Enabled = config.Bind("SpatialVoice", "Enabled", true,
                new ConfigDescription("セリフと挿入音の再生方式。2Dは距離・向きに関係なく音量一定。3Dは女キャラの口元から鳴り、距離で音量が変化。再生中にも切り替え可能。",
                    null, new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        DispName = "再生方式（2D / 3D）",
                        Order = 10,
                        CustomDrawer = DrawPlaybackMode
                    }));
            MinDistance = config.Bind("SpatialVoice", "MinDistance", 0.5f,
                new ConfigDescription("音量を保つ近距離範囲(m)", new AcceptableValueRange<float>(0.01f, 10f)));
            MaxDistance = config.Bind("SpatialVoice", "MaxDistance", 10f,
                new ConfigDescription("距離減衰の上限(m)。MinDistance以上に補正。対数減衰では上限以遠も減衰後の音量を保つ。", new AcceptableValueRange<float>(0.1f, 100f)));
            Rolloff = config.Bind("SpatialVoice", "Rolloff", "Logarithmic",
                new ConfigDescription("距離減衰。Logarithmic=対数、Linear=線形。ドップラー効果は常にOFF。",
                    new AcceptableValueList<string>("Logarithmic", "Linear")));
        }

        private static void DrawPlaybackMode(ConfigEntryBase entryBase)
        {
            var entry = (ConfigEntry<bool>)entryBase;
            int current = entry.Value ? 1 : 0;
            int selected = GUILayout.Toolbar(current, PlaybackModeLabels, GUILayout.ExpandWidth(true));
            if (selected != current) entry.Value = selected == 1;
        }
    }
}
