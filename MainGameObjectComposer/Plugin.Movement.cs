using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private readonly MovementLineState _movementLine = new MovementLineState();
        private readonly Dictionary<PresetEntry, Dictionary<string, MovementFieldRule>> _movementRuleCache =
            new Dictionary<PresetEntry, Dictionary<string, MovementFieldRule>>();
        private float _movementScore = 50f, _movementFromScore = 50f, _movementTargetScore = 50f;
        private float _movementTransitionElapsed = 1f, _movementWeight;
        private SimpleFileLogger _movementLogger;
        private string _movementAppliedTrace;

        private void LogMovement(string message)
        {
            if (_cfgVerboseLog == null || !_cfgVerboseLog.Value) return;
            if (_movementLogger == null)
            {
                _movementLogger = new SimpleFileLogger();
                _movementLogger.Initialize(Path.Combine(_pluginDir, "MovementControl.log"), true);
            }
            _movementLogger.Write("INFO", message);
        }

        /// <summary>Called on the Unity thread at the actual start of a spoken source line.</summary>
        public static bool TryApplyMovementLine(string sessionId, int sourceLine, int score, int speechLevel)
        {
            Plugin inst = _instance;
            if (inst == null) return false;
            if (!inst._movementLine.Accept(sessionId, sourceLine, score, speechLevel)) return true;
            if (speechLevel >= 0 && speechLevel <= 4) inst._motionIntensityLevel = speechLevel;
            if (inst._movementLine.Score >= 0 && inst._movementTargetScore != inst._movementLine.Score)
            {
                inst._movementFromScore = inst._movementScore;
                inst._movementTargetScore = inst._movementLine.Score;
                inst._movementTransitionElapsed = 0f;
            }
            if (inst._cfgVerboseLog != null && inst._cfgVerboseLog.Value) inst.LogMovement("line session=" + sessionId + " sourceLine=" + (sourceLine + 1)
                + " received=" + score + " latest=" + inst._movementLine.Score
                + " mode=" + (inst._movementLine.SpeechPriority ? "speech" : "tag")
                + " speechLevel=" + inst._movementLine.SpeechLevel
                + " speechThrough=" + (inst._movementLine.SpeechThroughLine + 1)
                + " enabled=" + (inst._activePreset != null && inst._activePreset.movementEnabled));
            return true;
        }

        private static void EnsureMovementPreset(PresetEntry preset)
        {
            if (preset.movementVersion < 1)
            {
                preset.movementVersion = 1;
                preset.movementZeroMultiplier = 0.3f;
                preset.movementFullMultiplier = 1.5f;
                preset.movementTransitionSeconds = 0.3f;
            }
            if (preset.movementTargetIds == null) preset.movementTargetIds = new List<string>();
            if (preset.movementRulesJson == null) preset.movementRulesJson = new List<string>();
        }

        private void TickMovementTransition()
        {
            float duration = 0.3f;
            if (_activePreset != null)
            {
                EnsureMovementPreset(_activePreset);
                duration = Mathf.Max(0.01f, _activePreset.movementTransitionSeconds);
            }
            _movementTransitionElapsed += Time.unscaledDeltaTime;
            _movementScore = Mathf.Lerp(_movementFromScore, _movementTargetScore,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_movementTransitionElapsed / duration)));
            bool active = _movementEditPreset == null && _activePreset != null && _activePreset.movementEnabled
                && _movementLine.Score >= 0 && !_movementLine.SpeechPriority;
            _movementWeight = Mathf.MoveTowards(_movementWeight, active ? 1f : 0f, Time.unscaledDeltaTime / duration);
        }

        private float MovementBlendFor(string id)
        {
            if (_movementEditPreset != null || _activePreset == null || !_activePreset.movementEnabled) return 0f;
            EnsureMovementPreset(_activePreset);
            return _activePreset.movementTargetIds.Contains(id) ? _movementWeight : 0f;
        }

        private Dictionary<string, MovementFieldRule> MovementRules(PresetEntry preset)
        {
            EnsureMovementPreset(preset);
            if (_movementRuleCache.TryGetValue(preset, out var rules)) return rules;
            rules = new Dictionary<string, MovementFieldRule>(StringComparer.Ordinal);
            foreach (string json in preset.movementRulesJson)
            {
                try
                {
                    var rule = JsonUtility.FromJson<MovementFieldRule>(json);
                    if (rule != null && !string.IsNullOrEmpty(rule.objectId) && !string.IsNullOrEmpty(rule.field))
                        rules[rule.objectId + "/" + rule.field] = rule;
                }
                catch (Exception ex) { LogWarn("movement rule skipped: " + ex.Message); }
            }
            _movementRuleCache[preset] = rules;
            return rules;
        }

        private MovementFieldRule FindMovementField(PresetEntry preset, string id, string field)
        {
            MovementRules(preset).TryGetValue(id + "/" + field, out var result);
            return result;
        }

        private void SaveMovementField(PresetEntry preset, MovementFieldRule rule, bool save = true)
        {
            var rules = MovementRules(preset);
            rules[rule.objectId + "/" + rule.field] = rule;
            preset.movementRulesJson.Clear();
            foreach (var value in rules.Values) preset.movementRulesJson.Add(JsonUtility.ToJson(value));
            if (save) SaveMotionPresetMutation();
        }

        private float EvaluateMovementField(MovementField field, MovementFieldRule rule, ManagedObjectData l1,
            ManagedObjectData l2, float motion)
        {
            float score = Mathf.Clamp01(_movementScore / 100f);
            float Slot(ManagedObjectData original, int slot)
            {
                float baseline = field.Read(original);
                float zero = MovementFieldRule.Endpoint(rule, slot, false, baseline, field.Radius,
                    _activePreset.movementZeroMultiplier, _activePreset.movementFullMultiplier);
                float full = MovementFieldRule.Endpoint(rule, slot, true, baseline, field.Radius,
                    _activePreset.movementZeroMultiplier, _activePreset.movementFullMultiplier);
                return field.Blend(zero, full, score);
            }
            return field.Blend(Slot(l1, 1), Slot(l2, _activePreset.useLoopSplit ? 2 : 1), motion);
        }

        private void ApplyMovementPose(RuntimeObjectRef rr, ManagedObjectData l1, ManagedObjectData l2, float motion)
        {
            float weight = MovementBlendFor(rr.Data.id);
            if (weight <= 0f || !rr.Data.isRotationObject) return;
            foreach (MovementField field in MovementField.All)
            {
                if (field.Radius) continue; // Applied after legacy radius multipliers, in GetEffectiveOrbitRadii.
                MovementFieldRule rule = FindMovementField(_activePreset, rr.Data.id, field.Key);
                if (rule == null || !rule.enabled) continue;
                float value = EvaluateMovementField(field, rule, l1, l2, motion);
                field.Write(rr.Data, field.Blend(field.Read(rr.Data), value, weight));
            }
        }

        private void ApplyMovementRadii(ManagedObjectData data, ref float rx, ref float rz)
        {
            float weight = MovementBlendFor(data.id);
            if (weight <= 0f || !data.isRotationObject || _activeL1ById == null
                || !_activeL1ById.TryGetValue(data.id, out var l1)) return;
            ManagedObjectData l2 = l1;
            float motion = 0f;
            if (_activePreset.useLoopSplit && _activeL2ById != null && _activeL2ById.TryGetValue(data.id, out var second))
            { l2 = second; motion = _runtime.Flags != null ? Mathf.Clamp01(_runtime.Flags.motion) : 0f; }
            foreach (MovementField field in MovementField.All)
            {
                if (!field.Radius) continue;
                var rule = FindMovementField(_activePreset, data.id, field.Key);
                if (rule != null && !rule.enabled) continue;
                float value = EvaluateMovementField(field, rule, l1, l2, motion);
                if (field.Key == "orbitRx") rx = Mathf.Max(0.001f, Mathf.Lerp(rx, value, weight));
                else rz = Mathf.Max(0.001f, Mathf.Lerp(rz, value, weight));
            }
        }

        private void LogMovementAppliedIfReady()
        {
            if (_cfgVerboseLog == null || !_cfgVerboseLog.Value) return;
            if (_activePreset == null || !_activePreset.movementEnabled || _movementEditPreset != null) return;
            float duration = Mathf.Max(0.01f, _activePreset.movementTransitionSeconds);
            if (_movementTransitionElapsed < duration || (_movementWeight > 0f && _movementWeight < 1f)) return;
            string trace = _movementLine.Session + "/" + _movementLine.SourceLine + "/"
                + _activePreset.key + "/" + _movementWeight + "/" + _movementLine.SpeechLevel;
            if (trace == _movementAppliedTrace) return;
            _movementAppliedTrace = trace;
            foreach (string id in _activePreset.movementTargetIds)
            {
                RuntimeObjectRef rr = FindRuntimeById(id);
                if (rr == null || rr.GameObject == null) continue;
                GetEffectiveOrbitRadii(rr.Data, out float rx, out float rz);
                LogMovement("applied sourceLine=" + (_movementLine.SourceLine + 1) + " object=" + id
                    + " tagWeight=" + _movementWeight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    + " score=" + _movementScore.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    + " Rx=" + rx.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                    + " Rz=" + rz.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                    + " position=" + rr.GameObject.transform.localPosition);
            }
        }
    }
}
