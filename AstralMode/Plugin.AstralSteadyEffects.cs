using BepInEx.Configuration;

namespace AstralMode
{
    // アストラル界 滞在中（AstralState.Astral）に画面全体へ持続的にかける残像＋波紋。
    // ワープ遷移演出(DriveWarpMixer)とは別系統。Update() から毎フレーム再駆動して維持する。
    public sealed partial class Plugin
    {
        // 一度でも適用したら true。Astral を抜けた最初のフレームで一括解除するために使う。
        private bool _astralSteadyApplied;

        // ---- Config ----
        private ConfigEntry<bool> _cfgAstralSteadyEnabled;
        private ConfigEntry<bool> _cfgAstralSteadyAfterimage;
        private ConfigEntry<bool> _cfgAstralSteadyDisplacement;
        private ConfigEntry<bool> _cfgAstralSteadyFullScreen;

        // Afterimage（強度の見た目は SimpleAfterimage 側の 色/残像アルファ倍率 でも調整可能）
        private ConfigEntry<float> _cfgAstralSteadyAfterimageIntensity;
        private ConfigEntry<float> _cfgAstralSteadyAfterimageAlphaScale;
        private ConfigEntry<int> _cfgAstralSteadyAfterimageFadeFrames;
        private ConfigEntry<int> _cfgAstralSteadyAfterimageCaptureInterval;

        // Displacement（波紋。ripple の波形は MainGameCircularDisplacement 側の Ripple 設定が使われる）
        private ConfigEntry<DisplacementMode> _cfgAstralSteadyDispMode;
        private ConfigEntry<float> _cfgAstralSteadyDispIntensity;
        private ConfigEntry<float> _cfgAstralSteadyDispOpacity;
        private ConfigEntry<float> _cfgAstralSteadyDispStrength;
        private ConfigEntry<float> _cfgAstralSteadyDispRadius;
        private ConfigEntry<float> _cfgAstralSteadyDispInnerRadius;
        private ConfigEntry<float> _cfgAstralSteadyDispCenterX;
        private ConfigEntry<float> _cfgAstralSteadyDispCenterY;

        private void SetupAstralSteadyEffectsConfig()
        {
            const string cat = "35. Astral Steady Effects";

            _cfgAstralSteadyEnabled = Bind(cat, "Enabled", true,
                "アストラル界に滞在している間、画面全体に残像＋波紋を持続表示する", order: 100);
            _cfgAstralSteadyAfterimage = Bind(cat, "Use Afterimage", true,
                "滞在中に残像を出す", order: 95);
            _cfgAstralSteadyDisplacement = Bind(cat, "Use Displacement", true,
                "滞在中に波紋(ディスプレイスメント)を出す", order: 90);
            _cfgAstralSteadyFullScreen = Bind(cat, "Full Screen (exclude UI)", true,
                "UIを除く画面全体(背景含む)をキャプチャ対象にする。OFFでキャラのみ", order: 85);

            // Afterimage
            _cfgAstralSteadyAfterimageIntensity = BindRange(cat, "Afterimage Intensity", 1.0f, 0f, 1f,
                "残像強度(0..1)", order: 80);
            _cfgAstralSteadyAfterimageAlphaScale = BindRange(cat, "Afterimage AlphaScale", 1.0f, 0f, 1f,
                "残像Alpha倍率(0..1)。最終濃度は SimpleAfterimage の 色A/残像アルファ倍率 と掛け合わせ", order: 75);
            _cfgAstralSteadyAfterimageFadeFrames = BindRangeInt(cat, "Afterimage FadeFrames", 90, 1, 300,
                "残像寿命フレーム", order: 70);
            _cfgAstralSteadyAfterimageCaptureInterval = BindRangeInt(cat, "Afterimage CaptureInterval", 1, 1, 60,
                "残像キャプチャ間隔(1=毎フレーム)", order: 65);

            // Displacement
            _cfgAstralSteadyDispMode = Bind(cat, "Displacement Mode", DisplacementMode.Ripple,
                "波紋の種類。波紋=Ripple", order: 60);
            _cfgAstralSteadyDispIntensity = BindRange(cat, "Displacement Intensity", 0.5023474f, 0f, 1f,
                "波紋の全体強度(0..1)", order: 55);
            _cfgAstralSteadyDispOpacity = BindRange(cat, "Displacement Opacity", 1.0f, 0f, 1f,
                "波紋オーバーレイの不透明度。全画面を歪んだコピーで置き換えるには1.0", order: 50);
            _cfgAstralSteadyDispStrength = BindRange(cat, "Displacement Strength", 0.02f, -0.5f, 0.5f,
                "UV変位量（MGCD単体での実測動作値）", order: 45);
            _cfgAstralSteadyDispRadius = BindRange(cat, "Displacement Radius", 1.5f, 0.001f, 2f,
                "波紋半径。画面全体に届かせるには大きめ。MGCD単体での実測動作値=2", order: 40);
            _cfgAstralSteadyDispInnerRadius = BindRange(cat, "Displacement InnerRadius", 0.0f, 0f, 2f,
                "波紋の安定内側半径", order: 35);
            _cfgAstralSteadyDispCenterX = BindRange(cat, "Displacement Center X", 0.5f, 0f, 1f,
                "波紋中心X", order: 30);
            _cfgAstralSteadyDispCenterY = BindRange(cat, "Displacement Center Y", 0.5f, 0f, 1f,
                "波紋中心Y", order: 25);
        }

        // Astral 滞在中に毎フレーム呼ぶ。
        private void DriveAstralSteadyEffects()
        {
            if (_cfgAstralSteadyEnabled == null || !_cfgAstralSteadyEnabled.Value)
            {
                if (_astralSteadyApplied) ClearAstralSteadyEffects();
                return;
            }

            bool fullScreen = _cfgAstralSteadyFullScreen.Value;
            // 各フレーム再適用するので hold は短くてよいが、フレーム落ちでも切れないよう少し余裕を持たせる。
            const float hold = 0.5f;

            if (AfterimageEnabled(_cfgAstralSteadyAfterimage))
            {
                TrySetWarpAfterimageFull(
                    _cfgAstralSteadyAfterimageIntensity.Value,
                    _cfgAstralSteadyAfterimageAlphaScale.Value,
                    _cfgAstralSteadyAfterimageFadeFrames.Value,
                    _cfgAstralSteadyAfterimageCaptureInterval.Value,
                    /*pauseBaseCapture*/ true,
                    /*baseAlphaMultiplier*/ 1f,
                    hold,
                    fullScreen);
            }
            else
            {
                TryClearWarpAfterimage();
            }

            if (DisplacementEnabled(_cfgAstralSteadyDisplacement))
            {
                TrySetCircularDisplacementFull(
                    _cfgAstralSteadyDispIntensity.Value,
                    _cfgAstralSteadyDispMode.Value.ToString(),
                    _cfgAstralSteadyDispOpacity.Value,
                    _cfgAstralSteadyDispStrength.Value,
                    _cfgAstralSteadyDispRadius.Value,
                    _cfgAstralSteadyDispInnerRadius.Value,
                    _cfgAstralSteadyDispCenterX.Value,
                    _cfgAstralSteadyDispCenterY.Value,
                    hold,
                    fullScreen);
            }
            else
            {
                TryClearCircularDisplacement();
            }

            _astralSteadyApplied = true;
        }

        private void ClearAstralSteadyEffects()
        {
            ClearExternalWarpEffects();
            _astralSteadyApplied = false;
        }
    }
}
