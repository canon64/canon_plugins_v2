using System;

namespace MainGameRhythmSync
{
    // One instance per H scene, not per actor. Contains no Unity dependency so
    // stop/resume, tempo changes and stalls can be tested deterministically.
    internal sealed class SharedRhythmClock
    {
        private long _identity;
        private bool _sampled;
        private double _time, _normalized, _intrinsic;
        public float Speed { get; private set; } = 1f;
        public double BeatError { get; private set; }

        public void Reset()
        {
            _sampled = false;
            _intrinsic = 0d;
            Speed = 1f;
            BeatError = 0d;
        }

        public float Step(long identity, double normalized, double length, double time,
            bool playing, double targetPhase, double cycleSeconds, double correctionSeconds)
        {
            if (!playing || length <= 0d || cycleSeconds <= 0d || !Finite(normalized) || !Finite(time))
            {
                Reset();
                return Speed;
            }
            double dt = time - _time;
            if (!_sampled || identity != _identity || dt <= 0d || dt > 0.5d || normalized < _normalized)
            {
                _intrinsic = 0d;
                dt = 0d;
            }
            else if (dt > 0.001d && Speed > 0.001f)
            {
                double measured = (normalized - _normalized) / (dt * Speed);
                if (Finite(measured) && measured > 0.01d && measured < 30d)
                    _intrinsic = _intrinsic <= 0d ? measured : _intrinsic * 0.8d + measured * 0.2d;
            }
            _sampled = true;
            _identity = identity;
            _time = time;
            _normalized = normalized;
            BeatError = PhaseDifference(targetPhase, normalized);
            double hz = Math.Max(0.05d, 1d / cycleSeconds + BeatError / Math.Max(0.2d, correctionSeconds));
            double intrinsic = _intrinsic > 0d ? _intrinsic : 1d / length;
            double command = Math.Max(0.1d, Math.Min(8d, hz / intrinsic));
            double maxStep = dt * 12d;
            Speed = (float)(Speed + Math.Max(-maxStep, Math.Min(maxStep, command - Speed)));
            return Speed;
        }

        internal static double PhaseDifference(double target, double actual)
        {
            double d = target - actual;
            return d - Math.Floor(d + 0.5d);
        }
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
