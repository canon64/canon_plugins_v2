using UnityEngine;

namespace SimpleAfterimage
{
    public sealed partial class Plugin
    {
        // External runtime control for AstralMode and other plugins.
        // This does not write ConfigEntry values and does not save config.json.
        private bool _externalAfterimageActive;
        private float _externalAfterimageUntil;
        private float _externalAfterimageIntensity;
        private float _externalAfterimageAlphaScale = 1f;
        private int _externalAfterimageFadeFrames = -1;
        private int _externalAfterimageCaptureInterval = -1;
        private bool _externalAfterimageClearOnStart;
        private bool _externalAfterimageClearConsumed;

        public static bool IsAvailable => Instance != null;

        public static void SetExternalAfterimage(float intensity)
        {
            SetExternalAfterimage(intensity, 1f, -1, -1, false, 0.35f);
        }

        public static void SetExternalAfterimage(
            float intensity,
            float alphaScale,
            int fadeFrames,
            int captureInterval,
            bool clearExisting,
            float holdSeconds = 0.35f)
        {
            if (Instance == null) return;
            Instance.ApplyExternalAfterimage(intensity, alphaScale, fadeFrames, captureInterval, clearExisting, holdSeconds);
        }

        public static void ClearExternalAfterimage(bool clearSlots = false)
        {
            if (Instance == null) return;
            Instance._externalAfterimageActive = false;
            Instance._externalAfterimageIntensity = 0f;
            Instance._externalAfterimageClearOnStart = false;
            Instance._externalAfterimageClearConsumed = false;
            if (clearSlots) Instance.ClearAfterimageSlots();
        }

        private void ApplyExternalAfterimage(
            float intensity,
            float alphaScale,
            int fadeFrames,
            int captureInterval,
            bool clearExisting,
            float holdSeconds)
        {
            intensity = Mathf.Clamp01(intensity);
            if (intensity <= 0.0001f)
            {
                _externalAfterimageActive = false;
                _externalAfterimageIntensity = 0f;
                return;
            }

            bool wasInactive = !IsExternalAfterimageActiveNow(false);
            _externalAfterimageActive = true;
            _externalAfterimageUntil = Time.unscaledTime + Mathf.Max(0.02f, holdSeconds);
            _externalAfterimageIntensity = intensity;
            _externalAfterimageAlphaScale = Mathf.Clamp01(alphaScale);
            _externalAfterimageFadeFrames = fadeFrames > 0 ? Mathf.Clamp(fadeFrames, 1, 300) : -1;
            _externalAfterimageCaptureInterval = captureInterval > 0 ? Mathf.Clamp(captureInterval, 1, 60) : -1;

            if (clearExisting && wasInactive)
            {
                _externalAfterimageClearOnStart = true;
                _externalAfterimageClearConsumed = false;
            }
        }

        private bool IsExternalAfterimageActiveNow(bool expire = true)
        {
            if (!_externalAfterimageActive) return false;
            if (Time.unscaledTime <= _externalAfterimageUntil && _externalAfterimageIntensity > 0.0001f)
                return true;

            if (expire)
            {
                _externalAfterimageActive = false;
                _externalAfterimageIntensity = 0f;
                _externalAfterimageClearOnStart = false;
                _externalAfterimageClearConsumed = false;
            }
            return false;
        }

        private int GetEffectiveFadeFrames()
        {
            if (IsExternalAfterimageActiveNow() && _externalAfterimageFadeFrames > 0)
                return _externalAfterimageFadeFrames;
            return GetBaseFadeFrames();
        }

        private int GetBaseFadeFrames()
        {
            if (_runtimeBeatFadeFrames > 0) return _runtimeBeatFadeFrames;
            return _cfgFadeFrames != null ? Mathf.Max(1, _cfgFadeFrames.Value) : 1;
        }

        private int GetEffectiveCaptureInterval()
        {
            if (IsExternalAfterimageActiveNow() && _externalAfterimageCaptureInterval > 0)
                return _externalAfterimageCaptureInterval;
            return _cfgCaptureInterval != null ? _cfgCaptureInterval.Value : 1;
        }

        private float GetEffectiveAlphaScale()
        {
            float baseScale = _cfgAlphaScale != null ? Mathf.Clamp01(_cfgAlphaScale.Value) : 1f;
            if (IsExternalAfterimageActiveNow())
                return Mathf.Clamp01(baseScale * _externalAfterimageAlphaScale * _externalAfterimageIntensity);
            return baseScale;
        }

        private void ConsumeExternalClearRequest()
        {
            if (!_externalAfterimageClearOnStart || _externalAfterimageClearConsumed) return;
            _externalAfterimageClearConsumed = true;
            ClearAfterimageSlots();
        }

        private void ClearAfterimageSlots()
        {
            _timedSlots.Clear();
            _nextVmdCapture = 0;
            if (_life != null)
            {
                for (int i = 0; i < _life.Length; i++)
                    _life[i] = 0;
            }
            _drawCount = 0;
            _writeIndex = 0;
            _frameCounter = 0;
            ClearVrStereoSlots();
        }
    }
}
