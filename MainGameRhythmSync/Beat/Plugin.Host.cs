using BepInEx.Configuration;
using BepInEx.Logging;

namespace MainGameBeatSyncSpeed
{
    public partial class Plugin
    {
        internal static MainGameRhythmSync.Plugin Host;
        private ConfigFile Config => Host.BeatConfig;
        private ManualLogSource Logger => Host.BeatLogger;

        internal HSceneProc Scene => _hSceneProc;
        internal bool Enabled => _cfgEnabled != null && _cfgEnabled.Value;
        internal bool VideoPlaying => _videoRoomPlaying;
        internal bool PlaybackFresh => UnityEngine.Time.unscaledTime - _lastVideoSnapshotAt <= 0.25f;
        internal void RefreshPlaybackForSync()
        {
            if (Enabled && CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom) PollVideoRoom();
            else ResetVideoRoom();
        }
        internal double VideoTimeSec => _videoRoomTimeSec;
        internal int Bpm => _cfgBpm != null ? _cfgBpm.Value : 0;
        internal string CurrentSongPath => _currentSongPathKey;
        internal float ZoneTarget => _currentZoneRawTarget;
        internal float LowTarget => _cfgLowIntensity != null ? _cfgLowIntensity.Value : 0f;
        internal float MidTarget => _cfgMidIntensity != null ? _cfgMidIntensity.Value : 0.44f;
        internal float HighTarget => _cfgHighIntensity != null ? _cfgHighIntensity.Value : 1f;
        internal void RefreshSongBpmForBridge() => RefreshSongBpmAutoLoad();
    }
}
