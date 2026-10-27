using System;
using BepInEx.Bootstrap;
using UnityEngine;
namespace CanonPlugins.Shared {
    internal static class VmdRhythmSource {
        private delegate bool ReadSource(out string path, out double time, out bool playing);
        private static ReadSource _read;
        private static float _retryAt;
        internal static bool TryRead(out string path, out double time, out bool playing) {
            path = null; time = 0; playing = false;
            if (_read == null && Time.unscaledTime >= _retryAt) {
                _retryAt = Time.unscaledTime + 3;
                if (Chainloader.PluginInfos.TryGetValue("com.kks.maingame.vmdplay", out var plugin)) {
                    var method = plugin.Instance.GetType().GetMethod("TryGetRhythmSource");
                    if (method != null) _read = (ReadSource)Delegate.CreateDelegate(typeof(ReadSource), method);
                }
            }
            return _read != null && _read(out path, out time, out playing);
        }
    }
}
