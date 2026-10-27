using BepInEx.Configuration;
using MainGameVoiceFaceEventBridge;

namespace MainGameVoiceImpactBoost
{
    internal sealed class PluginConfig
    {
        internal enum TriggerMode
        {
            Always = 0,
            BeatSyncGated = 1,
            OneShot = 2
        }

        public ConfigEntry<bool> Enabled { get; private set; }
        public ConfigEntry<bool> MoanModeEnabled { get; private set; }
        public ConfigEntry<bool> VerboseLog { get; private set; }
        public ConfigEntry<TriggerMode> Mode { get; private set; }
        public ConfigEntry<float> BeatSyncMinIntensity { get; private set; }
        public ConfigEntry<float> MinIntensity { get; private set; }
        public ConfigEntry<int> CooldownMs { get; private set; }
        public ConfigEntry<float> PeakMultiplier { get; private set; }
        public ConfigEntry<int> AttackMs { get; private set; }
        public ConfigEntry<int> HoldMs { get; private set; }
        public ConfigEntry<int> ReleaseMs { get; private set; }
        public ConfigEntry<int> SilenceMs { get; private set; }
        public ConfigEntry<int> SilenceFadeOutMs { get; private set; }
        public ConfigEntry<int> SilenceFadeInMs { get; private set; }
        public ConfigEntry<PublicApi.EasingShape> Easing { get; private set; }

        public PluginConfig(ConfigFile config)
        {
            Enabled = config.Bind("00.General", "Enabled", true, "プラグイン全体のON/OFF");
            MoanModeEnabled = config.Bind("00.General", "MoanModeEnabled", false, "喘ぎ声モード。ON: 選択音声を挿入 / OFF: 従来の音量ブースト");
            VerboseLog = config.Bind("00.General", "VerboseLog", false, "詳細ログをファイルに出力");

            Mode = config.Bind(
                "20.Trigger",
                "Mode",
                TriggerMode.Always,
                "Always=毎ピーク発火 / BeatSyncGated=BeatSync強度しきい以上のみ / OneShot=1Hシーン1回");
            BeatSyncMinIntensity = config.Bind(
                "20.Trigger",
                "BeatSyncMinIntensity",
                0.7f,
                new ConfigDescription(
                    "BeatSyncGated モードでの最小強度 (0..1)",
                    new AcceptableValueRange<float>(0f, 1f)));
            MinIntensity = config.Bind(
                "20.Trigger",
                "MinIntensity",
                0.5f,
                new ConfigDescription(
                    "腹ボコ正規化強度がこの値未満なら発火しない (0..1)",
                    new AcceptableValueRange<float>(0f, 1f)));
            CooldownMs = config.Bind(
                "20.Trigger",
                "CooldownMs",
                0,
                new ConfigDescription(
                    "発火後の最小再発火間隔 (ミリ秒)",
                    new AcceptableValueRange<int>(0, 5000)));

            PeakMultiplier = config.Bind(
                "30.Envelope",
                "PeakMultiplier",
                2.0f,
                new ConfigDescription("ピーク時の音量倍率", new AcceptableValueRange<float>(1f, 5f)));
            AttackMs = config.Bind(
                "30.Envelope",
                "AttackMs",
                20,
                new ConfigDescription("立ち上がり時間 (ミリ秒)", new AcceptableValueRange<int>(0, 2000)));
            HoldMs = config.Bind(
                "30.Envelope",
                "HoldMs",
                30,
                new ConfigDescription("ピーク維持時間 (ミリ秒)", new AcceptableValueRange<int>(0, 2000)));
            ReleaseMs = config.Bind(
                "30.Envelope",
                "ReleaseMs",
                200,
                new ConfigDescription("減衰時間 (ミリ秒)", new AcceptableValueRange<int>(1, 5000)));
            SilenceMs = config.Bind(
                "30.Envelope",
                "SilenceMs",
                100,
                new ConfigDescription(
                    "ブースト直後に挿入する無音時間 (ミリ秒)。0で無効。",
                    new AcceptableValueRange<int>(0, 2000)));
            SilenceFadeOutMs = config.Bind(
                "30.Envelope",
                "SilenceFadeOutMs",
                80,
                new ConfigDescription(
                    "無音挿入前に現在音量から0へ落とすフェードアウト時間 (ミリ秒)",
                    new AcceptableValueRange<int>(0, 1000)));
            SilenceFadeInMs = config.Bind(
                "30.Envelope",
                "SilenceFadeInMs",
                80,
                new ConfigDescription(
                    "無音挿入後に0から通常音量へ戻すフェードイン時間 (ミリ秒)",
                    new AcceptableValueRange<int>(0, 1000)));
            Easing = config.Bind(
                "30.Envelope",
                "Easing",
                PublicApi.EasingShape.CosineInOut,
                "音量フェードのイージング (Linear=旧線形, CosineInOut=滑らかなS字)");
        }
    }
}
