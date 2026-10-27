using System;
using System.Reflection;

namespace MainGameHandySync
{
    // Read the same player state used by BeatSync; voices and sound effects do not count as music.
    internal static class MusicPlaybackReader
    {
        private delegate bool PlaybackSnapshot(out double time, out double length, out bool prepared, out bool playing);
        private static PlaybackSnapshot _snapshot;
        private static bool _resolved;
        private static bool _warned;

        internal static bool IsPlaying()
        {
            try
            {
                if (!_resolved)
                {
                    _resolved = true;
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type type = assembly.GetType("MainGameBlankMapAdd.Plugin", false);
                        if (type == null) continue;
                        MethodInfo method = type.GetMethod("TryGetMainVideoPlaybackSnapshot",
                            BindingFlags.Public | BindingFlags.Static, null,
                            new[] { typeof(double).MakeByRefType(), typeof(double).MakeByRefType(),
                                typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() }, null);
                        if (method != null)
                            _snapshot = (PlaybackSnapshot)Delegate.CreateDelegate(typeof(PlaybackSnapshot), method);
                        break;
                    }
                }

                double time, length;
                bool prepared, playing;
                return _snapshot != null && _snapshot(out time, out length, out prepared, out playing) && playing;
            }
            catch (Exception ex)
            {
                if (!_warned && Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                    SimpleFileLogger.Warn("[hamp] music playback state unavailable: " + ex.Message);
                _warned = true;
                return false;
            }
        }
    }
}
