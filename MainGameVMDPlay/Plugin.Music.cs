using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private AudioSource _music;
        private Coroutine _musicRoutine;
        private UnityWebRequest _musicRequest;
        private bool _musicLoading;
        private string _musicPath = "";
        private string _musicError = "";

        private void CancelMusicLoad()
        {
            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = null;
            if (_musicRequest != null) { _musicRequest.Abort(); _musicRequest.Dispose(); _musicRequest = null; }
            _musicLoading = false;
            if (_musicVideo != null && !_musicVideo.isPrepared) ClearVideoMusic();
        }

        private void PlaybackLoadAudio(string path, bool playNow)
        {
            CancelMusicLoad();
            VmdBridgeSoundClearClip();
            _musicError = "";
            if (!File.Exists(path)) { _musicError = "音楽ファイルが見つかりません"; return; }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp4" || ext == ".webm" || ext == ".mov" || ext == ".avi" || ext == ".m4v" || ext == ".wmv") {
                _musicLoading = true; _musicRoutine = StartCoroutine(LoadVideoMusic(path)); return;
            }
            AudioType type = ext == ".wav" ? AudioType.WAV : ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".mp3" ? AudioType.MPEG : AudioType.UNKNOWN;
            if (type == AudioType.UNKNOWN) { _musicError = "WAV / OGG / MP3を選択してください"; return; }
            _musicLoading = true;
            _musicRoutine = StartCoroutine(LoadMusic(path, type, playNow));
        }

        private IEnumerator LoadMusic(string path, AudioType type, bool playNow)
        {
            _musicRequest = UnityWebRequestMultimedia.GetAudioClip(new Uri(Path.GetFullPath(path)).AbsoluteUri, type);
            yield return _musicRequest.SendWebRequest();
            AudioClip clip = null;
            try {
                if (_musicRequest.isNetworkError || _musicRequest.isHttpError) _musicError = _musicRequest.error;
                else clip = DownloadHandlerAudioClip.GetContent(_musicRequest);
            } catch (Exception ex) { _musicError = ex.Message; }
            _musicRequest.Dispose();
            _musicRequest = null;
            _musicLoading = false;
            _musicRoutine = null;
            if (clip == null) { if (string.IsNullOrEmpty(_musicError)) _musicError = "音楽を読み込めませんでした"; yield break; }
            if (_music == null) {
                _music = gameObject.AddComponent<AudioSource>();
                _music.playOnAwake = false;
                _music.spatialBlend = 0f;
            }
            _music.clip = clip;
            ApplyMusicOutput();
            ApplyAudioSpeed();
            _music.loop = _settings.AudioLoop;
            _musicPath = path;
            _settings.LastAudioAbsPath = path;
            _settings.AudioAutoLoad = true;
            _settings.AudioAutoPlay = true;
            SaveSettings("music-selected");
            if (_loadRoutine == null && _vmdOwnsCharacter && _state == PluginState.Playing) VmdBridgeSoundPlay();
        }

        private void SeekMusic(float time, bool play)
        {
            _mediaEnded = false; _mediaClockFrame = -1;
            if (_musicVideo != null && _musicVideo.isPrepared) {
                double lengthVideo = _musicVideo.length;
                if (double.IsNaN(lengthVideo) || double.IsInfinity(lengthVideo)) lengthVideo = 0;
                if (_musicVideo.isLooping && lengthVideo > 0) time = (float)(Mathf.Max(0, time) % lengthVideo);
                else if (lengthVideo > 0 && time >= lengthVideo) { time = (float)lengthVideo; _mediaEnded = true; play = false; }
                _videoWantsPlay = play;
                _videoHeldTime = Mathf.Max(0, time); _videoClockHeld = true;
                if (_musicVideo.canSetTime) { _videoSeeks.Request(_videoHeldTime); DispatchVideoSeek(); }
                if (play) StartVideoPlayback();
                else _musicVideo.Pause();
                return;
            }
            if (_music == null || _music.clip == null) return;
            float length = _music.clip.length;
            if (length <= 0) return;
            if (_music.loop) time = Mathf.Max(0, time) % length;
            else if (time >= length) { _mediaEnded = true; play = false; }
            _music.time = Mathf.Clamp(time, 0, Mathf.Max(0, length - 0.001f));
            if (play) { SetWallMedia(null); if (!_music.isPlaying) _music.Play(); }
            else _music.Pause();
        }

        private bool VmdBridgeSoundPlay() { SeekMusic((_controller as UnityEngine.Object) != null ? VmdBridgeGetAnimTime(_controller) : 0, true); return _musicVideo != null || (_music != null && _music.clip != null); }
        private bool VmdBridgeSoundPause() { _videoWantsPlay = false; _videoStarting = false; if (_musicVideo != null) _musicVideo.Pause(); if (_music != null) _music.Pause(); return true; }
        private bool VmdBridgeSoundStop() { _videoWantsPlay = false; _videoStarting = false; _videoClockHeld = false; _mediaEnded = false; _videoSeeks.Reset(); _mediaClockFrame = -1; if (_musicVideo != null && _musicVideo.isPrepared) { _musicVideo.Pause(); if (_musicVideo.canSetTime) _musicVideo.time = 0; } if (_music != null) _music.Stop(); ReleaseWallMedia(); return true; }
        private bool VmdBridgeSoundClearClip()
        {
            CancelMusicLoad();
            UnregisterMusicReverb();
            ClearVideoMusic();
            _videoLoadRequested = false;
            if (_music != null) { _music.Stop(); var clip = _music.clip; _music.clip = null; if (clip != null) Destroy(clip); }
            _musicPath = "";
            return true;
        }
        private bool VmdBridgeSoundSetLoop(bool loop) { if (_musicVideo != null) _musicVideo.isLooping = loop; if (_music != null) _music.loop = loop; return true; }
        private float VmdBridgeSoundGetLength() => _musicVideo != null && _musicVideo.isPrepared ? (float)_musicVideo.length : _music != null && _music.clip != null ? _music.clip.length : 0;
        private string VmdBridgeSoundGetAudioFilePath() => _musicPath;
        private bool VmdBridgeSoundIsAvailable() => true;

        private void DrawMusicSection()
        {
            DrawMusicVolume();
            DrawRhythmBpm();
            GUILayout.Label("音源: " + (_musicLoading ? "読み込み中…" : string.IsNullOrEmpty(_musicPath) ? "未選択" : Path.GetFileName(_musicPath)));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("音楽を解除", GUILayout.Width(100))) PlaybackClearAudio();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_musicError)) GUILayout.Label("音楽エラー: " + _musicError);
            bool loop = DrawBinaryControl("audio.Loop", "音楽をループ", _settings.AudioLoop);
            if (loop != _settings.AudioLoop) { _settings.AudioLoop = loop; _settings.Adjustments["audio.Loop"] = loop ? 1 : 0; VmdBridgeSoundSetLoop(loop); SaveSettings("music-loop"); }
            bool walls = GUILayout.Toggle(_settings.VideoOnWalls, "動画をBlankMapAddの壁ディスプレイに表示");
            if (walls != _settings.VideoOnWalls) { _settings.VideoOnWalls = walls; SaveSettings("video-walls"); if (_wallLease) SetWallMedia(walls ? _musicVideoTexture : null); }
            if (!string.IsNullOrEmpty(_wallStatus)) GUILayout.Label(_wallStatus);
            GUILayout.Label("音源時刻にVMD・カメラが追従します（速度は音源速度で調整）");
        }
    }
}
