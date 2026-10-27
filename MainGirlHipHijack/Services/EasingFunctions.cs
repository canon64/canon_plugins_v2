using System;

namespace MainGirlHipHijack.Services
{
    internal static class EasingFunctions
    {
        internal static float EvaluateHeadOffset(float value, int mode)
        {
            float t = Clamp01(value);
            switch (mode)
            {
                case 1: return t * t;
                case 2: return t * (2f - t);
                case 3: return t < 0.5f
                    ? 2f * t * t
                    : -1f + (4f - 2f * t) * t;
                default: return t;
            }
        }

        internal static float EvaluatePoseTransition(float value, int easing)
        {
            float t = Clamp01(value);
            switch (easing)
            {
                case 0:
                    return t;
                case 2:
                    float inverse = 1f - t;
                    return 1f - (inverse * inverse * inverse);
                case 1:
                default:
                    return t * t * (3f - (2f * t));
            }
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f) return 0f;
            if (value >= 1f) return 1f;
            return value;
        }
    }
}
