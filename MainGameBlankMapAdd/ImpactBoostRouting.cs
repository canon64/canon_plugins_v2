using System;

namespace MainGameBlankMapAdd
{
    internal static class ImpactBoostRouting
    {
        internal static void Apply(bool enabled, bool vr, Action<bool> normal, Action<bool> virtualReality)
        {
            // Disable the other detector first, including during mode changes.
            if (vr)
            {
                normal(false);
                virtualReality(enabled);
            }
            else
            {
                virtualReality(false);
                normal(enabled);
            }
        }
    }
}
