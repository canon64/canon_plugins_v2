using BepInEx.Configuration;
using UnityEngine;

namespace MainGameHandySync
{
    internal sealed partial class Plugin
    {
        private ConfigEntry<float> _cfgNoMusicBpmMin;
        private ConfigEntry<float> _cfgNoMusicBpmMax;
        private bool _noMusicBpmChanged;

        private void SetupNoMusicBpmConfig()
        {
            _cfgNoMusicBpmMin = Config.Bind("HAMP 音楽なし", "最低BPM", 10f,
                new ConfigDescription("音楽を再生していない時、速度ゲージが最低の時にHandyへ送るBPM。停止・一時停止中に適用。",
                    new AcceptableValueRange<float>(1f, 600f),
                    new ConfigurationManagerAttributes { ShowRangeAsPercent = false }));
            _cfgNoMusicBpmMax = Config.Bind("HAMP 音楽なし", "最高BPM", 130f,
                new ConfigDescription("音楽を再生していない時、速度ゲージが最大の時にHandyへ送るBPM。音楽再生中の設定には影響しません。",
                    new AcceptableValueRange<float>(1f, 600f),
                    new ConfigurationManagerAttributes { ShowRangeAsPercent = false }));
            _cfgNoMusicBpmMin.SettingChanged += (_, __) => _noMusicBpmChanged = true;
            _cfgNoMusicBpmMax.SettingChanged += (_, __) => _noMusicBpmChanged = true;
        }

        private float GetNoMusicBpm(float gauge)
        {
            float min = Mathf.Min(_cfgNoMusicBpmMin.Value, _cfgNoMusicBpmMax.Value);
            float max = Mathf.Max(_cfgNoMusicBpmMin.Value, _cfgNoMusicBpmMax.Value);
            return Mathf.Lerp(min, max, gauge);
        }
    }

    // ConfigurationManager reads this field from ConfigDescription.Tags.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? ShowRangeAsPercent;
    }
}
