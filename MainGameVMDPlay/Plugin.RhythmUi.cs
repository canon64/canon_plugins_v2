using System;
using BepInEx.Bootstrap;
using UnityEngine;
namespace MainGameVMDPlay {
    public sealed partial class Plugin {
        private Func<string, int> _readSongBpm;
        private Func<int, bool> _registerSongBpm;
        private float _rhythmUiRetry;
        private string _bpmSong, _songBpmInput = "128", _songBpmStatus;
        private void DrawRhythmBpm() {
            if (_readSongBpm == null && Time.unscaledTime >= _rhythmUiRetry) {
                _rhythmUiRetry = Time.unscaledTime + 3;
                foreach (var info in Chainloader.PluginInfos.Values) {
                    var type = info.Instance.GetType();
                    if (type.FullName != "MainGameRhythmSync.Plugin") continue;
                    var read = type.GetMethod("GetRegisteredSongBpm"); var write = type.GetMethod("RegisterCurrentSongBpm");
                    if (read != null && write != null) {
                        _readSongBpm = (Func<string, int>)Delegate.CreateDelegate(typeof(Func<string, int>), read);
                        _registerSongBpm = (Func<int, bool>)Delegate.CreateDelegate(typeof(Func<int, bool>), write);
                    }
                    break;
                }
            }
            int saved = _readSongBpm != null && !string.IsNullOrEmpty(_musicPath) ? _readSongBpm(_musicPath) : 0;
            if (_bpmSong != _musicPath) { _bpmSong = _musicPath; _songBpmInput = (saved > 0 ? saved : 128).ToString(); _songBpmStatus = ""; }
            GUILayout.BeginHorizontal();
            GUILayout.Label(saved > 0 ? "音源BPM: " + saved : "音源BPM: 未登録");
            _songBpmInput = GUILayout.TextField(_songBpmInput, GUILayout.Width(65));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && _registerSongBpm != null && _vmdOwnsCharacter && !_musicLoading && !string.IsNullOrEmpty(_musicPath);
            if (GUILayout.Button("BPMを登録")) {
                _songBpmStatus = int.TryParse(_songBpmInput, out int bpm) && bpm >= 1 && bpm <= 999 && _registerSongBpm(bpm)
                    ? "登録しました。RhythmSync有効時、呼出キャラの曲にHアニメが同期します。" : "BPMは1〜999で指定してください。音源の準備も確認してください。";
            }
            GUI.enabled = enabled; GUILayout.EndHorizontal();
            if (_registerSongBpm == null) GUILayout.Label("BPM登録には対応版RhythmSyncが必要です");
            if (!string.IsNullOrEmpty(_songBpmStatus)) GUILayout.Label(_songBpmStatus);
        }
    }
}
