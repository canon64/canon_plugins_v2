using System;
using BepInEx.Configuration;

namespace MainGameVrHipIkVoiceImpactBoost
{
    internal sealed class PluginSettings
    {
        internal enum EasingMode
        {
            CosineInOut = 0,
            Linear = 1,
        }

        private readonly ConfigEntry<bool> _enabled;
        private readonly ConfigEntry<bool> _moanModeEnabled;
        private readonly ConfigEntry<bool> _verboseLog;
        private readonly ConfigEntry<bool> _requireVrActive;
        private readonly ConfigEntry<bool> _requireBodyIkRunning;

        private readonly ConfigEntry<float> _velocityThresholdMps;
        private readonly ConfigEntry<float> _minDeltaMeters;
        private readonly ConfigEntry<float> _smoothingFactor;
        private readonly ConfigEntry<int> _cooldownMs;

        private readonly ConfigEntry<bool> _enableSpeedScaling;
        private readonly ConfigEntry<float> _speedMaxMps;
        private readonly ConfigEntry<float> _minPeakMultiplier;
        private readonly ConfigEntry<float> _maxPeakMultiplier;
        private readonly ConfigEntry<int> _minSilenceMs;
        private readonly ConfigEntry<int> _maxSilenceMs;

        private readonly ConfigEntry<float> _peakMultiplier;
        private readonly ConfigEntry<int> _attackMs;
        private readonly ConfigEntry<int> _holdMs;
        private readonly ConfigEntry<int> _releaseMs;
        private readonly ConfigEntry<int> _silenceMs;
        private readonly ConfigEntry<EasingMode> _easing;

        // Changed: ConfigurationManager 等で cfg 値が変更されたときに発火。
        // 起動時 ApplyJson による Value 上書きでも発火するが、Plugin 側ではこの後に購読する。
        internal event Action Changed;

        internal PluginSettings(ConfigFile config)
        {
            _enabled = config.Bind(
                "1.全般", "Enabled", true,
                "プラグイン全体の有効化");
            _moanModeEnabled = config.Bind("1.全般", "MoanModeEnabled", false,
                "喘ぎ声モード。ON: セリフの有無に関係なく選択音声を挿入 / OFF: 音量ブースト");
            _verboseLog = config.Bind(
                "1.全般", "VerboseLog", false,
                "詳細ログ出力(cooldownスキップ等を記録)");
            _requireVrActive = config.Bind(
                "1.全般", "RequireVrActive", true,
                "VRモード起動中のみ動作する");
            _requireBodyIkRunning = config.Bind(
                "1.全般", "RequireBodyIkRunning", true,
                "腰BodyIKが有効なときのみ動作する");

            _velocityThresholdMps = config.Bind(
                "2.検出", "VelocityThresholdMps", 0.45f,
                new ConfigDescription(
                    "頭方向への速度しきい値(m/s)。これ以上で発火",
                    new AcceptableValueRange<float>(0.01f, 10f)));
            _minDeltaMeters = config.Bind(
                "2.検出", "MinDeltaMeters", 0.005f,
                new ConfigDescription(
                    "1フレームで頭方向に進んだ最小距離。画面はmm単位、スライダーは0～20mm。数値欄で0.1mmなどを直接入力可能。保存値は互換性維持のためm単位。",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes
                    {
                        DispName = "MinDelta (mm)",
                        CustomDrawer = MillimeterDistanceDrawer.Draw
                    }));
            _smoothingFactor = config.Bind(
                "2.検出", "SmoothingFactor", 0.35f,
                new ConfigDescription(
                    "速度の平滑化係数(0=平滑化なし, 1=即応)",
                    new AcceptableValueRange<float>(0f, 1f)));
            _cooldownMs = config.Bind(
                "2.検出", "CooldownMs", 250,
                new ConfigDescription(
                    "連発防止のクールダウン(ms)。高速ピストンで短くする",
                    new AcceptableValueRange<int>(0, 5000)));

            _enableSpeedScaling = config.Bind(
                "3.速度スケーリング", "EnableSpeedScaling", true,
                "速度に応じてピーク倍率と無音時間を補間する");
            _speedMaxMps = config.Bind(
                "3.速度スケーリング", "SpeedMaxMps", 1.60f,
                new ConfigDescription(
                    "速度スケーリングの上限(m/s)。この速度で最大強度",
                    new AcceptableValueRange<float>(0.05f, 10f)));
            _minPeakMultiplier = config.Bind(
                "3.速度スケーリング", "MinPeakMultiplier", 1.35f,
                new ConfigDescription(
                    "速度しきい値ちょうどでのピーク倍率",
                    new AcceptableValueRange<float>(1f, 5f)));
            _maxPeakMultiplier = config.Bind(
                "3.速度スケーリング", "MaxPeakMultiplier", 2.00f,
                new ConfigDescription(
                    "速度上限到達時のピーク倍率",
                    new AcceptableValueRange<float>(1f, 5f)));
            _minSilenceMs = config.Bind(
                "3.速度スケーリング", "MinSilenceMs", 60,
                new ConfigDescription(
                    "速度しきい値ちょうどでの無音時間(ms)",
                    new AcceptableValueRange<int>(0, 2000)));
            _maxSilenceMs = config.Bind(
                "3.速度スケーリング", "MaxSilenceMs", 180,
                new ConfigDescription(
                    "速度上限到達時の無音時間(ms)",
                    new AcceptableValueRange<int>(0, 2000)));

            _peakMultiplier = config.Bind(
                "4.エンベロープ(スケーリング無効時)", "PeakMultiplier", 1.8f,
                new ConfigDescription(
                    "速度スケーリング無効時のピーク倍率",
                    new AcceptableValueRange<float>(1f, 5f)));
            _attackMs = config.Bind(
                "4.エンベロープ(スケーリング無効時)", "AttackMs", 20,
                new ConfigDescription(
                    "ピークまでの立ち上がり時間(ms)",
                    new AcceptableValueRange<int>(0, 2000)));
            _holdMs = config.Bind(
                "4.エンベロープ(スケーリング無効時)", "HoldMs", 20,
                new ConfigDescription(
                    "ピーク保持時間(ms)",
                    new AcceptableValueRange<int>(0, 2000)));
            _releaseMs = config.Bind(
                "4.エンベロープ(スケーリング無効時)", "ReleaseMs", 220,
                new ConfigDescription(
                    "戻りの減衰時間(ms)",
                    new AcceptableValueRange<int>(1, 5000)));
            _silenceMs = config.Bind(
                "4.エンベロープ(スケーリング無効時)", "SilenceMs", 100,
                new ConfigDescription(
                    "速度スケーリング無効時の無音時間(ms)",
                    new AcceptableValueRange<int>(0, 2000)));
            _easing = config.Bind(
                "4.エンベロープ(スケーリング無効時)", "Easing", EasingMode.CosineInOut,
                "エンベロープのイージング形状");

            SubscribeAll();
        }

        public bool Enabled => _enabled.Value;
        public bool MoanModeEnabled => _moanModeEnabled.Value;
        public bool VerboseLog => _verboseLog.Value;
        public bool RequireVrActive => _requireVrActive.Value;
        public bool RequireBodyIkRunning => _requireBodyIkRunning.Value;

        public float VelocityThresholdMps => _velocityThresholdMps.Value;
        public float MinDeltaMeters => _minDeltaMeters.Value;
        public float SmoothingFactor => _smoothingFactor.Value;
        public int CooldownMs => _cooldownMs.Value;

        public bool EnableSpeedScaling => _enableSpeedScaling.Value;
        public float SpeedMaxMps => _speedMaxMps.Value;
        public float MinPeakMultiplier => _minPeakMultiplier.Value;
        public float MaxPeakMultiplier => _maxPeakMultiplier.Value;
        public int MinSilenceMs => _minSilenceMs.Value;
        public int MaxSilenceMs => _maxSilenceMs.Value;

        public float PeakMultiplier => _peakMultiplier.Value;
        public int AttackMs => _attackMs.Value;
        public int HoldMs => _holdMs.Value;
        public int ReleaseMs => _releaseMs.Value;
        public int SilenceMs => _silenceMs.Value;
        public EasingMode Easing => _easing.Value;

        internal void ApplyJson(SettingsJsonDto dto)
        {
            if (dto == null) return;

            _enabled.Value = dto.Enabled;
            _moanModeEnabled.Value = dto.MoanModeEnabled;
            _verboseLog.Value = dto.VerboseLog;
            _requireVrActive.Value = dto.RequireVrActive;
            _requireBodyIkRunning.Value = dto.RequireBodyIkRunning;

            _velocityThresholdMps.Value = dto.VelocityThresholdMps;
            _minDeltaMeters.Value = dto.MinDeltaMeters;
            _smoothingFactor.Value = dto.SmoothingFactor;
            _cooldownMs.Value = dto.CooldownMs;

            _enableSpeedScaling.Value = dto.EnableSpeedScaling;
            _speedMaxMps.Value = dto.SpeedMaxMps;
            _minPeakMultiplier.Value = dto.MinPeakMultiplier;
            _maxPeakMultiplier.Value = dto.MaxPeakMultiplier;
            _minSilenceMs.Value = dto.MinSilenceMs;
            _maxSilenceMs.Value = dto.MaxSilenceMs;

            _peakMultiplier.Value = dto.PeakMultiplier;
            _attackMs.Value = dto.AttackMs;
            _holdMs.Value = dto.HoldMs;
            _releaseMs.Value = dto.ReleaseMs;
            _silenceMs.Value = dto.SilenceMs;
            _easing.Value = ParseEasing(dto.Easing);
        }

        internal SettingsJsonDto ToJson()
        {
            return new SettingsJsonDto
            {
                Enabled = Enabled,
                MoanModeEnabled = MoanModeEnabled,
                VerboseLog = VerboseLog,
                RequireVrActive = RequireVrActive,
                RequireBodyIkRunning = RequireBodyIkRunning,

                VelocityThresholdMps = VelocityThresholdMps,
                MinDeltaMeters = MinDeltaMeters,
                SmoothingFactor = SmoothingFactor,
                CooldownMs = CooldownMs,

                EnableSpeedScaling = EnableSpeedScaling,
                SpeedMaxMps = SpeedMaxMps,
                MinPeakMultiplier = MinPeakMultiplier,
                MaxPeakMultiplier = MaxPeakMultiplier,
                MinSilenceMs = MinSilenceMs,
                MaxSilenceMs = MaxSilenceMs,

                PeakMultiplier = PeakMultiplier,
                AttackMs = AttackMs,
                HoldMs = HoldMs,
                ReleaseMs = ReleaseMs,
                SilenceMs = SilenceMs,
                Easing = Easing.ToString(),
            };
        }

        private void SubscribeAll()
        {
            _enabled.SettingChanged += Forward;
            _moanModeEnabled.SettingChanged += Forward;
            _verboseLog.SettingChanged += Forward;
            _requireVrActive.SettingChanged += Forward;
            _requireBodyIkRunning.SettingChanged += Forward;
            _velocityThresholdMps.SettingChanged += Forward;
            _minDeltaMeters.SettingChanged += Forward;
            _smoothingFactor.SettingChanged += Forward;
            _cooldownMs.SettingChanged += Forward;
            _enableSpeedScaling.SettingChanged += Forward;
            _speedMaxMps.SettingChanged += Forward;
            _minPeakMultiplier.SettingChanged += Forward;
            _maxPeakMultiplier.SettingChanged += Forward;
            _minSilenceMs.SettingChanged += Forward;
            _maxSilenceMs.SettingChanged += Forward;
            _peakMultiplier.SettingChanged += Forward;
            _attackMs.SettingChanged += Forward;
            _holdMs.SettingChanged += Forward;
            _releaseMs.SettingChanged += Forward;
            _silenceMs.SettingChanged += Forward;
            _easing.SettingChanged += Forward;
        }

        private void Forward(object sender, EventArgs e)
        {
            Changed?.Invoke();
        }

        private static EasingMode ParseEasing(string value)
        {
            if (string.Equals(value, "Linear", StringComparison.OrdinalIgnoreCase))
            {
                return EasingMode.Linear;
            }
            return EasingMode.CosineInOut;
        }
    }
}
