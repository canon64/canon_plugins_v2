using System;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameMilkSpray
{
    internal enum ParticleCountMode { 固定, ランダム }

    // Configuration Manager reads these optional tags by field name.
    internal sealed class SettingDisplay
    {
        public string DispName;
        public string Category;
        public bool Browsable = true;
    }

    internal sealed class MilkSettings
    {
        public readonly ConfigEntry<bool> Enabled, Left, Right, Verbose, Trails, PerformanceLog;
        public readonly ConfigEntry<float> GaugeMin, Interval, MinStroke, Speed, Spread, Life, Gravity, Yaw, Pitch;
        public readonly ConfigEntry<float> SizeMinMm, SizeMaxMm, SpeedVariation, GhostOpacity, GhostEndScale, TrailLengthCm, Gloss;
        public readonly ConfigEntry<float> StretchRatio, Opacity, EdgeSoftness, Emission;
        public readonly ConfigEntry<int> Drops, CountMin, CountMax, PhasePercent, BurstDurationMs, GhostCount, GhostLifeMs;
        public readonly ConfigEntry<ParticleCountMode> CountMode;
        public readonly ConfigEntry<ShotTiming> Timing;
        public readonly ConfigEntry<Color> Color;

        public MilkSettings(ConfigFile config)
        {
            const string general = "01.全般", trigger = "02.発動", oldLook = "03.見た目";
            const string particles = "03.液体粒子", ghosts = "04.粒子の残像";
            Enabled = Bind(config, general, "Enabled", true, "有効", "OFFで液体・残像・放出予定を即時消去。プレイバックバーと共通。", general);
            Left = Bind(config, general, "Left", true, "左から出す", "左乳首から出す。次の噴出から反映。", general);
            Right = Bind(config, general, "Right", true, "右から出す", "右乳首から出す。次の噴出から反映。", general);
            Verbose = Bind(config, general, "VerboseLog", false, "診断ログ", "発射の診断ログを出す。", general);
            PerformanceLog = Bind(config, "06.負荷計測", "PerformanceLogEnabled", false, "処理時間を記録",
                "フレーム・処理別・カメラ別の所要時間を_logs/milk-performance.csvに記録。書き込みは別スレッド、最大16MiB×3ファイル。GPU時間は含みません。", "06.負荷計測");
            GaugeMin = F(config, trigger, "FemaleGaugeMin", 70, 0, 100, "女性ゲージ下限", "この値以上、100未満で発動。", trigger);
            Interval = F(config, trigger, "MinIntervalSeconds", .12f, 0, 2, "発射の最短間隔（秒）", "重複発射を防ぐ間隔。", trigger);
            MinStroke = F(config, trigger, "MinStrokeMeters", .008f, .001f, .2f, "最小往復距離（m）", "最深点検出に必要な距離。", trigger);
            Timing = Bind(config, trigger, "ShotTiming", ShotTiming.DepthPeak, "発射タイミング", "DepthPeak=最深点、AnimationPhase=アニメの指定位置。", trigger);
            PhasePercent = I(config, trigger, "AnimationPhasePercent", 50, 0, 99, "アニメの発射位置（%）", "AnimationPhase時のみ。", trigger);
            CountMode = Bind(config, particles, "CountMode", ParticleCountMode.固定, "粒子数の方式", "固定数、または片側ごとに最小～最大から抽選。", particles);
            Drops = I(config, oldLook, "DropsPerNipple", 12, 1, 250, "固定粒子数（1回・片側）", "固定方式で出す個数。残像は数えません。", particles);
            CountMin = I(config, particles, "RandomCountMin", 32, 1, 250, "ランダム粒子数・最小", "ランダム方式の下限。上下逆なら小さい方を下限として使用。", particles);
            CountMax = I(config, particles, "RandomCountMax", 64, 1, 250, "ランダム粒子数・最大", "ランダム方式の上限。", particles);
            SizeMinMm = F(config, particles, "DiameterMinMm", 3, .5f, 20, "粒子の直径・最小（mm）", "この直径～最大直径から粒子ごとに均等に抽選。", particles);
            SizeMaxMm = F(config, particles, "DiameterMaxMm", 7, .5f, 20, "粒子の直径・最大（mm）", "上下逆でも小さい方を最小として使用。", particles);
            StretchRatio = F(config, particles, "StretchRatio", 3, 1, 10, "前後の伸び率（直径の何倍）", "粒子ごとの直径×この比率を全長にします。1倍で球体、3倍で前後に3倍伸ばした楕円体。次の噴出から反映。", particles);
            BurstDurationMs = I(config, "04.液体の筋", "BurstDurationMs", 100, 20, 300, "噴出時間（ms）", "この時間内に粒子を時間差で放出。次の噴出から反映。", particles);
            Speed = F(config, oldLook, "Speed", 1.5f, .1f, 8, "基準速度（m/秒）", "各粒子はこの速度にばらつきを加えて飛びます。", particles);
            SpeedVariation = F(config, particles, "SpeedVariationPercent", 20, 0, 50, "速度のばらつき（±%）", "基準速度の前後に均等にばらつかせます。", particles);
            Spread = F(config, oldLook, "SpreadDegrees", 9, 0, 45, "広がり（度）", "中心方向からの最大角度。各粒子が独立した方向へ飛びます。", particles);
            Life = F(config, oldLook, "LifetimeSeconds", .7f, .1f, 3, "粒子の寿命（秒）", "放出から消えるまで。消えると残像も回収します。", particles);
            Gravity = F(config, oldLook, "Gravity", .7f, 0, 2, "重力の倍率", "放出済みの粒子はワールド座標で落下します。", particles);
            Yaw = F(config, oldLook, "YawDegrees", 0, -90, 90, "左右角度（度）", "中心方向の左右角度。", particles);
            Pitch = F(config, oldLook, "PitchDegrees", 50, -90, 90, "上下角度（度）", "水平からの角度。90度で真上。", particles);
            Color = Bind(config, oldLook, "SprayColor", UnityEngine.Color.white, "液体の色", "粒子と残像の共通色。次の噴出から反映。", particles);
            Gloss = F(config, particles, "GlossPercent", 65, 0, 100, "光沢（%）", "立体粒子の表面の滑らかさ。次の噴出から反映。", particles);
            Opacity = F(config, particles, "OpacityPercent", 100, 0, 100, "不透明度・濃さ（%）", "0で透明、100で不透明。色のアルファ値と掛け合わせ、残像にも反映。", particles);
            EdgeSoftness = F(config, particles, "EdgeSoftnessPercent", 20, 0, 100, "境界の柔らかさ（%）", "0で明瞭な輪郭。上げるほど輪郭の内側へ向かって色と光沢を薄くします。", particles);
            Emission = F(config, particles, "EmissionIntensity", 1, 0, 10, "発光・十字グローの強度（倍率）", "0で発光とグローOFF。液体の色で発光し、周囲に十字状の柔らかな光を重ねます。粒子と残像の透明度に合わせて薄くなります。次の噴出から反映。", particles);
            Trails = Bind(config, oldLook, "TrailsEnabled", true, "残像を付ける", "過去位置に粒子の形を残す。粒子間を線でつなぎません。", ghosts);
            GhostCount = I(config, ghosts, "AfterimageCount", 4, 1, 12, "残像数（1粒あたり）", "1粒の後ろに同時に残す最大個数。", ghosts);
            GhostLifeMs = I(config, ghosts, "AfterimageLifetimeMs", 40, 10, 300, "残像が消える時間（ms）", "残像は過去のワールド位置で静止し、この時間で消えます。", ghosts);
            GhostOpacity = F(config, ghosts, "AfterimageOpacityPercent", 22, 0, 100, "残像の濃さ（%）", "新しい残像の濃さ。古くなるほど透明になります。", ghosts);
            GhostEndScale = F(config, ghosts, "AfterimageEndSizePercent", 60, 0, 100, "消える直前の大きさ（%）", "残像発生時の直径・全長に対する割合。", ghosts);
            TrailLengthCm = F(config, oldLook, "TrailLengthCm", 5, 0, 15, "残像の最大距離（cm）", "元の粒子から離れて残せる最大距離。0で残像なし。", ghosts);
            RemoveRetiredSettings(config);
        }

        private static void RemoveRetiredSettings(ConfigFile config)
        {
            // Bind consumes the saved orphan entry, then Remove removes it from the live config.
            // Disable autosave so the temporary string bindings are never written or exposed to the UI.
            bool autoSave = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                RemoveRetired(config, "03.見た目", "DropSize");
                RemoveRetired(config, "03.見た目", "TrailWidth");
                RemoveRetired(config, "03.見た目", "TrailLifetime");
                RemoveRetired(config, "03.見た目", "ForwardOffset");
                RemoveRetired(config, "03.液体粒子", "LengthMinMm");
                RemoveRetired(config, "03.液体粒子", "LengthMaxMm");
                RemoveRetired(config, "04.液体の筋", "JetCount");
                RemoveRetired(config, "04.液体の筋", "JetDiameterMm");
                RemoveRetired(config, "04.液体の筋", "BreakupDelayMs");
                RemoveRetired(config, "05.噴出口の位置", "LeftOffsetXmm");
                RemoveRetired(config, "05.噴出口の位置", "LeftOffsetYmm");
                RemoveRetired(config, "05.噴出口の位置", "LeftOffsetZmm");
                RemoveRetired(config, "05.噴出口の位置", "RightOffsetXmm");
                RemoveRetired(config, "05.噴出口の位置", "RightOffsetYmm");
                RemoveRetired(config, "05.噴出口の位置", "RightOffsetZmm");
            }
            finally { config.SaveOnConfigSet = autoSave; }
            config.Save();
        }
        private static void RemoveRetired(ConfigFile config, string section, string key)
        {
            config.Bind<string>(section, key, string.Empty);
            config.Remove(new ConfigDefinition(section, key));
        }

        private static ConfigEntry<T> Bind<T>(ConfigFile c, string section, string key, T value, string name, string help, string category, AcceptableValueBase range = null)
            => c.Bind(section, key, value, new ConfigDescription(help, range, new SettingDisplay { DispName = name, Category = category }));
        private static ConfigEntry<float> F(ConfigFile c, string section, string key, float value, float min, float max, string name, string help, string category)
            => Bind(c, section, key, value, name, help, category, new AcceptableValueRange<float>(min, max));
        private static ConfigEntry<int> I(ConfigFile c, string section, string key, int value, int min, int max, string name, string help, string category)
            => Bind(c, section, key, value, name, help, category, new AcceptableValueRange<int>(min, max));

        public LiquidSpec Snapshot()
        {
            Color color = Color.Value;
            return new LiquidSpec {
                RandomCount = CountMode.Value == ParticleCountMode.ランダム, Drops = Drops.Value,
                CountMin = Math.Min(CountMin.Value, CountMax.Value), CountMax = Math.Max(CountMin.Value, CountMax.Value),
                SizeMin = Math.Min(SizeMinMm.Value, SizeMaxMm.Value) * .001f,
                SizeMax = Math.Max(SizeMinMm.Value, SizeMaxMm.Value) * .001f,
                StretchRatio = StretchRatio.Value,
                Duration = BurstDurationMs.Value * .001f, Speed = Speed.Value, SpeedVariation = SpeedVariation.Value * .01f,
                SpreadRadians = Spread.Value * Mathf.Deg2Rad, DropLife = Life.Value, Gravity = Gravity.Value * 9.81f,
                Afterimages = Trails.Value, GhostCount = GhostCount.Value, GhostLife = GhostLifeMs.Value * .001f,
                GhostOpacity = GhostOpacity.Value * .01f, GhostEndScale = GhostEndScale.Value * .01f,
                GhostDistance = TrailLengthCm.Value * .01f, Gloss = Gloss.Value * .01f, EdgeSoftness = EdgeSoftness.Value * .01f, Emission = Emission.Value,
                Red = color.r, Green = color.g, Blue = color.b, Alpha = color.a * Opacity.Value * .01f,
                YawRadians = Yaw.Value * Mathf.Deg2Rad, PitchRadians = Pitch.Value * Mathf.Deg2Rad
            };
        }
    }
}

