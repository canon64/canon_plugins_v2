using System.Collections.Generic;
using UnityEngine;

namespace MainGameBlankMapAdd.Overlay
{
    internal enum OverlayBeatZone { Unknown, Low, Mid, High }

    internal sealed class OverlayRuntimeState
    {
        internal List<GameObject> Quads { get; } = new List<GameObject>();
        internal Material Material { get; set; }
        internal RenderTexture CachedRenderTexture { get; set; }
        internal int CachedTextureWidth { get; set; }
        internal int CachedTextureHeight { get; set; }
        internal bool LoggedMissingTexture { get; set; }
        internal string LastBuiltSurface { get; set; } = string.Empty;
        internal string LastBuiltMode { get; set; } = string.Empty;
        internal int LastBuiltChildCount { get; set; } = -1;
        internal GameObject LastBuiltRoot { get; set; }
        internal bool LastEnabledState { get; set; }
        internal OverlayBeatZone LastBeatZone { get; set; } = OverlayBeatZone.Unknown;
        internal float BeatSmoothedZoneSpeed01 { get; set; } = -1f;
        internal float BeatPhase01 { get; set; }
        internal float BeatLastBpmHz { get; set; }
        internal float LastResolvedOpacity { get; set; } = 0.5f;
    }
}
