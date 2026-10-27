using HarmonyLib;
using MMD4U;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private MTSequenceSet _motionClockSequence;
        private bool _mediaClockPatched, _mediaEnded;
        private readonly LatestSeek _videoSeeks = new LatestSeek();
        private int _mediaClockFrame = -1;
        private float _mediaClockTime;
        private static readonly AccessTools.FieldRef<MTSequenceSet, bool> SequenceLoop = AccessTools.FieldRefAccess<MTSequenceSet, bool>("_loop");

        private void PrepareMediaClock(MTSequenceSet sequence)
        {
            _motionClockSequence = sequence;
            if (_mediaClockPatched) return;
            _trialHarmony.Patch(AccessTools.Method(typeof(MTSequenceSet), "UpdateInternal"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(UpdateFromMediaClock)));
            _mediaClockPatched = true;
        }

        // Read the actual player cursor once per frame, shared by body and camera.
        // Never integrate deltaTime, and never seek the audio during ordinary playback.
        private bool TryMediaTime(out float time)
        {
            time = 0;
            if (_seekDragging || _seekRequested) { time = _seekDragValue; return true; }
            if (_videoClockHeld && _musicVideo != null) { time = _videoHeldTime; return true; }
            if (_musicLoading) return false;
            bool video = _musicVideo != null && _musicVideo.isPrepared;
            if (!video && (_music == null || _music.clip == null)) return false;
            if (_mediaClockFrame != Time.frameCount) {
                if (video) _mediaClockTime = _videoSeeks.Pending ? _videoSeeks.Target : (float)_musicVideo.time;
                else {
                    _mediaClockTime = (float)_music.timeSamples / _music.clip.frequency;
                    // AudioSource resets its cursor to zero on natural completion.
                    if (_state == PluginState.Playing && !_music.isPlaying && !_music.loop && _music.pitch > 0)
                        _mediaEnded = true;
                }
                if (_mediaEnded) _mediaClockTime = VmdBridgeSoundGetLength();
                _mediaClockFrame = Time.frameCount;
            }
            time = _mediaClockTime;
            return true;
        }

        private static bool UpdateFromMediaClock(MTSequenceSet __instance)
        {
            var p = Instance;
            if (p?._astral != null && (ReferenceEquals(__instance,p._motionClockSequence) || ReferenceEquals(__instance,p._cameraSequence))) return false;
            if (p == null || !p._vmdOwnsCharacter || p._loadRoutine != null ||
                (p._state != PluginState.Playing && p._state != PluginState.Paused) ||
                (!ReferenceEquals(__instance, p._motionClockSequence) && !ReferenceEquals(__instance, p._cameraSequence)) ||
                !p.TryMediaTime(out float time)) return true;
            bool body = ReferenceEquals(__instance, p._motionClockSequence);
            bool ended = !p._seekDragging && !p._seekRequested && (p._mediaEnded || (body && !SequenceLoop(__instance) && time >= __instance.Length));
            time = MediaTimeline.Position(time, __instance.Length, SequenceLoop(__instance) && !ended);
            __instance.ApplyWithSync(time, !ended, p._state == PluginState.Paused);
            if (body) p._currentAnimTime = time;
            return false;
        }

        private void VideoClockSeekCompleted(UnityEngine.Video.VideoPlayer player)
        {
            if (player != _musicVideo || !_videoSeeks.Pending) return;
            _videoSeeks.Complete();
            DispatchVideoSeek();
            if (!_videoSeeks.Pending) { if (_videoWantsPlay) StartVideoPlayback(); else _videoClockHeld = false; }
            _mediaClockFrame = -1;
        }
        private void DispatchVideoSeek()
        {
            if (_musicVideo != null && _musicVideo.isPrepared && _musicVideo.canSetTime && _videoSeeks.TryBegin(out float target))
            {
                // Seeking to the cursor already in use need not emit seekCompleted.
                if (Mathf.Abs((float)_musicVideo.time - target) < 0.001f) {
                    _videoSeeks.Complete();
                    if (_videoWantsPlay) StartVideoPlayback(); else _videoClockHeld = false;
                    return;
                }
                _videoDeadline = Time.realtimeSinceStartup + 10;
                _musicVideo.time = target;
            }
        }
        private void VideoClockEnded(UnityEngine.Video.VideoPlayer player)
        { if (player == _musicVideo && !player.isLooping && !_videoSeeks.Pending && !_seekDragging && !_seekRequested) _mediaEnded = true; _mediaClockFrame = -1; }
    }
}
