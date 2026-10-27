using System;
using System.Collections;
using MainGameBlankMapAdd.Application;
using UnityEngine;
using UnityEngine.Video;

namespace MainGameBlankMapAdd.Room
{
    internal sealed class AstralRoomController : IDisposable
    {
        private readonly ICoroutineHost _coroutines;
        private readonly Action _clearPendingPlayback;
        private readonly Action<string> _warning;
        private bool _roomMoved;
        private Vector3 _savedRoomLocalPosition;
        private Vector3 _savedRoomWorldPosition;
        private Vector3 _targetRoomWorldPosition;
        private Coroutine _fadeCoroutine;
        private bool _roomEnforceLogged;
        private bool _videoWasPlaying;
        private float _savedVideoVolume = 1f;
        private float _savedAudioFadeSeconds;

        internal AstralRoomController(ICoroutineHost coroutines, Action clearPendingPlayback, Action<string> warning)
        {
            _coroutines = coroutines ?? throw new ArgumentNullException(nameof(coroutines));
            _clearPendingPlayback = clearPendingPlayback ?? throw new ArgumentNullException(nameof(clearPendingPlayback));
            _warning = warning ?? throw new ArgumentNullException(nameof(warning));
        }

        internal bool IsSuppressed { get; private set; }
        internal bool IsRoomMoved => _roomMoved;

        internal void Begin(GameObject room, VideoPlayer player, AudioSource source, float roomYOffset, float audioFadeSeconds)
        {
            if (!IsSuppressed)
            {
                _videoWasPlaying = player != null && player.isPlaying;
                _savedVideoVolume = source != null ? source.volume : 1f;
            }
            _savedAudioFadeSeconds = Mathf.Max(0f, audioFadeSeconds);
            IsSuppressed = true;
            _clearPendingPlayback();
            EnsureRoomOffset(room, roomYOffset);
            StopFade();
            _fadeCoroutine = _coroutines.Start(FadeOutAndPause(player, source, Mathf.Max(0f, audioFadeSeconds)));
            _warning($"[astral] suppression begin fade={audioFadeSeconds:F3}");
        }

        internal void End(GameObject room, VideoPlayer player, AudioSource source)
        {
            IsSuppressed = false;
            StopFade();
            if (room != null && _roomMoved)
            {
                room.transform.position = _savedRoomWorldPosition;
                room.transform.localPosition = _savedRoomLocalPosition;
                _warning($"[astral] video room restored world={_savedRoomWorldPosition} local={_savedRoomLocalPosition}");
            }
            _roomMoved = false;
            _fadeCoroutine = _coroutines.Start(FadeInAndResume(player, source, _savedAudioFadeSeconds));
            _warning($"[astral] suppression end; resume requested wasPlaying={_videoWasPlaying} fade={_savedAudioFadeSeconds:F3}");
        }

        internal void EnsureRoomOffset(GameObject room, float roomYOffset)
        {
            if (room == null || _roomMoved) return;
            _savedRoomLocalPosition = room.transform.localPosition;
            _savedRoomWorldPosition = room.transform.position;
            _targetRoomWorldPosition = _savedRoomWorldPosition + new Vector3(0f, roomYOffset, 0f);
            room.transform.position = _targetRoomWorldPosition;
            _roomMoved = true;
            _warning($"[astral] video room moved worldY={roomYOffset:F3} savedWorld={_savedRoomWorldPosition} targetWorld={_targetRoomWorldPosition} savedLocal={_savedRoomLocalPosition} nowLocal={room.transform.localPosition}");
        }

        internal void Maintain(GameObject room)
        {
            if (!IsSuppressed || room == null || !_roomMoved) return;
            if (Vector3.Distance(room.transform.position, _targetRoomWorldPosition) <= 0.01f)
            {
                _roomEnforceLogged = false;
                return;
            }
            Vector3 before = room.transform.position;
            room.transform.position = _targetRoomWorldPosition;
            if (_roomEnforceLogged) return;
            _roomEnforceLogged = true;
            _warning($"[astral] video room position enforced before={before} target={_targetRoomWorldPosition} local={room.transform.localPosition} (連続enforceログは定位置復帰まで抑制)");
        }

        private IEnumerator FadeOutAndPause(VideoPlayer player, AudioSource source, float duration)
        {
            float startVolume = source != null ? source.volume : 0f;
            if (duration > 0f && source != null)
            {
                float elapsed = 0f;
                while (elapsed < duration && IsSuppressed)
                {
                    elapsed += Time.unscaledDeltaTime;
                    source.volume = Mathf.Lerp(startVolume, 0f, Mathf.Clamp01(elapsed / duration));
                    yield return null;
                }
            }
            if (source != null) source.volume = 0f;
            if (player != null)
            {
                try { player.Pause(); } catch { }
                try { player.playOnAwake = false; } catch { }
            }
            _fadeCoroutine = null;
            _warning($"[astral] video paused player={(player != null ? player.url : "null")} startVolume={startVolume:F3}");
        }

        private IEnumerator FadeInAndResume(VideoPlayer player, AudioSource source, float duration)
        {
            if (player != null && _videoWasPlaying && !Plugin.IsExternalMediaActive)
            {
                try { player.Play(); } catch { }
            }
            if (source != null)
            {
                source.volume = 0f;
                if (duration > 0f)
                {
                    float elapsed = 0f;
                    while (elapsed < duration && !IsSuppressed)
                    {
                        elapsed += Time.unscaledDeltaTime;
                        source.volume = Mathf.Lerp(0f, _savedVideoVolume, Mathf.Clamp01(elapsed / duration));
                        yield return null;
                    }
                }
                if (!IsSuppressed) source.volume = _savedVideoVolume;
            }
            _fadeCoroutine = null;
            _warning($"[astral] video resumed wasPlaying={_videoWasPlaying} targetVolume={_savedVideoVolume:F3}");
        }

        private void StopFade()
        {
            if (_fadeCoroutine == null) return;
            _coroutines.Stop(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        public void Dispose()
        {
            StopFade();
            IsSuppressed = false;
            _roomMoved = false;
        }
    }
}
