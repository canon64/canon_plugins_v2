using System;
using MainGameBlankMapAdd.Compatibility;

namespace MainGameBlankMapAdd
{
    /// <summary>外部プラグインが動画マップへ接続するための互換API。</summary>
    public sealed partial class Plugin
    {
        public static event Action<string> OnVideoLoaded;
        public static event Action<string> OnVideoStarted;
        public static event Action<string> OnVideoEnded;
        public static event Action OnSettingsApplied;

        public static bool LoadVideo(string urlOrPath)
        {
            Plugin instance = PluginCompatibilityResolver.ResolveLoadedPlugin();
            if (instance == null) return false;
            instance.LogWarn("[api] LoadVideo is disabled. Individual playback has been removed; use folder play controls.");
            return false;
        }

        public static string GetCurrentVideoPath()
        {
            Plugin instance = PluginCompatibilityResolver.ResolveLoadedPlugin();
            if (instance?._mainVideoPlayer == null) return null;
            string url = instance._mainVideoPlayer.url;
            if (string.IsNullOrEmpty(url)) return null;
            if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                url = Uri.UnescapeDataString(url.Substring(8));
            return url;
        }

        public static bool BeginAstralModeSuppression(float roomYOffset, float audioFadeSeconds)
        {
            Plugin instance = PluginCompatibilityResolver.ResolveLoadedPlugin();
            if (instance?._astralController == null) return false;
            instance._astralController.Begin(
                instance._videoRoomRoot,
                instance._mainVideoPlayer,
                instance._videoRoomAudioSource,
                roomYOffset,
                audioFadeSeconds);
            return true;
        }

        public static bool EndAstralModeSuppression()
        {
            Plugin instance = PluginCompatibilityResolver.ResolveLoadedPlugin();
            if (instance?._astralController == null) return false;
            instance._astralController.End(
                instance._videoRoomRoot,
                instance._mainVideoPlayer,
                instance._videoRoomAudioSource);
            return true;
        }

        private bool IsAstralModeSuppressed => _astralController?.IsSuppressed ?? false;
        private bool IsAstralRoomMoved => _astralController?.IsRoomMoved ?? false;

        private void MaintainAstralRoomOffset()
        {
            _astralController?.Maintain(_videoRoomRoot);
        }

        private void ApplyAstralRoomOffset(float roomYOffset)
        {
            _astralController?.EnsureRoomOffset(_videoRoomRoot, roomYOffset);
        }

        private static void FireOnVideoLoaded(string videoPath)
        {
            try { OnVideoLoaded?.Invoke(videoPath); }
            catch (Exception ex) { PluginCompatibilityResolver.ResolveLoadedPlugin()?.LogWarn($"[api] OnVideoLoaded handler threw: {ex.Message}"); }
        }

        private static void FireOnVideoStarted(string videoPath)
        {
            try { OnVideoStarted?.Invoke(videoPath); }
            catch (Exception ex) { PluginCompatibilityResolver.ResolveLoadedPlugin()?.LogWarn($"[api] OnVideoStarted handler threw: {ex.Message}"); }
        }

        private static void FireOnVideoEnded(string videoPath)
        {
            try { OnVideoEnded?.Invoke(videoPath); }
            catch (Exception ex) { PluginCompatibilityResolver.ResolveLoadedPlugin()?.LogWarn($"[api] OnVideoEnded handler threw: {ex.Message}"); }
        }

        private static void FireOnSettingsApplied()
        {
            try { OnSettingsApplied?.Invoke(); }
            catch (Exception ex) { PluginCompatibilityResolver.ResolveLoadedPlugin()?.LogWarn($"[api] OnSettingsApplied handler threw: {ex.Message}"); }
        }
    }
}
