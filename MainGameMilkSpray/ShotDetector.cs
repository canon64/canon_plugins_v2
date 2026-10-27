using System;

namespace MainGameMilkSpray
{
    internal enum ShotTiming { DepthPeak, AnimationPhase }

    internal sealed class ShotDetector
    {
        private bool _initialized, _approaching;
        private double _lastFire = double.NegativeInfinity;
        private float _lastPhase, _far, _near;
        private ShotTiming _mode;
        private int _targetPercent;

        public void Reset()
        {
            _initialized = _approaching = false;
            _lastFire = double.NegativeInfinity;
        }

        public bool Sample(ShotTiming mode, int phasePercent, float time, float distance, double clock, float minStroke, float interval)
        {
            if (!_initialized || _mode != mode || _targetPercent != phasePercent)
            {
                _initialized = true; _mode = mode; _targetPercent = phasePercent;
                _lastPhase = time; _far = _near = distance; _approaching = false;
                return false;
            }
            bool fire = false;
            if (mode == ShotTiming.AnimationPhase)
            {
                float target = phasePercent * .01f;
                if (time >= _lastPhase)
                    fire = Math.Floor(time - target) > Math.Floor(_lastPhase - target);
                else
                {
                    float before = _lastPhase - (float)Math.Floor(_lastPhase), after = time - (float)Math.Floor(time);
                    // Only a genuine 1 -> 0 wrap is accepted, not a seek backwards within a loop.
                    fire = before > .75f && after < .25f && (target > before || target <= after);
                }
            }
            else if (!_approaching)
            {
                _far = Math.Max(_far, distance);
                if (_far - distance >= minStroke) { _approaching = true; _near = distance; }
            }
            else
            {
                _near = Math.Min(_near, distance);
                // Hysteresis operates on accumulated travel, not a frame-sized delta.
                if (distance - _near >= .0005f)
                { fire = _far - _near >= minStroke; _approaching = false; _far = distance; }
            }
            _lastPhase = time;
            if (!fire || clock - _lastFire < interval) return false;
            _lastFire = clock;
            return true;
        }
    }
}
