using MainGameBlankMapAdd.Integrations;
using UnityEngine;
using UnityEngine.Video;

namespace MainGameBlankMapAdd.UI
{
    internal sealed class PlaybackUiState
    {
        internal bool SeekDragging { get; set; }
        internal bool VolumeDragging { get; set; }
        internal bool GainDragging { get; set; }
        internal bool ReverbDropdownOpen { get; set; }
        internal Vector2 ReverbDropdownScroll { get; set; } = Vector2.zero;
        internal Rect ReverbDropdownButtonRect { get; set; } = Rect.zero;
        internal Rect ReverbDropdownListRect { get; set; } = Rect.zero;
        internal bool HiddenByUser { get; set; }
        internal float SeekNormalized { get; set; }
        internal float RoomScale { get; set; } = 1f;
        internal bool RoomControlsExpanded { get; set; }
        internal bool FolderDropdownOpen { get; set; }
        internal Vector2 FolderDropdownScroll { get; set; } = Vector2.zero;
        internal bool VideoDropdownOpen { get; set; }
        internal Vector2 VideoDropdownScroll { get; set; } = Vector2.zero;
        internal bool SlideshowFolderDropdownOpen { get; set; }
        internal Vector2 SlideshowFolderDropdownScroll { get; set; } = Vector2.zero;
        internal string OverlayOpacityInput { get; set; } = string.Empty;

        internal bool DeferredPlayRequested { get; set; }
        internal float DeferredPlayDeadline { get; set; }
        internal VideoPlayer DeferredPlayPlayer { get; set; }
        internal bool BeatSnapshotCacheReady { get; set; }
        internal float NextBeatSnapshotPollTime { get; set; }
        internal bool BeatSnapshotAvailable { get; set; }
        internal IntegrationRegistry.BeatSyncSnapshot BeatSnapshot { get; set; }
        internal bool ExternalSnapshotCacheReady { get; set; }
        internal float NextExternalSnapshotPollTime { get; set; }
        internal bool UnifiedUiAvailable { get; set; }
        internal bool UnifiedUiVisible { get; set; }
        internal bool AfterimageAvailable { get; set; }
        internal bool AfterimageEnabled { get; set; }
        internal bool VrScreenEffectAvailable { get; set; }
        internal bool VrScreenEffectEnabled { get; set; }
        internal bool VrScreenEffectUiAvailable { get; set; }
        internal bool VrScreenEffectUiVisible { get; set; }
        internal bool DollModeAvailable { get; set; }
        internal bool DollModeEnabled { get; set; }
        internal bool PoseAvailable { get; set; }
        internal bool PoseEnabled { get; set; }
        internal bool ScenarioTextAvailable { get; set; }
        internal bool ScenarioTextAutoSendEnabled { get; set; }
    }
}
