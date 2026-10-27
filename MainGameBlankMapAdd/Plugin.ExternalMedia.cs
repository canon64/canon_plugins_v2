using System.Collections.Generic;
using MainGameBlankMapAdd.Compatibility;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private ExternalMediaLease _externalMedia;
        internal bool ExternalMediaBlocksPlayback => _externalMedia != null && _externalMedia.Owner != null;
        internal static bool IsExternalMediaActive => PluginCompatibilityResolver.ResolveLoadedPlugin()?.ExternalMediaBlocksPlayback == true;

        // A transient lease: does not change the room's playlist or saved settings.
        public static bool SetExternalMedia(string owner, Texture texture)
        {
            var plugin = PluginCompatibilityResolver.ResolveLoadedPlugin();
            if (plugin == null || string.IsNullOrEmpty(owner)) return false;
            if (plugin._externalMedia == null) plugin._externalMedia = plugin.gameObject.AddComponent<ExternalMediaLease>();
            var lease = plugin._externalMedia;
            if (lease.Owner != null && lease.Owner != owner) return false;
            lease.Plugin = plugin; lease.Owner = owner; lease.Texture = texture;
            lease.enabled = true;
            plugin.ClearDeferredPlayFromBar();
            plugin._pendingVideoStarts.Clear();
            lease.Apply();
            return texture == null || lease.SurfaceCount > 0;
        }

        public static void ReleaseExternalMedia(string owner)
        {
            var plugin = PluginCompatibilityResolver.ResolveLoadedPlugin();
            if (plugin != null && plugin._externalMedia != null && plugin._externalMedia.Owner == owner)
                plugin._externalMedia.Release();
        }

        [DefaultExecutionOrder(32000)]
        private sealed class ExternalMediaLease : MonoBehaviour
        {
            internal Plugin Plugin;
            internal string Owner;
            internal Texture Texture;
            internal int SurfaceCount => _surfaces.Count;
            private GameObject _root;
            private int _children;
            private float _nextSurfaceRetry;
            private AudioSource _audio;
            private bool _mute;
            private readonly List<Surface> _surfaces = new List<Surface>();
            private sealed class Surface { internal Renderer Renderer; internal Material Original, Override; }

            internal void Apply()
            {
                if (Plugin == null || Owner == null) return;
                if (Plugin._mainVideoPlayer != null && Plugin._mainVideoPlayer.isPlaying) Plugin._mainVideoPlayer.Pause();
                var source = Plugin._videoRoomAudioSource;
                if (source != _audio) {
                    if (_audio != null) _audio.mute = _mute;
                    _audio = source; if (_audio != null) _mute = _audio.mute;
                }
                if (_audio != null && !_audio.mute) _audio.mute = true;
                var root = Plugin._videoRoomRoot;
                if (root != _root || (root != null && root.transform.childCount != _children) || (Texture != null && _surfaces.Count == 0 && Time.unscaledTime >= _nextSurfaceRetry)) {
                    _nextSurfaceRetry = Time.unscaledTime + 1f;
                    RestoreSurfaces(); _root = root; _children = root == null ? 0 : root.transform.childCount;
                    if (root != null && Texture != null) foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
                        if (!renderer.name.StartsWith("VideoSurface_") || renderer.name.Contains("Floor") || renderer.name.Contains("Ceiling")) continue;
                        var original = renderer.sharedMaterial;
                        if (original == null) continue;
                        var replacement = new Material(original);
                        _surfaces.Add(new Surface { Renderer = renderer, Original = original, Override = replacement });
                    }
                }
                if (Texture == null) { RestoreSurfaces(); return; }
                foreach (var surface in _surfaces) {
                    if (surface.Renderer == null) continue;
                    if (surface.Override.mainTexture != Texture) {
                        surface.Override.mainTexture = Texture;
                        if (surface.Override.HasProperty("_EmissionMap")) surface.Override.SetTexture("_EmissionMap", Texture);
                    }
                    if (surface.Renderer.sharedMaterial != surface.Override) surface.Renderer.sharedMaterial = surface.Override;
                }
            }
            private void LateUpdate() { Apply(); }
            private void RestoreSurfaces() {
                foreach (var surface in _surfaces) {
                    if (surface.Renderer != null && surface.Renderer.sharedMaterial == surface.Override) surface.Renderer.sharedMaterial = surface.Original;
                    if (surface.Override != null) Destroy(surface.Override);
                }
                _surfaces.Clear();
            }
            internal void Release() {
                RestoreSurfaces();
                if (_audio != null) _audio.mute = _mute;
                _audio = null; _root = null; Owner = null; Texture = null; enabled = false;
            }
            private void OnDestroy() { Release(); }
        }
    }
}
