using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Video;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private VideoPlayer _musicVideo;
        private bool _videoWantsPlay, _videoStarting, _videoClockHeld;
        private float _videoDeadline, _videoHeldTime;
        private bool _videoFailureHandled, _videoLoadRequested;
        private void StartVideoPlayback()
        {
            if (_musicVideo == null || !_musicVideo.isPrepared || !_videoWantsPlay || _videoSeeks.Pending) return;
            SetWallMedia(_settings.VideoOnWalls ? _musicVideoTexture : null);
            if (_musicVideo.isPlaying) { _videoStarting = false; _videoClockHeld = false; return; }
            if (_videoStarting) return;
            _videoStarting = true; _videoDeadline = Time.realtimeSinceStartup + 10;
            _musicVideo.Play();
        }
        private void VideoPlaybackStarted(VideoPlayer player)
        {
            if (player != _musicVideo || !_videoWantsPlay) return;
            _videoStarting = false; _videoClockHeld = false; _mediaClockFrame = -1;
            LogInfo("[Video] started: " + _musicPath);
        }
        private void TickVideoPlayback()
        {
            if (_musicVideo != null && (_videoSeeks.Pending || _videoStarting) && Time.realtimeSinceStartup >= _videoDeadline && string.IsNullOrEmpty(_musicError))
                OnVideoMusicError(_musicVideo, _videoSeeks.Pending ? "動画シークが完了しませんでした" : "動画の再生開始を確認できませんでした");
            if (!string.IsNullOrEmpty(_musicError) && !_videoFailureHandled && _videoLoadRequested) {
                _videoFailureHandled = true;
                _videoWantsPlay = false; _videoStarting = false; _videoClockHeld = false;
                if (_musicVideo != null) _musicVideo.Pause();
                if (_vmdOwnsCharacter && _state == PluginState.Playing) PlaybackPause();
                _presetMessage = "動画再生失敗: " + _musicError;
                LogError("[Video] " + _musicError);
            }
        }
        private RenderTexture _musicVideoTexture;
        private MethodInfo _wallSet, _wallRelease;
        private bool _wallLease;
        private string _wallStatus = "";

        private void SetWallMedia(Texture texture)
        {
            if (_wallSet == null && Chainloader.PluginInfos.TryGetValue("com.kks.maingameblankmapadd", out var info)) {
                var type = info.Instance.GetType();
                _wallSet = type.GetMethod("SetExternalMedia", BindingFlags.Public | BindingFlags.Static);
                _wallRelease = type.GetMethod("ReleaseExternalMedia", BindingFlags.Public | BindingFlags.Static);
            }
            if (_wallSet == null) { _wallStatus = "BlankMapAdd連携版が未ロードです"; return; }
            try {
                bool displayed = (bool)_wallSet.Invoke(null, new object[] { Guid, texture });
                _wallLease = true;
                _wallStatus = displayed ? "BlankMapAdd音楽ミュート中" : "壁ディスプレイ待機中（BlankMapAdd音楽ミュート中）";
            } catch (Exception ex) { _wallStatus = "壁連携失敗: " + ex.GetBaseException().Message; }
        }
        private void ReleaseWallMedia()
        {
            if (_wallLease) { try { _wallRelease?.Invoke(null, new object[] { Guid }); } catch (Exception ex) { LogWarn(ex.Message); } }
            _wallLease = false; _wallStatus = "";
        }
        private IEnumerator LoadVideoMusic(string path)
        {
            if (_music == null) { _music = gameObject.AddComponent<AudioSource>(); _music.playOnAwake = false; _music.spatialBlend = 0; }
            _videoFailureHandled = false; _videoLoadRequested = true;
            _videoHeldTime = _controller != null ? VmdBridgeGetAnimTime(_controller) : 0;
            _videoClockHeld = true;
            _music.enabled = true; _music.mute = false; _music.pitch = 1;
            ApplyMusicOutput();
            _musicVideo = gameObject.AddComponent<VideoPlayer>();
            _musicVideo.playOnAwake = false;
            _musicVideo.source = VideoSource.Url;
            _musicVideo.url = System.IO.Path.GetFullPath(path);
            _musicVideo.waitForFirstFrame = true;
            _musicVideo.skipOnDrop = true;
            _musicVideo.renderMode = VideoRenderMode.RenderTexture;
            _musicVideo.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _musicVideo.controlledAudioTrackCount = 1;
            _musicVideo.EnableAudioTrack(0, true);
            _musicVideo.SetTargetAudioSource(0, _music);
            _musicVideo.isLooping = _settings.AudioLoop;
            _musicVideo.started += VideoPlaybackStarted;
            _musicVideo.errorReceived += OnVideoMusicError;
            _musicVideo.seekCompleted += VideoClockSeekCompleted;
            _musicVideo.loopPointReached += VideoClockEnded;
            // Assign a valid output before Prepare, as BlankMapAdd does.
            _musicVideoTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            _musicVideoTexture.Create(); _musicVideo.targetTexture = _musicVideoTexture;
            _musicVideo.Prepare();
            float deadline = Time.realtimeSinceStartup + 30;
            while (!_musicVideo.isPrepared && string.IsNullOrEmpty(_musicError) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!_musicVideo.isPrepared) {
                if (string.IsNullOrEmpty(_musicError)) _musicError = "動画の読み込みがタイムアウトしました";
                ClearVideoMusic(); _musicLoading = false; _musicRoutine = null; yield break;
            }
            if (_musicVideo.audioTrackCount == 0) {
                _musicError = "選択した動画には音声トラックがありません";
                ClearVideoMusic(); _musicLoading = false; _musicRoutine = null; yield break;
            }
            _musicVideo.EnableAudioTrack(0, true);
            _musicVideo.SetTargetAudioSource(0, _music);
            LogInfo("[Video] prepared length=" + _musicVideo.length + " tracks=" + _musicVideo.audioTrackCount + " path=" + path);
            ApplyAudioSpeed();
            _musicPath = path; _settings.LastAudioAbsPath = path; _settings.AudioAutoLoad = true; _settings.AudioAutoPlay = true;
            SaveSettings("video-music-selected");
            _musicLoading = false; _musicRoutine = null;
            if (_loadRoutine == null && _vmdOwnsCharacter && _state == PluginState.Playing) VmdBridgeSoundPlay();
        }
        private void OnVideoMusicError(VideoPlayer player, string message) { _musicError = message; _videoSeeks.Reset(); ReleaseWallMedia(); }
        private void ClearVideoMusic()
        {
            _videoWantsPlay = false; _videoStarting = false; _videoClockHeld = false;
            if (_musicVideo != null) { _musicVideo.started -= VideoPlaybackStarted; _musicVideo.errorReceived -= OnVideoMusicError; _musicVideo.seekCompleted -= VideoClockSeekCompleted; _musicVideo.loopPointReached -= VideoClockEnded; _videoSeeks.Reset(); _musicVideo.Stop(); Destroy(_musicVideo); _musicVideo = null; }
            ReleaseWallMedia();
            if (_musicVideoTexture != null) { _musicVideoTexture.Release(); Destroy(_musicVideoTexture); _musicVideoTexture = null; }
        }
    }
}
