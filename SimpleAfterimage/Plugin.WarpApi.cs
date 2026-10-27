using UnityEngine;

namespace SimpleAfterimage
{
    public sealed partial class Plugin
    {
        private bool _warpAfterimageActive;
        private float _warpAfterimageUntil;
        private float _warpAfterimageIntensity;
        private float _warpAfterimageAlphaScale = 1f;
        private int _warpAfterimageFadeFrames = -1;
        private int _warpAfterimageCaptureInterval = -1;
        private bool _warpAfterimagePauseBaseCapture;
        private float _warpAfterimageBaseAlphaMultiplier = 1f;
        // When true, the warp-slot capture covers the whole scene (minus UI) instead of only
        // the Chara layer. Driven by AstralMode for full-screen afterimage.
        private bool _warpAfterimageFullScreen;

        public static void SetWarpAfterimage(
            float intensity,
            float alphaScale,
            int fadeFrames,
            int captureInterval,
            bool pauseBaseCapture,
            float baseAlphaMultiplier,
            float holdSeconds = 0.35f)
        {
            if (Instance == null) return;
            Instance.ApplyWarpAfterimage(intensity, alphaScale, fadeFrames, captureInterval, pauseBaseCapture, baseAlphaMultiplier, holdSeconds, false);
        }

        // Full-screen capable overload. fullScreen=true captures everything except the UI layer.
        public static void SetWarpAfterimage(
            float intensity,
            float alphaScale,
            int fadeFrames,
            int captureInterval,
            bool pauseBaseCapture,
            float baseAlphaMultiplier,
            float holdSeconds,
            bool fullScreen)
        {
            if (Instance == null) return;
            Instance.ApplyWarpAfterimage(intensity, alphaScale, fadeFrames, captureInterval, pauseBaseCapture, baseAlphaMultiplier, holdSeconds, fullScreen);
        }

        public static void ClearWarpAfterimage()
        {
            if (Instance == null) return;
            Instance._warpAfterimageActive = false;
            Instance._warpAfterimageIntensity = 0f;
            Instance._warpAfterimagePauseBaseCapture = false;
            Instance._warpAfterimageBaseAlphaMultiplier = 1f;
            Instance._warpAfterimageFullScreen = false;
        }

        private void ApplyWarpAfterimage(
            float intensity,
            float alphaScale,
            int fadeFrames,
            int captureInterval,
            bool pauseBaseCapture,
            float baseAlphaMultiplier,
            float holdSeconds,
            bool fullScreen)
        {
            intensity = Mathf.Clamp01(intensity);
            if (!pauseBaseCapture && intensity <= 0.0001f)
            {
                _warpAfterimageActive = false;
                _warpAfterimageIntensity = 0f;
                _warpAfterimagePauseBaseCapture = false;
                _warpAfterimageBaseAlphaMultiplier = 1f;
                _warpAfterimageFullScreen = false;
                return;
            }

            _warpAfterimageFullScreen = fullScreen;
            _warpAfterimageActive = true;
            _warpAfterimageUntil = Time.unscaledTime + Mathf.Max(0.02f, holdSeconds);
            _warpAfterimageIntensity = intensity;
            _warpAfterimageAlphaScale = Mathf.Clamp01(alphaScale);
            _warpAfterimageFadeFrames = fadeFrames > 0 ? Mathf.Clamp(fadeFrames, 1, 300) : -1;
            _warpAfterimageCaptureInterval = captureInterval > 0 ? Mathf.Clamp(captureInterval, 1, 60) : -1;
            _warpAfterimagePauseBaseCapture = pauseBaseCapture;
            _warpAfterimageBaseAlphaMultiplier = Mathf.Clamp01(baseAlphaMultiplier);
        }

        private bool IsWarpAfterimageActive(bool expire = true)
        {
            if (!_warpAfterimageActive) return false;
            if (Time.unscaledTime <= _warpAfterimageUntil) return true;
            if (expire)
            {
                _warpAfterimageActive = false;
                _warpAfterimageIntensity = 0f;
                _warpAfterimagePauseBaseCapture = false;
                _warpAfterimageBaseAlphaMultiplier = 1f;
            }
            return false;
        }

        private bool IsBaseAfterimageCapturePausedByWarp()
        {
            return IsWarpAfterimageActive() && _warpAfterimagePauseBaseCapture;
        }

        private bool ShouldCaptureWarpAfterimage()
        {
            return IsWarpAfterimageActive() && _warpAfterimageIntensity > 0.0001f;
        }

        private bool IsWarpFullSceneActive()
        {
            return IsWarpAfterimageActive() && _warpAfterimageFullScreen;
        }

        // AstralMode 等の外部残像はユーザーの「有効」トグルと別管理。
        // 外部 warp 残像が動いている間は、プラグイン本体が無効でもランタイムを回す。
        private bool IsEffectivelyEnabled()
        {
            if (_cfgEnabled != null && _cfgEnabled.Value) return true;
            return IsWarpAfterimageActive(false);
        }

        private int GetWarpAfterimageFadeFrames()
        {
            if (IsWarpAfterimageActive() && _warpAfterimageFadeFrames > 0) return _warpAfterimageFadeFrames;
            return GetBaseFadeFrames();
        }

        private int GetWarpAfterimageCaptureInterval()
        {
            if (IsWarpAfterimageActive() && _warpAfterimageCaptureInterval > 0) return _warpAfterimageCaptureInterval;
            return _cfgCaptureInterval != null ? Mathf.Max(1, _cfgCaptureInterval.Value) : 1;
        }

        private float GetWarpAfterimageAlphaScale()
        {
            float baseScale = _cfgAlphaScale != null ? Mathf.Clamp01(_cfgAlphaScale.Value) : 1f;
            if (IsWarpAfterimageActive()) return Mathf.Clamp01(baseScale * _warpAfterimageAlphaScale * _warpAfterimageIntensity);
            return baseScale;
        }

        private float GetBaseAfterimageAlphaMultiplierByWarp()
        {
            return IsWarpAfterimageActive() ? _warpAfterimageBaseAlphaMultiplier : 1f;
        }

        private void EnsureWarpAfterimageSlotMetadata()
        {
            if (_slots == null) return;
            int n = _slots.Length;
            if (_slotFadeFrames == null || _slotFadeFrames.Length != n) _slotFadeFrames = new int[n];
            if (_slotAlphaScale == null || _slotAlphaScale.Length != n) _slotAlphaScale = new float[n];
            if (_slotExternal == null || _slotExternal.Length != n) _slotExternal = new bool[n];
            int fade = GetBaseFadeFrames();
            float alpha = _cfgAlphaScale != null ? Mathf.Clamp01(_cfgAlphaScale.Value) : 1f;
            for (int i = 0; i < n; i++)
            {
                if (_slotFadeFrames[i] <= 0) _slotFadeFrames[i] = fade;
                if (_slotAlphaScale[i] <= 0f) _slotAlphaScale[i] = alpha;
            }
        }
    }
}
