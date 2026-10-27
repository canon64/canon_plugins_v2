using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameBlankMapAdd.Media.Images
{
    internal sealed class SlideshowRuntimeState
    {
        internal List<GameObject> Quads { get; } = new List<GameObject>();
        internal List<GameObject> TransitionQuads { get; } = new List<GameObject>();
        internal Material Material { get; set; }
        internal Material TransitionMaterial { get; set; }
        internal Texture2D Texture { get; set; }
        internal Texture2D TransitionTexture { get; set; }
        internal string TexturePath { get; set; } = string.Empty;
        internal DateTime TextureWriteTimeUtc { get; set; } = DateTime.MinValue;
        internal string TransitionPath { get; set; } = string.Empty;
        internal DateTime TransitionWriteTimeUtc { get; set; } = DateTime.MinValue;
        internal string PendingPath { get; set; } = string.Empty;
        internal string[] Files { get; set; } = Array.Empty<string>();
        internal int Index { get; set; } = -1;
        internal int TransitionIndex { get; set; } = -1;
        internal string FolderResolved { get; set; } = string.Empty;
        internal float NextScanTime { get; set; }
        internal float NextSlideTime { get; set; }
        internal bool TransitionActive { get; set; }
        internal float TransitionStartTime { get; set; }
        internal float TransitionDuration { get; set; }
        internal float DisplayStartTime { get; set; } = -1f;
        internal float TransitionDisplayStartTime { get; set; } = -1f;
        internal string TransitionRuntimeMode { get; set; } = "CrossFade";
        internal bool LoggedMissingFolder { get; set; }
        internal bool LoggedNoImages { get; set; }
        internal bool LastEnabledState { get; set; }
        internal string LastBuiltSurface { get; set; } = string.Empty;
        internal string LastBuiltMode { get; set; } = string.Empty;
        internal int LastBuiltChildCount { get; set; } = -1;
        internal GameObject LastBuiltRoot { get; set; }
        internal string FolderInput { get; set; } = string.Empty;
        internal string SecondsInput { get; set; } = string.Empty;
        internal string ScanInput { get; set; } = string.Empty;
        internal string OpacityInput { get; set; } = string.Empty;
        internal string TransitionSecondsInput { get; set; } = string.Empty;
    }
}
