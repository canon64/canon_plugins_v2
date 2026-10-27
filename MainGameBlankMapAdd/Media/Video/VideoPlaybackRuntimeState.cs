using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace MainGameBlankMapAdd.Media.Video
{
    internal sealed class PendingVideoStart
    {
        internal VideoPlayer Player;
        internal string SurfaceName;
        internal string VideoPath;
        internal bool PlayWhenReady;
    }

    internal sealed class VideoBinding
    {
        internal VideoPlayer Player;
        internal RenderTexture Texture;
        internal readonly List<Material> Materials = new List<Material>();
        internal VideoPlayer.EventHandler PreparedHandler;
        internal VideoPlayer.EventHandler LoopPointHandler;
        internal VideoPlayer.ErrorEventHandler ErrorHandler;
        internal Guid RoomGeneration;
    }

    internal sealed class VideoPlaybackRuntimeState
    {
        internal Dictionary<string, VideoBinding> Bindings { get; } =
            new Dictionary<string, VideoBinding>(StringComparer.OrdinalIgnoreCase);
        internal List<PendingVideoStart> PendingStarts { get; } = new List<PendingVideoStart>();
        internal VideoPlayer MainPlayer { get; set; }
        internal AudioSource RoomAudioSource { get; set; }
        internal float NextPendingLogTime { get; set; }
        internal float NextBridgeSnapshotLogTime { get; set; }
        internal float NextBridgeSnapshotMissingLogTime { get; set; }
    }
}
