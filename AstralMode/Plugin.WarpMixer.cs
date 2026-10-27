using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private struct EffectWindow
        {
            public float StartT;
            public float EndT;
            public EasingType Easing;

            public EffectWindow(float startT, float endT, EasingType easing)
            {
                StartT = startT;
                EndT = endT;
                Easing = easing;
            }
        }

        private const int WarpMixerLogSteps = 10;
        private int _lastBloomMixerLogStep = -999;
        private int _lastWhiteCanvasMixerLogStep = -999;
        private int _lastAfterimageMixerLogStep = -999;
        private int _lastDisplacementMixerLogStep = -999;

        private float EvalWindow(float t01, EffectWindow window)
        {
            return Easing.Window(window.Easing, t01, window.StartT, window.EndT);
        }

        private float EvalFloat(float t01, EffectWindow window, float startValue, float endValue)
        {
            return Mathf.Lerp(startValue, endValue, EvalWindow(t01, window));
        }

        private int EvalInt(float t01, EffectWindow window, int startValue, int endValue)
        {
            return Mathf.RoundToInt(EvalFloat(t01, window, startValue, endValue));
        }

        private bool ShouldLogWarpMixer(ref int lastStep, float t01)
        {
            int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(t01) * WarpMixerLogSteps), 0, WarpMixerLogSteps);
            if (step == lastStep) return false;
            lastStep = step;
            return true;
        }

        private void DriveWarpMixer(float t01)
        {
            DriveBloomMixer(t01);
            DriveWhiteCanvasMixer(t01);
            // 帰還warp(ReturnWarping)中で steady(残像＋波紋)が有効なときは、warp mixer 側で
            // 残像/波紋を上書きしない。アストラルの steady 効果を白飛びするまでそのまま維持するため
            // （Update 側が ReturnWarping 中も DriveAstralSteadyEffects を回す）。
            bool keepSteadyDuringReturn = _state == AstralState.ReturnWarping
                && _cfgAstralSteadyEnabled != null && _cfgAstralSteadyEnabled.Value;
            if (!keepSteadyDuringReturn)
            {
                DriveAfterimageMixer(t01);
                DriveDisplacementMixer(t01);
            }
            DriveFovMixer(t01);
        }

        private void DriveBloomMixer(float t01)
        {
            if (_cfgUseBloom != null && !_cfgUseBloom.Value)
            {
                SetBloomIntensity(0f);
                return;
            }

            var window = new EffectWindow(_cfgBloomStartT.Value, _cfgBloomEndT.Value, _cfgBloomEasing.Value);
            float windowT = EvalWindow(t01, window);
            float intensity = Mathf.Lerp(_cfgBloomStartIntensity.Value, _cfgBloomEndIntensity.Value, windowT);
            float max = Mathf.Max(0.0001f, _cfgBloomMaxIntensity.Value);
            float intensity01 = Mathf.Clamp01(intensity / max);
            SetBloomIntensity(intensity01);

        }

        private void DriveWhiteCanvasMixer(float t01)
        {
            if (_cfgUseWhiteCanvas != null && !_cfgUseWhiteCanvas.Value)
            {
                SetCanvasAlpha(0f);
                return;
            }

            var window = new EffectWindow(_cfgWhiteCanvasStartT.Value, _cfgWhiteCanvasEndT.Value, _cfgWhiteCanvasEasing.Value);
            float windowT = EvalWindow(t01, window);
            float alpha = Mathf.Lerp(_cfgWhiteCanvasStartAlpha.Value, _cfgWhiteCanvasEndAlpha.Value, windowT);
            SetCanvasAlpha(alpha);

        }

        private void DriveAfterimageMixer(float t01)
        {
            if (!AfterimageEnabled(_cfgUseAfterimage))
            {
                TryClearWarpAfterimage();
                return;
            }

            var window = new EffectWindow(_cfgAfterimageStartT.Value, _cfgAfterimageEndT.Value, _cfgAfterimageEasing.Value);
            float intensity = EvalFloat(t01, window, _cfgAfterimageStartIntensity.Value, _cfgAfterimageEndIntensity.Value);
            float alphaScale = EvalFloat(t01, window, _cfgAfterimageStartAlphaScale.Value, _cfgAfterimageEndAlphaScale.Value);
            int fadeFrames = EvalInt(t01, window, _cfgAfterimageStartFadeFrames.Value, _cfgAfterimageEndFadeFrames.Value);
            int captureInterval = EvalInt(t01, window, _cfgAfterimageStartCaptureInterval.Value, _cfgAfterimageEndCaptureInterval.Value);

            bool pauseBase = _cfgAfterimageExistingMode.Value == AfterimageExistingMode.StopAddingAndDecay
                || _cfgAfterimageExistingMode.Value == AfterimageExistingMode.FadeExistingFaster;

            float baseAlpha = 1f;
            if (_cfgAfterimageExistingMode.Value == AfterimageExistingMode.ClearImmediately)
            {
                baseAlpha = 0f;
                pauseBase = true;
            }
            else if (_cfgAfterimageExistingMode.Value == AfterimageExistingMode.FadeExistingFaster
                || _cfgAfterimageExistingMode.Value == AfterimageExistingMode.StopAddingAndDecay)
            {
                var decayWindow = new EffectWindow(
                    _cfgAfterimageExistingDecayStartT.Value,
                    _cfgAfterimageExistingDecayEndT.Value,
                    _cfgAfterimageEasing.Value);
                baseAlpha = EvalFloat(t01, decayWindow, _cfgAfterimageExistingStartAlpha.Value, _cfgAfterimageExistingEndAlpha.Value);
            }

            float outIntensity = Mathf.Clamp01(intensity);
            float outAlphaScale = Mathf.Clamp01(alphaScale);
            int outFadeFrames = Mathf.Clamp(fadeFrames, 1, 300);
            int outCaptureInterval = Mathf.Clamp(captureInterval, 1, 60);
            float outBaseAlpha = Mathf.Clamp01(baseAlpha);

            bool ok = TrySetWarpAfterimage(
                outIntensity,
                outAlphaScale,
                outFadeFrames,
                outCaptureInterval,
                pauseBase,
                outBaseAlpha,
                0.35f);

        }

        private void DriveDisplacementMixer(float t01)
        {
            if (!DisplacementEnabled(_cfgUseDisplacement))
            {
                TryClearCircularDisplacement();
                return;
            }

            var window = new EffectWindow(_cfgRadialBlurStartT.Value, _cfgRadialBlurEndT.Value, _cfgRadialBlurEasing.Value);
            float intensity = EvalFloat(t01, window, _cfgDisplacementStartIntensity.Value, _cfgDisplacementEndIntensity.Value);
            float opacity = EvalFloat(t01, window, _cfgDisplacementStartOpacity.Value, _cfgDisplacementEndOpacity.Value);
            float strength = EvalFloat(t01, window, _cfgDisplacementStartStrength.Value, _cfgDisplacementEndStrength.Value);
            float radius = EvalFloat(t01, window, _cfgDisplacementStartRadius.Value, _cfgDisplacementEndRadius.Value);
            float inner = EvalFloat(t01, window, _cfgDisplacementStartInnerRadius.Value, _cfgDisplacementEndInnerRadius.Value);

            float outIntensity = Mathf.Clamp01(intensity);
            float outOpacity = Mathf.Clamp01(opacity);
            float outRadius = Mathf.Max(0.001f, radius);
            float outInner = Mathf.Max(0f, inner);
            float outCenterX = Mathf.Clamp01(_cfgDisplacementCenterX.Value);
            float outCenterY = Mathf.Clamp01(_cfgDisplacementCenterY.Value);
            string mode = _cfgDisplacementMode.Value.ToString();

            bool ok = TrySetCircularDisplacement(
                outIntensity,
                mode,
                outOpacity,
                strength,
                outRadius,
                outInner,
                outCenterX,
                outCenterY,
                0.35f);

        }
    }
}
