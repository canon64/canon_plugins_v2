namespace MainGameVMDPlay {
    public sealed partial class Plugin {
        // True means VMD owns the media channel, including preparation/pause.
        // H animation sync is enabled only for an independent dancer.
        // Read-only media status for effects; main-character VMD is included.
        public static bool TryGetAfterimageRhythmSource(out string path, out double time, out bool playing) {
            path = null; time = 0; playing = false;
            var p = Instance;
            if (p == null || !p._vmdOwnsCharacter || p._astral != null) return false;
            path = p._musicLoading ? null : p._musicPath;
            if (p._musicLoading || p._loadRoutine != null || !p.TryMediaTime(out float cursor)) return true;
            time = cursor;
            bool audible = p._musicVideo != null && p._musicVideo.isPrepared ? p._musicVideo.isPlaying : p._music != null && p._music.isPlaying;
            playing = p._state == PluginState.Playing && audible && !p._mediaEnded && !p._seekDragging && !p._seekRequested && !p._videoSeeks.Pending;
            return true;
        }
        public static bool TryGetRhythmSource(out string path, out double time, out bool playing) {
            path = null; time = 0; playing = false;
            var p = Instance;
            if (p == null || !p._vmdOwnsCharacter) return false;
            if (p._astral != null) return true;
            path = p._musicLoading ? null : p._musicPath;
            if (p._musicLoading || p._loadRoutine != null || !p.TryMediaTime(out float cursor)) return true;
            time = cursor;
            bool audible = p._musicVideo != null && p._musicVideo.isPrepared ? p._musicVideo.isPlaying : p._music != null && p._music.isPlaying;
            playing = p.IsIndependentTarget && p._state == PluginState.Playing && audible && !p._mediaEnded && !p._seekDragging && !p._seekRequested && !p._videoSeeks.Pending;
            return true;
        }
    }
}
