using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private Harmony _gameAudioHarmony;
        private bool _gameAudioSuppressed;
        private bool _independentVoiceTouch;
        private readonly TouchVoiceRequests _touchVoiceRequests = new TouchVoiceRequests();
        private readonly bool[] _touchVoiceSequence = new bool[2];
        private readonly Dictionary<AudioSource, bool> _gameAudioMuted = new Dictionary<AudioSource, bool>();
        private readonly HashSet<AudioSource> _touchSounds = new HashSet<AudioSource>();
        private int _touchSoundDepth;
        private int _gameAudioCreated;
        private readonly List<AudioSource> _deadGameAudio = new List<AudioSource>();

        private void InitGameAudio()
        {
            _gameAudioHarmony = new Harmony(Guid + ".game-audio");
            _gameAudioHarmony.Patch(AccessTools.Method(typeof(Manager.Voice), "Play", new[] { typeof(Manager.Voice.Loader) }),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(MuteIndependentNativeVoice)));
            foreach (string name in new[] { "OnCollision", "HitReactionPlay", "ClickAction", "DragAction" })
                _gameAudioHarmony.Patch(AccessTools.Method(typeof(HandCtrl), name), transpiler: new HarmonyMethod(typeof(Plugin), nameof(TrackTouchWrites)));
            _gameAudioHarmony.Patch(AccessTools.Method(typeof(HandCtrl), "PlaySE"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeginTouchSound)), finalizer: new HarmonyMethod(typeof(Plugin), nameof(EndTouchSound)));
            foreach (string name in new[] { "VoiceProc", "ShortBreathProc" })
                _gameAudioHarmony.Patch(AccessTools.Method(typeof(HVoiceCtrl), name),
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(AdmitGameVoice)), postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterGameVoice)));
            _gameAudioHarmony.Patch(AccessTools.Method(typeof(HVoiceCtrl), "BreathProc"), prefix: new HarmonyMethod(typeof(Plugin), nameof(AdmitAutomaticBreath)));
            _gameAudioHarmony.Patch(AccessTools.Method(typeof(Manager.Sound), "Create"), postfix: new HarmonyMethod(typeof(Plugin), nameof(CreatedGameSound)));
            _gameAudioHarmony.Patch(AccessTools.Method(typeof(Manager.Sound), "CreateCache", new[] { typeof(Manager.Sound.Type), typeof(string), typeof(string), typeof(string) }),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(CreatedGameSound)));
        }

        // Record the actual writes, including repeated assignments of the same ID.
        // Comparing snapshots around HandCtrl would miss those touch requests.
        private static IEnumerable<CodeInstruction> TrackTouchWrites(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions) {
                if (instruction.opcode == OpCodes.Stelem_I4) {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Plugin), nameof(StoreTouchRequest));
                }
                yield return instruction;
            }
        }
        private static void StoreTouchRequest(int[] array, int index, int value)
        {
            array[index] = value;
            var p = Instance;
            var voice = p?._hSceneProc?.flags?.voice;
            if (voice == null) return;
            if (ReferenceEquals(array, voice.playVoices)) p._touchVoiceRequests.Record(index, false, value, Time.frameCount);
            else if (ReferenceEquals(array, voice.playShorts)) p._touchVoiceRequests.Record(index, true, value, Time.frameCount);
        }
        private static bool AdmitGameVoice(HVoiceCtrl __instance, int _main, System.Reflection.MethodBase __originalMethod, ref bool __result, out bool __state)
        {
            __state = false;
            var p = Instance;
            if (p == null || __instance != p._hSceneProc?.voice || _main < 0 || _main > 1) return true;
            bool shortVoice = __originalMethod.Name == "ShortBreathProc";
            var requests = shortVoice ? __instance.flags.voice.playShorts : __instance.flags.voice.playVoices;
            __state = p._touchVoiceRequests.Matches(_main, shortVoice, requests[_main], Time.frameCount);
            if (p._gameAudioSuppressed && p.IsIndependentTarget) {
                __state |= !shortVoice && p._touchVoiceSequence[_main] && __instance.nowVoices[_main].link.isLilnk
                    && __instance.nowVoices[_main].link.kind == HVoiceCtrl.VoiceKind.voice && requests[_main] < 0;
                p._independentVoiceTouch = __state;
                return true; // Preserve native state transitions and face updates.
            }
            if (__state || !p._gameAudioSuppressed) return true;
            if (!shortVoice && p._touchVoiceSequence[_main] && __instance.nowVoices[_main].link.isLilnk
                && __instance.nowVoices[_main].link.kind == HVoiceCtrl.VoiceKind.voice) {
                // Only the continuation is allowed, never a queued automatic line.
                requests[_main] = -1;
                __state = true;
                return true;
            }
            requests[_main] = -1;
            __result = false; return false;
        }
        private static void AfterGameVoice(HVoiceCtrl __instance, int _main, System.Reflection.MethodBase __originalMethod, bool __result, bool __state)
        {
            var p = Instance;
            if (p == null || __instance != p._hSceneProc?.voice || _main < 0 || _main > 1 || !__result) return;
            // Successful native playback has already consumed playVoices/playShorts.
            // During suppression only touch requests/links can reach here.
            p._touchVoiceSequence[_main] = __state;
            p._touchVoiceRequests.Clear(_main, __originalMethod.Name == "ShortBreathProc");
        }
        private static bool AdmitAutomaticBreath(HVoiceCtrl __instance, ref bool __result)
        {
            if (Instance == null || !Instance._gameAudioSuppressed || __instance != Instance._hSceneProc?.voice) return true;
            if (Instance.IsIndependentTarget) { Instance._independentVoiceTouch = false; return true; }
            __result = false; return false;
        }
        private static void MuteIndependentNativeVoice(Manager.Voice.Loader loader, AudioSource __result) {
            var p = Instance;
            if (p == null || !p._gameAudioSuppressed || !p.IsIndependentTarget || __result == null || loader == null) return;
            var mouths = p._hSceneProc?.flags?.transVoiceMouth;
            if (mouths == null || Array.IndexOf(mouths, loader.voiceTrans) < 0) return;
            if (!p._independentVoiceTouch) p.MuteGameSource(__result);
        }
        private static void BeginTouchSound() { if (Instance != null) Instance._touchSoundDepth++; }
        private static Exception EndTouchSound(Exception __exception)
        {
            if (Instance != null) Instance._touchSoundDepth = Math.Max(0, Instance._touchSoundDepth - 1);
            return __exception;
        }
        private static void CreatedGameSound(Manager.Sound.Type type, AudioSource __result)
        {
            var p = Instance;
            if (p == null || __result == null) return;
            if (p._touchSoundDepth > 0) { p._touchSounds.RemoveWhere(s => s == null); p._touchSounds.Add(__result); return; }
            if (p._gameAudioSuppressed && type != Manager.Sound.Type.SystemSE) p.MuteGameSource(__result);
        }
        private void MuteGameSource(AudioSource source)
        {
            if (source == null || source == _music || _touchSounds.Contains(source)) return;
            if ((++_gameAudioCreated & 63) == 0) {
                _deadGameAudio.Clear();
                foreach (var pair in _gameAudioMuted) if (pair.Key == null) _deadGameAudio.Add(pair.Key);
                foreach (var dead in _deadGameAudio) _gameAudioMuted.Remove(dead);
                _deadGameAudio.Clear();
            }
            if (!_gameAudioMuted.ContainsKey(source)) _gameAudioMuted.Add(source, source.mute);
            source.mute = true;
        }
        private void SuppressGameAudio()
        {
            // Only silence the H participant when that participant is dancing.
            // An independently spawned dancer must not alter the ongoing H audio.
            if (IsIndependentTarget) { RestoreGameAudio(); return; }
            if (_gameAudioSuppressed) return;
            _gameAudioSuppressed = true;
            _touchSounds.RemoveWhere(s => s == null);
            if (Manager.Sound.initialized)
            {
                foreach (Manager.Sound.Type type in Enum.GetValues(typeof(Manager.Sound.Type)))
                    if (type != Manager.Sound.Type.SystemSE)
                        foreach (var source in Manager.Sound.ToPlaying(type)) MuteGameSource(source);
                // Cached sources are parented outside ToPlaying's category roots.
                var caches = AccessTools.Property(typeof(Manager.Sound), "_audioCacheTable")?.GetValue(null, null)
                    as Dictionary<int, Dictionary<string, AudioSource>>;
                if (caches != null)
                    foreach (var category in caches)
                        if (category.Key != (int)Manager.Sound.Type.SystemSE)
                            foreach (var source in category.Value.Values) MuteGameSource(source);
            }
            var voice = _hSceneProc?.voice;
            if (IsIndependentTarget && voice != null && Manager.Voice.initialized) {
                // One bounded scan of native voice roots at acquisition, not every frame.
                var roots = AccessTools.Property(typeof(Manager.Voice), "_transTable")?.GetValue(null, null) as Dictionary<int, Transform>;
                var mouths = _hSceneProc.flags.transVoiceMouth;
                if (roots != null) foreach (var root in roots.Values) {
                    if (root == null) continue;
                    foreach (var source in root.GetComponentsInChildren<AudioSource>(true)) {
                        var component = source.GetComponent<Voice_Component>();
                        int index = component == null ? -1 : Array.IndexOf(mouths, component.voiceTrans);
                        if (index >= 0 && (index >= 2 || !_touchVoiceSequence[index] || voice.nowVoices[index].state == HVoiceCtrl.VoiceKind.breath)) MuteGameSource(source);
                    }
                }
            }
            if (!IsIndependentTarget && voice != null && Manager.Voice.initialized)
                for (int i = 0; i < voice.voiceTrans.Length; i++)
                    if (i >= 2 || !_touchVoiceSequence[i] || voice.nowVoices[i].state == HVoiceCtrl.VoiceKind.breath) Manager.Voice.Stop(voice.voiceTrans[i]);
        }
        private void RestoreGameAudio()
        {
            _gameAudioSuppressed = false; _independentVoiceTouch = false;
            foreach (var pair in _gameAudioMuted) if (pair.Key != null) pair.Key.mute = pair.Value;
            _gameAudioMuted.Clear();
            _touchVoiceRequests.Reset();
            Array.Clear(_touchVoiceSequence, 0, _touchVoiceSequence.Length);
        }
    }
}
