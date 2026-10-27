using System;
namespace MainGameVMDPlay
{
    internal static class MediaTimeline
    {
        internal static float Position(float mediaTime, float length, bool loop)
        {
            if (length <= 0) return 0;
            mediaTime = Math.Max(0, mediaTime);
            return loop ? mediaTime % length : Math.Min(mediaTime, length);
        }
    }
}
