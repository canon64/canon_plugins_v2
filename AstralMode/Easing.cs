using UnityEngine;

namespace AstralMode
{
    public enum EasingType
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut,
        EaseInCubic,
        EaseOutCubic,
        EaseInQuint,
        EaseOutQuint
    }

    internal static class Easing
    {
        internal static float Apply(EasingType type, float t)
        {
            t = Mathf.Clamp01(t);
            switch (type)
            {
                case EasingType.Linear: return t;
                case EasingType.EaseIn: return t * t;
                case EasingType.EaseOut: return 1f - (1f - t) * (1f - t);
                case EasingType.EaseInOut:
                    return t < 0.5f
                        ? 2f * t * t
                        : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
                case EasingType.EaseInCubic: return t * t * t;
                case EasingType.EaseOutCubic: return 1f - Mathf.Pow(1f - t, 3f);
                case EasingType.EaseInQuint: return t * t * t * t * t;
                case EasingType.EaseOutQuint: return 1f - Mathf.Pow(1f - t, 5f);
                default: return t;
            }
        }

        // 0→1 区間 [start, end] にマッピングして適用
        internal static float Window(EasingType type, float t01, float start, float end)
        {
            if (end <= start) return t01 >= start ? 1f : 0f;
            float local = Mathf.Clamp01((t01 - start) / (end - start));
            return Apply(type, local);
        }
    }
}
