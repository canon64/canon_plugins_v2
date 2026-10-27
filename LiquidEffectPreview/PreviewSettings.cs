using BepInEx.Configuration;
using UnityEngine;

namespace LiquidEffectPreview
{
    internal sealed class PreviewSettings
    {
        public readonly ConfigEntry<bool> Enabled, Paused, EmitOnce, Trails;
        public readonly ConfigEntry<KeyboardShortcut> FireKey;
        public readonly ConfigEntry<Color> Color;
        public readonly ConfigEntry<int> DurationMs, Jets, Drops, BreakupMs;
        public readonly ConfigEntry<float> DiameterMm, Speed, Spread, SizeMm, Life, Gravity, Pitch, Yaw, TrailLengthCm, TrailWidth;
        public readonly ConfigEntry<float> NozzleRight, NozzleUp, NozzleForward;

        public PreviewSettings(ConfigFile config)
        {
            Enabled = config.Bind("01.プレビュー", "Enabled", false, "カメラ前のノズルから液体を手動試射します。OFFにすると表示と噴出予定を消去します。");
            Paused = config.Bind("01.プレビュー", "Paused", false, "液体の時間を停止。停止中の試射入力は予約されません。");
            EmitOnce = config.Bind("01.プレビュー", "EmitOnce", false, "ONにすると一度だけ試射し、このチェックは自動でOFFに戻ります。EnabledがONで停止していない時に使用してください。");
            FireKey = config.Bind("01.プレビュー", "FireKey", new KeyboardShortcut(KeyCode.F8), "一回試射するキー。EnabledがONの時のみ反応します。");
            DurationMs = Slider(config, "02.液体の筋", "BurstDurationMs", 100, 20, 300, "一回の噴出時間(ms)");
            Jets = Slider(config, "02.液体の筋", "JetCount", 3, 1, 5, "独立した筋の本数。滴の数を増やしても筋は増えません。");
            DiameterMm = Slider(config, "02.液体の筋", "JetDiameterMm", 2f, .5f, 5f, "筋の最大直径(mm)。出始め、終わり、分離先端は細くなります。");
            BreakupMs = Slider(config, "02.液体の筋", "BreakupDelayMs", 100, 20, 300, "放出から滴に分かれるまでの時間(ms)");
            Speed = Slider(config, "02.液体の筋", "Speed", 1.5f, .1f, 8f, "初速(m/秒)");
            Spread = Slider(config, "02.液体の筋", "SpreadDegrees", 9f, 0f, 45f, "筋同士の方向の広がり(度)。各筋のばらつきは噴出開始時に決まります。");
            Pitch = Slider(config, "02.液体の筋", "PitchDegrees", 50f, -90f, 90f, "水平を0度とした上下角。90度で真上。");
            Yaw = Slider(config, "02.液体の筋", "YawDegrees", 0f, -180f, 180f, "カメラの水平前方からの左右角(度)");
            Gravity = Slider(config, "02.液体の筋", "Gravity", 6.867f, 0f, 19.62f, "下向きの加速度(m/秒²)");
            Color = config.Bind("03.滴と尾", "Color", UnityEngine.Color.white, "筋・滴・尾に共通する色。初期色は純白。");
            Drops = Slider(config, "03.滴と尾", "DropCount", 49, 1, 80, "一回の噴出で生成する滴の総数");
            SizeMm = Slider(config, "03.滴と尾", "DropSizeMm", 5f, 1f, 40f, "滴の直径(mm)");
            Life = Slider(config, "03.滴と尾", "DropLifetimeSeconds", .7f, .1f, 3f, "分離後の滴の寿命(秒)");
            Trails = config.Bind("03.滴と尾", "TailsEnabled", true, "滴に短い尾を付けます。");
            TrailLengthCm = Slider(config, "03.滴と尾", "TailLengthCm", 5f, 0f, 15f, "尾の最大長(cm)。末端ほど細く透明になります。");
            TrailWidth = Slider(config, "03.滴と尾", "TailWidth", .5f, .05f, 2f, "滴の直径に対する尾の太さの倍率");
            NozzleRight = Slider(config, "04.ノズル位置", "RightMeters", .25f, -1f, 1f, "カメラから右方向の距離(m)");
            NozzleUp = Slider(config, "04.ノズル位置", "UpMeters", -.2f, -1f, 1f, "カメラから上方向の距離(m)");
            NozzleForward = Slider(config, "04.ノズル位置", "ForwardMeters", 1f, .2f, 3f, "カメラから前方向の距離(m)");
        }
        private static ConfigEntry<T> Slider<T>(ConfigFile file, string section, string key, T value, T min, T max, string help)
            where T : System.IComparable => file.Bind(section, key, value, new ConfigDescription(help, new AcceptableValueRange<T>(min, max)));

        public LiquidSpec Snapshot() => new LiquidSpec {
            Duration = DurationMs.Value * .001f, Jets = Jets.Value, Diameter = DiameterMm.Value * .001f,
            Breakup = BreakupMs.Value * .001f, Speed = Speed.Value, SpreadRadians = Spread.Value * Mathf.Deg2Rad,
            Drops = Drops.Value, DropSize = SizeMm.Value * .001f, DropLife = Life.Value, Gravity = Gravity.Value };
    }
}
