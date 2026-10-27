using System;
using BepInEx.Bootstrap;
using UnityEngine;
namespace MainGameRhythmSync {
    public sealed partial class Plugin {
        private delegate bool EffectSource(out string path, out double time, out bool playing);
        private static EffectSource _effectSource;
        private static float _effectSourceRetry;
        // No phase-lock or independent-character requirement. Invalid snapshots never return old values.
        public static bool TryGetVmdAfterimageRhythm(out string path, out int bpm, out int divisor, out string reason) {
            path = null; bpm = 0; divisor = 1; reason = "VMD再生待ち";
            if (Instance == null || !Instance.isActiveAndEnabled || !CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom) return false;
            if (_effectSource == null && Time.unscaledTime >= _effectSourceRetry) {
                _effectSourceRetry = Time.unscaledTime + 2;
                if (Chainloader.PluginInfos.TryGetValue("com.kks.maingame.vmdplay", out var info)) {
                    var method = info.Instance.GetType().GetMethod("TryGetAfterimageRhythmSource");
                    if (method != null) _effectSource = (EffectSource)Delegate.CreateDelegate(typeof(EffectSource), method);
                }
            }
            if (_effectSource == null || !_effectSource(out path, out double time, out bool playing)) return false;
            if (string.IsNullOrEmpty(path)) { reason = "VMD音源の準備中"; return false; }
            if (!playing) { reason = "VMD停止・一時停止中"; return false; }
            var beat = MainGameBeatSyncSpeed.Plugin.Instance;
            if (beat == null) { reason = "リズム解析が未ロード"; return false; }
            return beat.ReadAfterimageRhythm(path, time, out bpm, out divisor, out reason);
        }
    }
}
