using System;
using UnityEngine;

namespace MainGameVrHipIkVoiceImpactBoost
{
    internal sealed class HipIkMotionDetector
    {
        private readonly PluginSettings _settings;
        private readonly PluginFileLogger _logger;
        private readonly HipIkTrackingPositionSource _positionSource;
        private readonly Action<float, float, string> _onTrigger;
        private bool _hasLastPosition;
        private bool _wasAboveThreshold;
        private Vector3 _lastHipPosition;
        private float _smoothedHeadwardSpeed;
        private string _lastResetReason;

        internal HipIkMotionDetector(
            PluginSettings settings,
            PluginFileLogger logger,
            HipIkTrackingPositionSource positionSource,
            Action<float, float, string> onTrigger)
        {
            _settings = settings;
            _logger = logger;
            _positionSource = positionSource;
            _onTrigger = onTrigger;
        }

        internal void Update(float unscaledDeltaTime)
        {
            if (!_positionSource.TryGetHip(
                    out Vector3 hipPosition,
                    out bool bodyIkRunning,
                    out string hipSource))
            {
                Reset("hip_unavailable");
                return;
            }

            if (_settings.RequireBodyIkRunning && !bodyIkRunning)
            {
                Reset("body_ik_not_running");
                return;
            }

            if (!_positionSource.TryGetHead(out Vector3 headPosition, out string headSource))
            {
                Reset("head_unavailable");
                return;
            }

            Vector3 hipToHead = headPosition - hipPosition;
            if (hipToHead.sqrMagnitude < 1e-6f)
            {
                Reset("hip_head_overlap");
                return;
            }
            Vector3 headDir = hipToHead.normalized;

            float dt = Mathf.Max(0.0001f, unscaledDeltaTime);
            if (!_hasLastPosition)
            {
                _lastHipPosition = hipPosition;
                _hasLastPosition = true;
                _lastResetReason = null;
                return;
            }

            // 頭方向に進んだ距離(マイナスなら足方向=戻り)
            Vector3 hipDelta = hipPosition - _lastHipPosition;
            float headwardDelta = Vector3.Dot(hipDelta, headDir);
            float rawHeadwardSpeed = headwardDelta / dt;

            float alpha = _settings.SmoothingFactor;
            _smoothedHeadwardSpeed = alpha <= 0f
                ? rawHeadwardSpeed
                : Mathf.Lerp(_smoothedHeadwardSpeed, rawHeadwardSpeed, alpha);

            _lastHipPosition = hipPosition;

            bool above = headwardDelta >= _settings.MinDeltaMeters
                && _smoothedHeadwardSpeed >= _settings.VelocityThresholdMps;

            if (above && !_wasAboveThreshold)
            {
                string source = (hipSource ?? "unknown") + "/" + (headSource ?? "unknown");
                _onTrigger(_smoothedHeadwardSpeed, headwardDelta, source);
            }

            _wasAboveThreshold = above;
        }

        internal void Reset(string reason)
        {
            _hasLastPosition = false;
            _wasAboveThreshold = false;
            _smoothedHeadwardSpeed = 0f;

            if (_settings.VerboseLog && reason != _lastResetReason)
            {
                _logger.LogInfo("detector reset reason=" + reason);
            }

            _lastResetReason = reason;
        }
    }
}
