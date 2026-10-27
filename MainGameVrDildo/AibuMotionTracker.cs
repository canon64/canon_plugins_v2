using System.Collections.Generic;

namespace MainGameVrDildo
{
    // Distances are measured along the entry-to-deep axis, in centimetres.
    internal sealed class AibuMotionTracker
    {
        private struct Sample
        {
            internal float Time;
            internal float Cm;
        }

        private readonly Queue<Sample> _samples = new Queue<Sample>();
        private float _rollingCm;
        private float _windowCm;
        private float _windowStart = -1f;

        internal float RollingCm { get { return _rollingCm; } }

        internal void Reset()
        {
            _samples.Clear();
            _rollingCm = 0f;
            _windowCm = 0f;
            _windowStart = -1f;
        }

        internal bool Add(float now, float cm, float windowSeconds, out float completedCm)
        {
            completedCm = 0f;
            if (_windowStart < 0f) _windowStart = now;
            while (_samples.Count > 0 && _samples.Peek().Time < now - windowSeconds)
                _rollingCm -= _samples.Dequeue().Cm;
            if (_rollingCm < 0f) _rollingCm = 0f;
            if (cm > 0f)
            {
                _samples.Enqueue(new Sample { Time = now, Cm = cm });
                _rollingCm += cm;
                _windowCm += cm;
            }
            if (now - _windowStart < windowSeconds) return false;
            completedCm = _windowCm;
            _windowCm = 0f;
            _windowStart = now;
            return true;
        }
    }
}
