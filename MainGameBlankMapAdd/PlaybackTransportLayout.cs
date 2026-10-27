using System;
namespace MainGameBlankMapAdd
{
    internal static class PlaybackTransportLayout
    {
        internal static bool WrapTransport(float width) => width < 980;
        internal static float ListOffset(float width) => WrapTransport(width) ? 52 : 26;
        internal static bool SeparateAudio(float width) => false;
        internal static float TopControlsHeight(float width) => width >= 1270f ? 24f : (width >= 840f ? 48f : 72f);
        internal static bool WrapPresetControls(float width) => width < 840f;
#if SHOW_EXPERIMENTAL_VRFX_BAR
        internal const float ToggleContentWidth = 952f;
#else
        internal const float ToggleContentWidth = 820f;
#endif
        internal static float ToggleRowHeight(float width) => ToggleContentWidth > width - 12f ? 38f : 20f;
        internal static float ExtraHeight(float width, bool expanded) => ListOffset(width) + 22 + (SeparateAudio(width) ? 26 : 0) + (expanded ? 26 : 0);
        internal static float MinimumHeight(float width, bool expanded) =>
            (expanded ? 522 : 8) + TopControlsHeight(width) + ListOffset(width) + 26 + ToggleRowHeight(width) + 4;
        internal static float ChoiceWidth(float width) => Math.Max(1, (width - 18) / 4);
        internal static float SeekWidth(float start, float end, bool handy) => Math.Max(40, end - start - (handy ? 68 : 0));
    }
}
