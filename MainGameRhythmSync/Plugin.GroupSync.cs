using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using CanonPlugins.Shared;
using BeatController = MainGameBeatSyncSpeed.Plugin;
using SpeedController = MainGameSpeedLimitBreak.Plugin;

namespace MainGameRhythmSync
{
    public sealed partial class Plugin
    {
        private const float PhaseTolerance = 0.001f;
        private static readonly FieldInfo FemalesField = AccessTools.Field(typeof(HSceneProc), "lstFemale");
        private static readonly FieldInfo MaleField = AccessTools.Field(typeof(HSceneProc), "male");
        private static readonly FieldInfo Male1Field = AccessTools.Field(typeof(HSceneProc), "male1");
        private readonly SharedRhythmClock _clock = new SharedRhythmClock();
        private readonly List<GroupActor> _actors = new List<GroupActor>();
        private readonly List<GroupActor> _nextActors = new List<GroupActor>();
        private readonly List<Animator> _items = new List<Animator>();
        private HSceneProc _scene;
        private bool _groupReady, _wasBpmActive, _pendingReset;
        private float _sharedSpeed = 1f, _nextGroupLog, _lastSpeedSpread, _lastPhaseSpread;
        private int _updateFrame = -1, _lateFrame = -1, _beatsPerCycle = 2, _positionCorrections, _unmatchedStates;
        private double _rawVideoTime = -1, _clockReadAt, _videoTime, _phaseOrigin;
        private string _song;
        private HFlag.EMode _cachedGroupMode;
        private string _cachedGroupModeName;

        private sealed class GroupActor
        {
            public string Role;
            public ChaControl Character;
            public Animator Animator;
        }

        private bool BpmRequested
        {
            get
            {
                var beat = BeatController.Instance;
                return isActiveAndEnabled && _phaseLockEnabled != null && _phaseLockEnabled.Value &&
                    BlankRoomGate.IsInBlankRoom && beat != null && beat.Enabled &&
                    beat.VideoPlaying && beat.PlaybackFresh && beat.Bpm > 0 &&
                    _scene != null && _scene.flags != null && !_scene.flags.isHSceneEnd;
            }
        }

        internal void PrepareGroup(HSceneProc scene)
        {
            if (!isActiveAndEnabled) return;
            if (_scene != scene) { ReleaseGroup(); _scene = scene; }
            BeatController.Instance?.RefreshPlaybackForSync();
            _groupReady = CollectActors(scene);
        }

        private bool CollectActors(HSceneProc scene)
        {
            if (scene == null || scene.flags == null || scene.flags.isHSceneEnd ||
                FemalesField == null || MaleField == null || Male1Field == null)
            {
                ReleaseActors();
                return false;
            }
            _nextActors.Clear();
            bool valid = true;
            var females = FemalesField.GetValue(scene) as List<ChaControl>;
            if (females == null || females.Count == 0) valid = false;
            else for (int i = 0; i < females.Count; i++)
                valid &= AddActor(females[i], i == 0 ? "F0" : i == 1 ? "F1" : "F" + i, i == 0);
            var male = MaleField.GetValue(scene) as ChaControl;
            valid &= AddActor(male, "M0", false);
            valid &= AddActor(Male1Field.GetValue(scene) as ChaControl, "M1", false);
            // In paired modes a missing primary male must not silently become a solo.
            if (_cachedGroupModeName == null || _cachedGroupMode != scene.flags.mode)
            {
                _cachedGroupMode = scene.flags.mode;
                _cachedGroupModeName = _cachedGroupMode.ToString();
            }
            string mode = _cachedGroupModeName;
            if (male == null && mode != "masturbation" && mode != "lesbian" && mode != "peeping") valid = false;

            bool changed = _actors.Count != _nextActors.Count;
            for (int i = 0; !changed && i < _actors.Count; i++)
                changed = _actors[i].Animator != _nextActors[i].Animator;
            if (changed)
            {
                ReleaseActors();
                _actors.AddRange(_nextActors);
                _clock.Reset();
                _nextGroupLog = 0f;
                // Only the known body-linked item, not facial/accessory animators.
                foreach (var actor in _actors)
                    foreach (var animator in actor.Character.GetComponentsInChildren<Animator>(true))
                        if (animator != actor.Animator && animator.name == "p_item_dildo") _items.Add(animator);
            }
            if (!valid && Time.unscaledTime >= _nextGroupLog)
            {
                _nextGroupLog = Time.unscaledTime + 2f;
                if (LoggingEnabled)
                    Logger.LogWarning("pair lookup incomplete; BPM control released for whole group");
            }
            return valid && _actors.Count > 0 && _actors[0].Role == "F0";
        }

        private bool AddActor(ChaControl character, string role, bool required)
        {
            if (character == null) return !required;
            if (character.GetComponent("IndependentActorMarker") != null) return !required;
            if (!required && !character.visibleAll) return true;
            var animator = character.animBody;
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
                return false;
            foreach (var actor in _nextActors) if (actor.Animator == animator) return true;
            GroupActor existing = null;
            for (int i = 0; i < _actors.Count; i++)
            {
                if (_actors[i].Character == character && _actors[i].Animator == animator && _actors[i].Role == role)
                {
                    existing = _actors[i];
                    break;
                }
            }
            _nextActors.Add(existing ?? new GroupActor { Role = role, Character = character, Animator = animator });
            return true;
        }

        internal void UpdateGroup(HSceneProc scene)
        {
            if (!isActiveAndEnabled || _updateFrame == Time.frameCount) return;
            _updateFrame = Time.frameCount;
            if (_scene != scene) PrepareGroup(scene);
            _groupReady = CollectActors(scene); // ChangeAnimator can replace actors/controllers in Update.
            if (!_groupReady) { SetSharedSpeed(1f); _clock.Reset(); return; }
            var master = _actors[0].Animator;
            var state = master.GetCurrentAnimatorStateInfo(0);
            bool bpm = BpmRequested && state.loop;
            foreach (var actor in _actors)
                if (actor.Animator.IsInTransition(0)) bpm = false;

            if (bpm)
            {
                var beat = BeatController.Instance;
                if (!string.Equals(_song, beat.CurrentSongPath, StringComparison.OrdinalIgnoreCase))
                {
                    _song = beat.CurrentSongPath;
                    _phaseOrigin = 0d;
                    _rawVideoTime = -1d;
                    _clock.Reset();
                }
                double raw = beat.VideoTimeSec;
                if (_rawVideoTime < 0 || Math.Abs(raw - _rawVideoTime) > 0.00001d)
                {
                    if (_rawVideoTime >= 0 && Math.Abs(raw - (_videoTime + Time.unscaledTime - _clockReadAt)) > 0.5d)
                        _clock.Reset();
                    _rawVideoTime = raw;
                    _videoTime = raw;
                    _clockReadAt = Time.unscaledTime;
                }
                _beatsPerCycle = GetBeatsPerCycle(beat);
                double cycle = 60d / beat.Bpm * _beatsPerCycle;
                double target = (_videoTime + Time.unscaledTime - _clockReadAt + _phaseOffsetSec.Value + _phaseOrigin) / cycle;
                long identity = ((long)master.GetInstanceID() << 32) ^ (uint)state.fullPathHash ^ master.runtimeAnimatorController.GetInstanceID();
                SetSharedSpeed(_clock.Step(identity, state.normalizedTime, state.length, Time.time,
                    true, target, cycle, _phaseCorrectionSec.Value));
                if (BeatController.CurrentIntensity01 >= 0f)
                {
                    float gauge = Mathf.Clamp01(BeatController.CurrentIntensity01);
                    scene.flags.speedCalc = gauge;
                    scene.flags.speedUpClac = new Vector2(gauge, gauge);
                }
            }
            else
            {
                _clock.Reset();
                _rawVideoTime = -1d;
                float speed = 1f;
                if (_actors.Count == 1 && BlankRoomGate.IsInBlankRoom && SpeedController.Instance != null)
                    speed = SpeedController.Instance.GetSoloManualSpeed(scene.flags.speedCalc);
                SetSharedSpeed(speed);
            }
            if (_wasBpmActive != bpm)
            {
                _wasBpmActive = bpm;
                if (LoggingEnabled)
                    Logger.LogInfo("pair mode=" + (bpm ? "BPM" : "manual") + " actors=" + _actors.Count + " sharedSpeed=" + _sharedSpeed);
            }
        }

        // This is the only owner of body Animator.speed during H scenes.
        private void SetSharedSpeed(float speed)
        {
            _sharedSpeed = speed;
            foreach (var actor in _actors)
                if (actor.Animator != null && !Mathf.Approximately(actor.Animator.speed, speed))
                    actor.Animator.speed = speed;
            foreach (var item in _items)
                if (item != null && !Mathf.Approximately(item.speed, speed))
                    item.speed = speed;
        }

        internal void AlignGroup(HSceneProc scene)
        {
            if (!isActiveAndEnabled || scene != _scene || !_groupReady || _lateFrame == Time.frameCount) return;
            _lateFrame = Time.frameCount;
            SetSharedSpeed(_sharedSpeed);
            var master = _actors[0].Animator;
            foreach (var actor in _actors)
                if (actor.Animator == null || actor.Animator.IsInTransition(0)) return;
            var reference = master.GetCurrentAnimatorStateInfo(0);
            if (_pendingReset && reference.loop)
            {
                foreach (var actor in _actors)
                {
                    var current = actor.Animator.GetCurrentAnimatorStateInfo(0);
                    if (current.shortNameHash != reference.shortNameHash) return;
                }
                foreach (var actor in _actors)
                {
                    var current = actor.Animator.GetCurrentAnimatorStateInfo(0);
                    SeekWithoutEvents(actor.Animator, current.fullPathHash, 0f);
                }
                var beat = BeatController.Instance;
                _phaseOrigin = -(beat?.VideoTimeSec ?? 0d) - _phaseOffsetSec.Value;
                _pendingReset = false;
                _clock.Reset();
                reference = master.GetCurrentAnimatorStateInfo(0);
                if (LoggingEnabled)
                    Logger.LogInfo("pair animation reset (all actors)");
            }
            _lastPhaseSpread = 0f;
            _lastSpeedSpread = 0f;
            _unmatchedStates = 0;
            for (int i = 1; i < _actors.Count; i++)
            {
                var animator = _actors[i].Animator;
                var state = animator.GetCurrentAnimatorStateInfo(0);
                _lastSpeedSpread = Mathf.Max(_lastSpeedSpread, Math.Abs(animator.speed - master.speed));
                if (state.shortNameHash != reference.shortNameHash || state.loop != reference.loop)
                {
                    _unmatchedStates++;
                    continue;
                }
                // Preserve the full loop index, including non-loop progress. Never wrap to zero.
                float difference = Math.Abs(state.normalizedTime - reference.normalizedTime);
                if (difference > PhaseTolerance)
                {
                    SeekWithoutEvents(animator, state.fullPathHash, reference.normalizedTime);
                    state = animator.GetCurrentAnimatorStateInfo(0);
                    _positionCorrections++;
                }
                _lastPhaseSpread = Mathf.Max(_lastPhaseSpread, Math.Abs(state.normalizedTime - reference.normalizedTime));
            }
            LogGroup(reference);
        }

        private static void SeekWithoutEvents(Animator animator, int state, float time)
        {
            bool events = animator.fireEvents;
            try
            {
                animator.fireEvents = false;
                animator.Play(state, 0, time);
                animator.Update(0f);
            }
            finally { animator.fireEvents = events; }
        }

        private void LogGroup(AnimatorStateInfo reference)
        {
            if (!LoggingEnabled || Time.unscaledTime < _nextGroupLog) return;
            _nextGroupLog = Time.unscaledTime + 2f;
            var log = new StringBuilder("pair v=0.2.0 mode=").Append(_wasBpmActive ? "BPM" : "manual")
                .Append(" sharedSpeed=").Append(_sharedSpeed.ToString("0.####"))
                .Append(" beats=").Append(_beatsPerCycle).Append(" beatError=").Append(_clock.BeatError.ToString("0.####"))
                .Append(" phaseDelta=").Append(_lastPhaseSpread.ToString("0.######"))
                .Append(" speedDelta=").Append(_lastSpeedSpread.ToString("0.######"))
                .Append(" unmatchedStates=").Append(_unmatchedStates)
                .Append(" seeks=").Append(_positionCorrections);
            foreach (var actor in _actors)
            {
                var state = actor.Animator.GetCurrentAnimatorStateInfo(0);
                log.Append(" | ").Append(actor.Role).Append('#').Append(actor.Animator.GetInstanceID())
                    .Append(" state=").Append(state.shortNameHash).Append(" t=").Append(state.normalizedTime.ToString("0.######"))
                    .Append(" speed=").Append(actor.Animator.speed.ToString("0.####"));
            }
            if (_unmatchedStates > 0 || _lastPhaseSpread > PhaseTolerance || _lastSpeedSpread > 0.00001f)
                Logger.LogWarning(log.ToString());
            else Logger.LogInfo(log.ToString());
        }

        private static int GetBeatsPerCycle(BeatController beat)
        {
            float zone = beat.ZoneTarget;
            if (zone < 0f) return 2;
            if (Mathf.Approximately(zone, beat.HighTarget)) return 1;
            return Mathf.Approximately(zone, beat.LowTarget) ? 4 : 2;
        }

        public void ResetAnimationPhase() => _pendingReset = true;
        public float CurrentCycleBpm => IsPhaseSyncActive ? (float)BeatController.Instance.Bpm / _beatsPerCycle : -1f;

        private void ReleaseActors()
        {
            SetSharedSpeed(1f);
            _actors.Clear();
            _items.Clear();
            _clock.Reset();
        }
        internal void ReleaseGroup()
        {
            ReleaseActors();
            _scene = null;
            _groupReady = _wasBpmActive = _pendingReset = false;
            _rawVideoTime = -1;
            _phaseOrigin = 0;
            _song = null;
            _updateFrame = _lateFrame = -1;
        }
        internal void ReleaseScene(HSceneProc scene)
        {
            if (_scene == scene) ReleaseGroup();
        }
    }

    [HarmonyPatch(typeof(HSceneProc), "Update")]
    internal static class SharedGroupUpdatePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(HSceneProc __instance) => Plugin.Instance?.PrepareGroup(__instance);
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(HSceneProc __instance) => Plugin.Instance?.UpdateGroup(__instance);
    }

    [HarmonyPatch(typeof(HSceneProc), "LateUpdate")]
    internal static class SharedGroupLatePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(HSceneProc __instance) => Plugin.Instance?.AlignGroup(__instance);
    }

    [HarmonyPatch(typeof(HSceneProc), "OnDestroy")]
    internal static class SharedGroupDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix(HSceneProc __instance) => Plugin.Instance?.ReleaseScene(__instance);
    }
}
