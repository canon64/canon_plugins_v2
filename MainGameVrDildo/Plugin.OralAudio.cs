using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        [Serializable] private sealed class OralSelection
        {
            public OralItem[] items = null;
            public OralClipEntry[] clips = null;
        }
        [Serializable] private sealed class OralItem { public string file = ""; public string text = ""; }
        [Serializable] private sealed class OralClipEntry { public string id = ""; public string caption = ""; }

        private readonly List<AudioClip> _oralVoices = new List<AudioClip>();
        private readonly List<string> _oralCaptions = new List<string>();
        private readonly List<AudioClip> _oralBreaths = new List<AudioClip>();
        private AudioSource _oralSource;
        private Harmony _oralHarmony;
        private Coroutine _oralLoad;
        private bool _oralLoaded;
        private bool _oralPlaying;
        private float _nextOralVoice;
        private int _lastOralVoice = -1;
        private int _lastOralBreath = -1;
        private string _oralAudioStatus = "未読込";
        private static readonly string[] OralBreathIds = {
            "h_ko_00_00_025", "h_ko_00_00_026", "h_ko_00_00_027", "h_ko_00_00_028",
            "h_ko_00_00_033", "h_ko_00_00_034", "h_ko_00_00_035", "h_ko_00_00_036",
            "h_ko_00_00_045", "h_ko_00_00_046", "h_ko_00_00_047", "h_ko_00_00_048"
        };

        private string OralAudioDirectory => Path.GetDirectoryName(OralSelectionFile) ?? Paths.PluginPath;
        private string OralSelectionFile => string.IsNullOrWhiteSpace(_settings.OralSelectionPath)
            ? Path.Combine(Paths.PluginPath, "canon_plugins", "MainGameVoiceFaceEventBridge", "ImpactCueAudio", "Oral", "manifest.json")
            : _settings.OralSelectionPath;

        private void InstallOralVoiceHooks()
        {
            try
            {
                _oralHarmony = new Harmony(Guid + ".oral");
                PatchOral(FindHVoiceMethod("Proc", 2), nameof(OralHVoiceProcPrefix), "HVoiceCtrl.Proc");
                PatchOral(FindHVoiceMethod("PlayVoice", 7), nameof(OralHVoicePlayPrefix), "HVoiceCtrl.PlayVoice");
                PatchOral(typeof(Manager.Voice), "Play", nameof(OralManagerVoicePrefix), typeof(Manager.Voice.Loader));
                PatchOral(typeof(Manager.Voice), "OncePlay", nameof(OralManagerVoicePrefix), typeof(Manager.Voice.Loader));
                PatchOral(typeof(Manager.Voice), "OncePlayChara", nameof(OralManagerVoicePrefix), typeof(Manager.Voice.Loader));
            }
            catch (Exception ex) { Logger.LogError("Oral voice hooks failed: " + ex); }
        }

        private void PatchOral(Type type, string method, string prefix, params Type[] args)
        {
            MethodInfo target = args.Length == 0 ? AccessTools.Method(type, method) : AccessTools.Method(type, method, args);
            PatchOral(target, prefix, type.Name + "." + method);
        }

        private static MethodInfo FindHVoiceMethod(string name, int parameterCount)
        {
            foreach (var method in typeof(HVoiceCtrl).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.Name == name && method.ReturnType == typeof(bool) &&
                    method.GetParameters().Length == parameterCount) return method;
            return null;
        }

        private void PatchOral(MethodInfo target, string prefix, string label)
        {
            if (target == null) { Logger.LogWarning("Oral voice hook missing: " + label); return; }
            _oralHarmony.Patch(target, prefix: new HarmonyMethod(typeof(Plugin), prefix));
        }

        private void RemoveOralVoiceHooks()
        {
            if (_oralHarmony != null) _oralHarmony.UnpatchSelf();
            _oralHarmony = null;
        }

        private bool BlockingNativeOralVoice => _oralContact && _settings != null && _settings.Enabled &&
            _settings.OralAudioEnabled && _scene != null && _female != null;

        private static bool OralHVoiceProcPrefix(HVoiceCtrl __instance, ref bool __result)
        {
            var p = Instance;
            if (p == null || !p.BlockingNativeOralVoice || p._scene.voice != __instance) return true;
            __result = true;
            return false;
        }

        private static bool OralHVoicePlayPrefix(HVoiceCtrl __instance, ref bool __result)
        {
            var p = Instance;
            if (p == null || !p.BlockingNativeOralVoice || p._scene.voice != __instance) return true;
            __result = false;
            return false;
        }

        private static bool OralManagerVoicePrefix(Manager.Voice.Loader loader, ref AudioSource __result)
        {
            var p = Instance;
            if (p == null || !p.BlockingNativeOralVoice || loader == null || p._scene.flags == null) return true;
            bool target = false;
            var trans = loader.voiceTrans;
            if (trans != null)
            {
                var mouths = p._scene.flags.transVoiceMouth;
                target = mouths != null && mouths.Length > 0 && mouths[0] != null && mouths[0] == trans;
            }
            else if (p._scene.flags.lstHeroine != null)
                foreach (var heroine in p._scene.flags.lstHeroine)
                    if (heroine != null && heroine.voiceNo == loader.no &&
                        p._scene.flags.lstHeroine.IndexOf(heroine) == 0) { target = true; break; }
            if (!target) return true;
            __result = null;
            return false;
        }

        private void UpdateOralContact()
        {
            if (!_oralContact || !_settings.OralAudioEnabled) { StopOralAudio(); return; }
            if (_oralPlaying) return;
            _oralPlaying = true;
            _nextOralVoice = Time.unscaledTime + 2f;
            StopNativeOralVoice();
            if (_oralSource == null && _mouthEntry != null)
            {
                _oralSource = _mouthEntry.gameObject.AddComponent<AudioSource>();
                _oralSource.playOnAwake = false;
                _oralSource.spatialBlend = 0.8f;
                _oralSource.dopplerLevel = 0f;
            }
            if (!_oralLoaded && _oralLoad == null) ReloadOralAudio();
        }

        private void StopNativeOralVoice()
        {
            if (_scene == null || _scene.flags == null || _scene.flags.transVoiceMouth == null) return;
            var mouths = _scene.flags.transVoiceMouth;
            if (mouths.Length > 0 && mouths[0] != null) Manager.Voice.Stop(mouths[0]);
        }

        private void StopOralAudio()
        {
            if (!_oralPlaying) return;
            _oralPlaying = false;
            if (_oralSource != null) _oralSource.Stop();
            ClearOralSubtitleFallback();
        }

        private void UpdateOralAudio()
        {
            if (!_oralPlaying || !_oralContact || !_settings.OralAudioEnabled || _oralSource == null) return;
            _oralSource.volume = _settings.OralAudioVolume;
            if (_oralSource.isPlaying) return;
            bool voice = _oralVoices.Count > 0 && Time.unscaledTime >= _nextOralVoice;
            var pool = voice ? _oralVoices : _oralBreaths;
            if (pool.Count == 0) return;
            int last = voice ? _lastOralVoice : _lastOralBreath;
            int index = UnityEngine.Random.Range(0, pool.Count);
            if (pool.Count > 1 && index == last) index = (index + 1) % pool.Count;
            if (voice) _lastOralVoice = index; else _lastOralBreath = index;
            _oralSource.clip = pool[index];
            _oralSource.Play();
            if (voice && _oralSource.isPlaying)
                ShowOralSubtitle(index, _oralSource.clip.length);
            if (voice) _nextOralVoice = Time.unscaledTime + _oralSource.clip.length +
                _settings.OralVoiceIntervalSeconds;
        }

        private void ReloadOralAudio()
        {
            if (_oralLoad != null) StopCoroutine(_oralLoad);
            _oralLoaded = false;
            _oralLoad = StartCoroutine(LoadOralAudio());
        }

        private IEnumerator LoadOralAudio()
        {
            if (_oralSource != null) _oralSource.Stop();
            foreach (var clip in _oralVoices) if (clip != null) Destroy(clip);
            foreach (var clip in _oralBreaths) if (clip != null) Destroy(clip);
            _oralVoices.Clear(); _oralCaptions.Clear(); _oralBreaths.Clear();
            ClearOralSubtitleFallback();
            var directory = OralAudioDirectory;
            var entries = new List<OralItem>();
            try
            {
                if (File.Exists(OralSelectionFile))
                {
                    var selected = JsonUtility.FromJson<OralSelection>(File.ReadAllText(OralSelectionFile));
                    if (selected != null && selected.items != null)
                    {
                        foreach (var entry in selected.items)
                            if (entry != null && IsSafeOralFile(entry.file)) entries.Add(entry);
                    }
                    else if (selected != null && selected.clips != null)
                    {
                        foreach (var entry in selected.clips)
                            if (entry != null && entry.id != null &&
                                System.Text.RegularExpressions.Regex.IsMatch(entry.id, @"^h_hh_00_\d{2}_\d{3}(_les)?$"))
                                entries.Add(new OralItem { file = entry.id + ".wav", text = entry.caption });
                    }
                }
            }
            catch (Exception ex) { Logger.LogWarning("Oral selection read failed: " + ex.Message); }
            foreach (var entry in entries)
            {
                var clip = LoadOralFile(directory, entry.file);
                if (clip != null)
                {
                    _oralVoices.Add(clip);
                    _oralCaptions.Add(entry.text ?? "");
                }
                yield return null;
            }
            foreach (var id in OralBreathIds)
            {
                var clip = LoadOralWav(directory, id);
                if (clip != null) _oralBreaths.Add(clip);
                yield return null;
            }
            _oralAudioStatus = "選択音声 " + _oralVoices.Count + "本 / フェラ呼吸 " + _oralBreaths.Count + "本";
            Logger.LogInfo("Oral audio: " + _oralAudioStatus);
            _oralLoaded = true;
            _oralLoad = null;
        }

        private AudioClip LoadOralWav(string directory, string id)
        {
            return LoadOralFile(directory, id + ".wav");
        }

        private static bool IsSafeOralFile(string file)
        {
            return !string.IsNullOrWhiteSpace(file) && Path.GetFileName(file) == file &&
                string.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase);
        }

        private AudioClip LoadOralFile(string directory, string file)
        {
            if (!IsSafeOralFile(file)) return null;
            string path = Path.Combine(directory, file);
            if (!File.Exists(path)) { Logger.LogWarning("Oral WAV missing: " + path); return null; }
            try { return PcmWav.Load(path); }
            catch (Exception ex) { Logger.LogWarning("Oral WAV invalid: " + file + " " + ex.Message); return null; }
        }

        private static class PcmWav
        {
            public static AudioClip Load(string path)
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not RIFF");
                    reader.ReadUInt32();
                    if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not WAVE");
                    ushort format = 0, channels = 0, bits = 0;
                    int rate = 0;
                    byte[] data = null;
                    while (stream.Position + 8 <= stream.Length)
                    {
                        string chunk = new string(reader.ReadChars(4));
                        uint length = reader.ReadUInt32();
                        long end = stream.Position + length;
                        if (end > stream.Length) throw new InvalidDataException("Truncated chunk");
                        if (chunk == "fmt ")
                        {
                            format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                            reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
                        }
                        else if (chunk == "data") data = reader.ReadBytes((int)length);
                        stream.Position = end + (length & 1u);
                    }
                    if (format != 1 || bits != 16 || channels < 1 || channels > 2 || rate < 8000 || data == null)
                        throw new InvalidDataException("Requires PCM16 mono/stereo WAV");
                    int frames = data.Length / (2 * channels);
                    var samples = new float[frames * channels];
                    for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(data, i * 2) / 32768f;
                    var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), frames, channels, rate, false);
                    clip.SetData(samples, 0);
                    return clip;
                }
            }
        }
    }
}
