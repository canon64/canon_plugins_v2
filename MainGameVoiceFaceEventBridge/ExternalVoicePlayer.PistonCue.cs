using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using MainGameVoiceFaceEventBridge.Audio;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class ExternalVoicePlayer
    {
        [DataContract]
        private sealed class PistonManifest
        {
            [DataMember(Name = "items")]
            public PistonManifestItem[] items;
        }

        [DataContract]
        private sealed class PistonManifestItem
        {
            [DataMember(Name = "id")]
            public int id;
            [DataMember(Name = "file")]
            public string file;
            [DataMember(Name = "text")]
            public string text;
        }

        private sealed class PistonCueEntry
        {
            internal int AssetIndex;
            internal string Subtitle;
        }

        private readonly List<PistonCueEntry> _pistonCues = new List<PistonCueEntry>();
        private readonly List<PistonCueEntry> _hardPistonCues = new List<PistonCueEntry>();
        private readonly List<PistonCueEntry> _ogazmCues = new List<PistonCueEntry>();
        private CueShuffleBag _pistonCueBag;
        private CueShuffleBag _hardPistonCueBag;
        private CueShuffleBag _ogazmCueBag;
        private bool _hardPistonCueActive;
        private bool _ogazmCueActive;
        private float _pistonCueVolume = 1f;
        private double _pistonNextVoiceAt;
        private double _hardPistonNextVoiceAt;
        private double _ogazmNextVoiceAt;
        internal bool HardPistonCuePlaying => _hardPistonCueActive && _cueSource != null && _cueSource.isPlaying;
        internal bool OgazmCuePlaying => _ogazmCueActive && _cueSource != null && _cueSource.isPlaying;
        internal bool SpecialAibuCuePlaying => _priorityPistonCueActive && _cueState.Busy;

        private void LoadPistonCues(string directory, bool hard = false, bool ogazm = false)
        {
            List<PistonCueEntry> cues = ogazm ? _ogazmCues : hard ? _hardPistonCues : _pistonCues;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            string manifestPath = Path.Combine(directory, "manifest.json");
            var subtitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var selectedFiles = new List<string>();
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasManifestItems = false;
            if (File.Exists(manifestPath))
            {
                try
                {
                    PistonManifest manifest;
                    using (var stream = File.OpenRead(manifestPath))
                        manifest = (PistonManifest)new DataContractJsonSerializer(typeof(PistonManifest)).ReadObject(stream);
                    if (manifest?.items != null)
                    {
                        hasManifestItems = true;
                        foreach (PistonManifestItem item in manifest.items)
                        {
                            if (item == null) continue;
                            string file = string.IsNullOrWhiteSpace(item.file)
                                ? (item.id > 0 ? item.id.ToString("D3") + ".wav" : null)
                                : item.file.Trim();
                            if (string.IsNullOrWhiteSpace(file) || Path.GetFileName(file) != file ||
                                !string.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase))
                            {
                                _logWarn?.Invoke("[piston-cue] invalid manifest file: " + file);
                                continue;
                            }
                            if (!seenFiles.Add(file)) continue;
                            selectedFiles.Add(file);
                            subtitles[file] = (item.text ?? string.Empty).Trim();
                        }
                    }
                    Plugin.LogImpactCueDiagnostic("piston subtitles=" + subtitles.Count + " path=" + manifestPath);
                }
                catch (Exception ex)
                {
                    Plugin.LogImpactCueDiagnostic("piston subtitles unavailable path=" + manifestPath +
                        " error=" + ex.Message);
                }
            }
            else
            {
                Plugin.LogImpactCueDiagnostic("piston subtitles unavailable manifest missing path=" + manifestPath);
            }
            string[] wavPaths;
            if (hasManifestItems)
            {
                wavPaths = new string[selectedFiles.Count];
                for (int i = 0; i < selectedFiles.Count; i++) wavPaths[i] = Path.Combine(directory, selectedFiles[i]);
            }
            else wavPaths = ogazm ? new string[0] : Directory.GetFiles(directory, "*.wav", SearchOption.TopDirectoryOnly);
            Array.Sort(wavPaths, StringComparer.OrdinalIgnoreCase);
            Plugin.LogImpactCueDiagnostic("piston wav files=" + wavPaths.Length + " path=" + directory);
            foreach (string path in wavPaths)
            {
                string file = Path.GetFileName(path);
                if (!File.Exists(path))
                {
                    _logWarn?.Invoke("[piston-cue] missing " + file);
                    continue;
                }
                if (!WavFileLoader.TryLoadPcm(path, out PcmData pcm, out string error))
                {
                    Plugin.LogImpactCueDiagnostic("piston wav skipped file=" + file + " error=" + error);
                    _logWarn?.Invoke("[piston-cue] skipped " + file + ": " + error);
                    continue;
                }
                double seconds = (double)pcm.Pcm.Length / pcm.Channels / pcm.SampleRate;
                if (seconds > 30)
                {
                    Plugin.LogImpactCueDiagnostic("piston wav skipped file=" + file + " over30sec=" + seconds.ToString("F2"));
                    _logWarn?.Invoke("[piston-cue] skipped " + file + ": longer than 30 seconds");
                    continue;
                }
                cues.Add(new PistonCueEntry
                {
                    AssetIndex = _cueAssets.Count,
                    Subtitle = subtitles.TryGetValue(file, out string text) ? text : string.Empty
                });
                _cueAssets.Add(new CueAsset { Pcm = pcm });
            }
            if (cues.Count > 0)
            {
                var bag = new CueShuffleBag(cues.Count, new System.Random());
                if (ogazm) _ogazmCueBag = bag;
                else if (hard) _hardPistonCueBag = bag;
                else _pistonCueBag = bag;
            }
            _logInfo?.Invoke("[" + (ogazm ? "ogazm" : hard ? "hard-piston" : "piston") + "-cue] loaded=" + cues.Count);
        }

        internal bool CanStartPistonCue(string owner)
        {
            bool allowed = _cueReady && _pistonCueBag != null &&
                !string.IsNullOrEmpty(owner) && _cueOwners.Contains(owner) && !_cueState.Busy &&
                Time.unscaledTime >= _cueState.NextAllowedAt && Time.unscaledTime >= _pistonNextVoiceAt;
            if (!allowed)
                Plugin.LogImpactCueDiagnostic("piston rejected ready=" + _cueReady +
                    " loaded=" + _pistonCues.Count + " ownerEnabled=" + _cueOwners.Contains(owner ?? "") +
                    " phase=" + _cueState.Phase + " cooldownSec=" +
                    Math.Max(0, _cueState.NextAllowedAt - Time.unscaledTime).ToString("F2"));
            return allowed;
        }

        internal bool CanStartHardPistonCue(string owner)
            => _cueReady && _hardPistonCueBag != null && femaleOwnerValid(owner) &&
               !_cueState.Busy &&
               Time.unscaledTime >= _hardPistonNextVoiceAt;

        internal bool CanStartOgazmCue(string owner)
            => _cueReady && _ogazmCueBag != null && femaleOwnerValid(owner) &&
               !_cueState.Busy && Time.unscaledTime >= _ogazmNextVoiceAt;

        private bool femaleOwnerValid(string owner)
            => !string.IsNullOrEmpty(owner) && _cueOwners.Contains(owner);

        internal bool RequestContinuousPistonCue(string owner, ChaControl female,
            float volume, float cooldownSeconds,
            out string subtitle, out float duration)
        {
            subtitle = string.Empty;
            duration = 0f;
            if (!CanStartPistonCue(owner) || female == null) return false;
            // Keep priority deep hits able to replace this voice; never replace an active cue.
            Stop("continuous piston cue");
            if (!_cueState.TryBegin(Time.unscaledTime, false, out CueAction action)) return false;
            PistonCueEntry entry = _pistonCues[_pistonCueBag.Next()];
            _priorityPistonCueActive = true;
            _hardPistonCueActive = false;
            _ogazmCueActive = false;
            _pistonCueVolume = Mathf.Clamp01(volume);
            _activeCueOwner = owner;
            _cueFemale = female;
            _selectedCue = _cueAssets[entry.AssetIndex].Clip;
            ApplyCueActions(action); // Existing cue source binds female.SetLipSync(_cueSource).
            if (_selectedCue == null || !_cueSource.isPlaying)
            {
                Plugin.LogImpactCueDiagnostic("piston start failed clip=" + (_selectedCue?.name ?? "null") +
                    " sourcePlaying=" + _cueSource.isPlaying + " nativeVoiceBusy=" + GameVoiceBusy);
                return false;
            }
            subtitle = entry.Subtitle;
            duration = _selectedCue.length;
            _pistonNextVoiceAt = Time.unscaledTime + duration + Mathf.Max(0f, cooldownSeconds);
            _logInfo?.Invoke("[piston-cue] started=" + _selectedCue.name);
            return true;
        }

        internal bool RequestHardPistonCue(string owner, ChaControl female, float volume,
            float cooldownSeconds, out string subtitle, out float duration)
        {
            subtitle = string.Empty;
            duration = 0f;
            if (!CanStartHardPistonCue(owner) || female == null) return false;
            // Keep the current line and its subtitle intact until it finishes.
            Stop("hard piston cue");
            if (!_cueState.TryBegin(Time.unscaledTime, false, out CueAction action)) return false;
            PistonCueEntry entry = _hardPistonCues[_hardPistonCueBag.Next()];
            _priorityPistonCueActive = true;
            _hardPistonCueActive = true;
            _ogazmCueActive = false;
            _pistonCueVolume = Mathf.Clamp01(volume);
            _activeCueOwner = owner;
            _cueFemale = female;
            _selectedCue = _cueAssets[entry.AssetIndex].Clip;
            ApplyCueActions(action);
            if (_selectedCue == null || !_cueSource.isPlaying) return false;
            subtitle = entry.Subtitle;
            duration = _selectedCue.length;
            _hardPistonNextVoiceAt = Time.unscaledTime + duration + Mathf.Max(0f, cooldownSeconds);
            _logInfo?.Invoke("[hard-piston-cue] started=" + _selectedCue.name);
            return true;
        }

        internal bool RequestOgazmCue(string owner, ChaControl female, float volume,
            float cooldownSeconds, out string subtitle, out float duration)
        {
            subtitle = string.Empty;
            duration = 0f;
            if (!CanStartOgazmCue(owner) || female == null) return false;
            Stop("ogazm cue");
            if (!_cueState.TryBegin(Time.unscaledTime, false, out CueAction action)) return false;
            PistonCueEntry entry = _ogazmCues[_ogazmCueBag.Next()];
            _priorityPistonCueActive = true;
            _hardPistonCueActive = false;
            _ogazmCueActive = true;
            _pistonCueVolume = Mathf.Clamp01(volume);
            _activeCueOwner = owner;
            _cueFemale = female;
            _selectedCue = _cueAssets[entry.AssetIndex].Clip;
            ApplyCueActions(action);
            if (_selectedCue == null || !_cueSource.isPlaying) return false;
            subtitle = entry.Subtitle;
            duration = _selectedCue.length;
            _ogazmNextVoiceAt = Time.unscaledTime + duration + Mathf.Max(0f, cooldownSeconds);
            _logInfo?.Invoke("[ogazm-cue] started=" + _selectedCue.name);
            return true;
        }
    }
}
