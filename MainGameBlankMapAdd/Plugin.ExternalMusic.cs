using System.Collections.Generic;
using MainGameBlankMapAdd.Compatibility;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private ExternalMusicSources _externalMusicSources;
        public static bool RegisterExternalMusicSource(string owner, AudioSource source)
        {
            var plugin = PluginCompatibilityResolver.ResolveLoadedPlugin();
            if (plugin == null || string.IsNullOrEmpty(owner) || source == null) return false;
            if (plugin._externalMusicSources == null) {
                plugin._externalMusicSources = plugin.gameObject.AddComponent<ExternalMusicSources>();
                plugin._externalMusicSources.Plugin = plugin;
            }
            plugin._externalMusicSources.Register(owner, source);
            return true;
        }
        public static void UnregisterExternalMusicSource(string owner)
        {
            var plugin = PluginCompatibilityResolver.ResolveLoadedPlugin();
            if (plugin != null && plugin._externalMusicSources != null) plugin._externalMusicSources.Unregister(owner);
        }
        private void ConfigureExternalMusicReverb(AudioSource source)
        {
            float mix = ResolveVideoAudioReverbMix();
            // V-REV gates both video audio and external VMD music.
            // Preserve the owner's volume and mute.
            bool active = _videoRoomRoot != null && (_settings?.ApplyReverbToVideoAudio ?? false) && mix > 0.0001f;
            source.bypassReverbZones = !active;
            source.reverbZoneMix = active ? mix : 0;
        }
        private sealed class ExternalMusicSources : MonoBehaviour
        {
            internal Plugin Plugin;
            private sealed class Entry { internal AudioSource Source; internal bool Bypass; internal float Mix; }
            private readonly Dictionary<string, Entry> _sources = new Dictionary<string, Entry>();
            private float _next;
            internal void Register(string owner, AudioSource source) {
                if (_sources.TryGetValue(owner, out var current) && current.Source == source) { Plugin.ConfigureExternalMusicReverb(source); return; }
                Unregister(owner);
                _sources[owner] = new Entry { Source = source, Bypass = source.bypassReverbZones, Mix = source.reverbZoneMix };
                Plugin.ConfigureExternalMusicReverb(source);
                enabled = true;
            }
            internal void Unregister(string owner) {
                if (!_sources.TryGetValue(owner, out var entry)) return;
                Restore(entry); _sources.Remove(owner);
                if (_sources.Count == 0) enabled = false;
            }
            private static void Restore(Entry entry) { if (entry.Source != null) { entry.Source.bypassReverbZones = entry.Bypass; entry.Source.reverbZoneMix = entry.Mix; } }
            private void LateUpdate() {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.25f;
                foreach (var entry in _sources.Values) if (entry.Source != null) Plugin.ConfigureExternalMusicReverb(entry.Source);
            }
            private void OnDestroy() { foreach (var entry in _sources.Values) Restore(entry); _sources.Clear(); }
        }
    }
}
