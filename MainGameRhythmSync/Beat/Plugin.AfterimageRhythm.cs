using System;
using UnityEngine;
namespace MainGameBeatSyncSpeed {
    public partial class Plugin {
        private string _effectSong;
        // Both H sync and VMD effects sample the same analyzed beat and thresholds.
        private float GetZoneTargetAt(double time) {
            int index = Mathf.Clamp((int)(time / _beatDurationSec), 0, _beatIntensities.Length - 1);
            float value = _beatIntensities[index];
            float low = _cfgAutoThreshold.Value ? _autoLowThreshold : _cfgLowThreshold.Value;
            float high = _cfgAutoThreshold.Value ? _autoHighThreshold : _cfgHighThreshold.Value;
            return value < low ? LowTarget : value < high ? MidTarget : HighTarget;
        }
        internal bool ReadAfterimageRhythm(string path, double time, out int bpm, out int divisor, out string reason) {
            bpm = 0; divisor = 1; reason = "リズム同期がOFF";
            if (!Enabled) return false;
            if (!string.Equals(_effectSong, path, StringComparison.OrdinalIgnoreCase)) {
                _effectSong = path;
                _lastVideoPath = path;
                InvalidateAnalysis();
            }
            RefreshSongBpmAutoLoad();
            int registered = ReadRegisteredBpm(path);
            if (registered <= 0) { reason = "この曲のBPMが未登録"; return false; }
            if (_cfgBpm.Value != registered) _cfgBpm.Value = registered;
            EnsureAnalysis();
            if (!_analysisReady || _beatIntensities == null || _beatIntensities.Length == 0 || _beatDurationSec <= 0) {
                reason = "音源解析の準備中（音源・ffmpegの設定を確認）"; return false;
            }
            float target = GetZoneTargetAt(time);
            divisor = Mathf.Approximately(target, HighTarget) ? 1 : Mathf.Approximately(target, LowTarget) ? 4 : 2;
            bpm = registered; reason = "連動中"; return true;
        }
    }
}
