using BepInEx.Configuration;

namespace MainGameVoiceFaceEventBridge.Audio
{
    internal sealed class ImpactCueSettings
    {
        internal readonly ConfigEntry<int> DialogueFadeOutMs, DialogueFadeInMs, CueFadeInMs, CueFadeOutMs, StopFadeOutMs, CooldownMs;
        internal readonly ConfigEntry<float> Volume;
        internal readonly ConfigEntry<float> DialogueDowntimeMultiplier;
        internal readonly ConfigEntry<float> TriggerChancePercent;
        internal readonly ConfigEntry<string> CueSource;
        internal ImpactCueSettings(ConfigFile config)
        {
            CueSource = config.Bind("ImpactCue", "CueSource", "SBV2",
                new ConfigDescription("挿入音の音源。SBV2: 選択済みの合成音声21本、Original: 既存の選択音声22本。この設定から切り替え、次の発火から反映。",
                    new AcceptableValueList<string>("SBV2", "Original")));
            DialogueFadeOutMs = Bind(config, "DialogueFadeOutMs", 80, "セリフの一時停止前のフェード時間(ms)");
            DialogueFadeInMs = Bind(config, "DialogueFadeInMs", 120, "セリフ再開時のフェード時間(ms)");
            CueFadeInMs = Bind(config, "CueFadeInMs", 80, "挿入音の冒頭フェード(ms)");
            CueFadeOutMs = Bind(config, "CueFadeOutMs", 80, "挿入音の末尾フェード(ms)");
            StopFadeOutMs = Bind(config, "StopFadeOutMs", 60, "モードOFF時の挿入音フェード(ms)");
            CooldownMs = Bind(config, "CooldownMs", 150, "挿入音終了後の追加間隔(ms)");
            DialogueDowntimeMultiplier = config.Bind("ImpactCue", "DialogueDowntimeMultiplier", 2f,
                new ConfigDescription("セリフ中のダウンタイム倍率。挿入音が鳴った秒数×この値だけ、終了後の次の挿入を禁止する。初期値2: 2秒鳴ったら4秒セリフのみ。0で倍率による追加待機なし。CooldownMsも適用。",
                    new AcceptableValueRange<float>(0f, 10f)));
            TriggerChancePercent = config.Bind("ImpactCue", "TriggerChancePercent", 50f,
                new ConfigDescription("喘ぎ声の発火率(%)。S/W・検出しきい値・ダウンタイムの条件を満たした検出1回につき抽選する。50=半分の確率、100=毎回、0=発火しない。外れた検出は予約しない。",
                    new AcceptableValueRange<float>(0f, 100f)));
            Volume = config.Bind("ImpactCue", "Volume", 1f,
                new ConfigDescription("挿入音の音量", new AcceptableValueRange<float>(0f, 1f)));
        }
        private static ConfigEntry<int> Bind(ConfigFile config, string name, int value, string description)
            => config.Bind("ImpactCue", name, value, new ConfigDescription(description, new AcceptableValueRange<int>(0, 5000)));
    }
}
