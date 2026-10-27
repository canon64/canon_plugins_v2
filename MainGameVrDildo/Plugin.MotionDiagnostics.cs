using System;
using System.Globalization;
using CanonPlugins.Shared;
using HarmonyLib;
using UnityEngine;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private MotionTraceLog _motionTrace;
        private float _nextMotionTrace;
        private int _traceAibuCalls, _traceGaugeCalls, _traceAibuFrame = -1, _traceApplyFrame = -1, _traceGaugeFrame = -1;
        private float _traceNativeMotion, _traceGaugeMotion, _traceAppliedMotion, _traceDisplayedGauge;
        private float _traceTravel, _traceSpeed, _traceDepth, _tracePath;
        private int _traceTravelFrame = -1;
        private float _traceOldSlow;
        private string _tracePause = "not sampled";
        private bool _traceInventoryDone;
        private float _traceInventoryAt;

        private static string TraceNumber(float value) => value.ToString("F4", CultureInfo.InvariantCulture);
        private static string TraceVector(Vector3 value) => "(" + TraceNumber(value.x) + ","
            + TraceNumber(value.y) + "," + TraceNumber(value.z) + ")";
        private bool MotionTraceEnabled => _motionTrace != null && _motionTrace.Enabled;

        private void TraceVibeEvent(string stage, string detail)
        {
            if (MotionTraceEnabled)
                _motionTrace.Write(stage, Time.frameCount, Time.realtimeSinceStartup, detail);
        }

        private void TraceAibuEnter()
        {
            if (!MotionTraceEnabled) return;
            _traceAibuCalls++;
            _traceAibuFrame = Time.frameCount;
            _traceNativeMotion = _scene != null && _scene.flags != null ? _scene.flags.motion : -1f;
        }

        private void TraceWheelInput()
        {
            if (!MotionTraceEnabled) return;
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.0001f)
                TraceVibeEvent("WHEEL_INPUT", "stage=Update wheel=" + TraceNumber(wheel)
                    + " flagsSpeed=" + TraceNumber(_scene?.flags != null ? _scene.flags.speed : -1f)
                    + " flagsMotion=" + TraceNumber(_scene?.flags != null ? _scene.flags.motion : -1f));
        }

        private void TraceAibuApplied(float gauge, float applied)
        {
            if (!MotionTraceEnabled) return;
            _traceApplyFrame = Time.frameCount;
            _traceGaugeMotion = gauge * _settings.MotionGaugeAnimationStrengthWeight;
            _traceAppliedMotion = applied;
        }

        private void TraceMovementSample(float travel, float speed, float depth, float path)
        {
            if (!MotionTraceEnabled) return;
            _tracePause = "tracking";
            _traceTravel = travel; _traceSpeed = speed; _traceDepth = depth; _tracePath = path;
            _traceTravelFrame = Time.frameCount;
            if (_traceOldSlow != _motionSlowActive)
                TraceVibeEvent("SLOW_CHANGE", "active=" + _motionSlowActive + " speedMps="
                    + TraceNumber(speed) + " confirmedSeconds=" + TraceNumber(_slowSeconds));
            _traceOldSlow = _motionSlowActive;
        }

        private void TracePause(string reason)
        {
            if (!MotionTraceEnabled) return;
            if (_tracePause != reason) TraceVibeEvent("PAUSE", "reason=" + reason);
            _tracePause = reason;
        }

        private static string TraceAnimator(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return "animator=missing";
            string motion = "absent", speed = "absent";
            foreach (var parameter in animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Float) continue;
                if (parameter.name == "motion") motion = TraceNumber(animator.GetFloat(parameter.nameHash));
                if (parameter.name == "speedBody") speed = TraceNumber(animator.GetFloat(parameter.nameHash));
            }
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            return "animMotion=" + motion + " animSpeedBody=" + speed
                + " animatorSpeed=" + TraceNumber(animator.speed) + " stateHash=" + state.fullPathHash
                + " normalizedTime=" + TraceNumber(state.normalizedTime);
        }

        private void TickVibeDiagnostics()
        {
            if (!MotionTraceEnabled || Time.unscaledTime < _nextMotionTrace) return;
            _nextMotionTrace = Time.unscaledTime + (_deepReturning ? 0.05f : 0.25f);
            try
            {
                if (!_traceInventoryDone && Time.unscaledTime >= _traceInventoryAt)
                {
                    TraceRuntimeInventory();
                    _traceInventoryDone = true;
                }
                HFlag flags = _scene != null ? _scene.flags : null;
                string block = _settings == null ? "settings_missing" : !_settings.Enabled ? "disabled"
                    : !_settings.MotionGaugeEnabled ? "gauge_disabled" : _model == null ? "model_missing"
                    : flags == null ? "scene_missing" : flags.mode != HFlag.EMode.aibu ? "not_aibu" : "none";
                TraceVibeEvent("STATE", "block=" + block + " pause=" + _tracePause
                    + " mode=" + (flags != null ? flags.mode.ToString() : "none")
                    + " poseId=" + (flags?.nowAnimationInfo != null ? flags.nowAnimationInfo.id : -1)
                    + " pose=" + flags?.nowAnimationInfo?.nameAnimation
                    + " model=" + (_model != null) + " engaged=" + _engaged + " target=" + _selectedTarget
                    + " axis=" + TraceVector(_selectedAxis) + " grip=" + (_grabController != null)
                    + " pushDirection=" + TraceVector(_motionPushDirection)
                    + " modelWorld=" + (_model != null ? TraceVector(_model.transform.position) : "missing")
                    + " gizmoDragging=" + IsDesktopGizmoDragging() + " requireGrip=" + _settings.AibuRequireGrip
                    + " travelFrame=" + _traceTravelFrame + " signedTravelM=" + TraceNumber(_traceTravel)
                    + " speedMps=" + TraceNumber(_traceSpeed) + " depthM=" + TraceNumber(_traceDepth)
                    + " passageM=" + TraceNumber(_tracePath)
                    + " gaugeCm=" + TraceNumber(_motionGaugeCm) + " fullCm=" + TraceNumber(_settings.MotionGaugeFullCm)
                    + " decayCmps=" + TraceNumber(_settings.MotionGaugeDecayCmPerSecond)
                    + " shownGauge01=" + TraceNumber(_traceDisplayedGauge)
                    + " strengthWeight=" + TraceNumber(_settings.MotionGaugeAnimationStrengthWeight)
                    + " wheelInput=" + TraceNumber(Input.GetAxis("Mouse ScrollWheel"))
                    + " nativeSpeed=" + TraceNumber(flags != null ? flags.speed : -1f)
                    + " flagsMotion=" + TraceNumber(flags != null ? flags.motion : -1f)
                    + " nativeMotionAtHook=" + TraceNumber(_traceNativeMotion)
                    + " gaugeMotionAtHook=" + TraceNumber(_traceGaugeMotion)
                    + " appliedMotionAtHook=" + TraceNumber(_traceAppliedMotion)
                    + " aibuCalls=" + _traceAibuCalls + " aibuFrame=" + _traceAibuFrame
                    + " appliedFrame=" + _traceApplyFrame + " gaugeDrawCalls=" + _traceGaugeCalls
                    + " gaugeDrawFrame=" + _traceGaugeFrame
                    + " " + TraceAnimator(_female != null ? _female.animBody : null)
                    + " slow=" + _motionSlowActive + " slowSeconds=" + TraceNumber(_slowSeconds)
                    + " slowMinMps=" + TraceNumber(_settings.AibuMinMotionSpeed)
                    + " slowMaxMps=" + TraceNumber(_settings.SlowMaxSpeed)
                    + " slowConfirm=" + TraceNumber(_settings.SlowConfirmSeconds)
                    + " deepPulse=" + TraceNumber(_deepPulse) + " deepReturning=" + _deepReturning
                    + " deepReturnSeconds=" + TraceNumber(_settings.DeepReturnSeconds)
                    + " deepReactionM=" + TraceNumber(_settings.DeepReactionMetres)
                    + " deepReturnSpeedMps=" + TraceNumber(_settings.DeepReactionMetres / _settings.DeepReturnSeconds)
                    + " bodyM=" + TraceNumber(_motionBodyOffset) + " upperM=" + TraceNumber(_motionUpperOffset)
                    + " bodyEnableWeight=" + TraceNumber(_bodyEnableWeight)
                    + " bodyReactionEnabled=" + _settings.BodyReactionEnabled
                    + " cloneMin=" + TraceNumber(_settings.CloneAnimationMinMultiplier)
                    + " cloneMax=" + TraceNumber(_settings.CloneAnimationMaxMultiplier)
                    + " cloneWeight=" + TraceNumber(_settings.CloneAnimationGaugeWeight)
                    + " eyesStatus=" + _ogazmEyeStatus + " eyesRenderCalls=" + _ogazmEyeRenderCalls
                    + " eyesAppliedFrame=" + _ogazmEyeLastAppliedFrame
                    + " irisAppliedCount=" + _ogazmIrisAppliedCount
                    + " eyesGauge01=" + TraceNumber(_ogazmEyeGauge)
                    + " eyesWeight=" + TraceNumber(_ogazmEyeWeight)
                    + " eyesEnabled=" + _settings.OgazmEyesEnabled
                    + " eyesStart01=" + TraceNumber(_settings.OgazmEyeStartGaugeFraction)
                    + " eyesFull01=" + TraceNumber(_settings.OgazmEyeFullGaugeFraction)
                    + " waistWorld=" + (_deep != null ? TraceVector(_deep.position) : "missing"));
            }
            catch (Exception ex) { TraceVibeEvent("DIAGNOSTIC_ERROR", ex.ToString()); }
        }

        private void TraceRuntimeInventory()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (name == "Assembly-CSharp" || name == "MainGameVrDildo" || name == "MainGameObjectComposer"
                    || name == "MainGirlHipHijack" || name == "MainGameFreeHMasturbationMenu")
                    TraceVibeEvent("LOADED_ASSEMBLY", MotionTraceLog.DescribeAssembly(assembly));
            }
            foreach (var method in new[] { AccessTools.Method(typeof(HAibu), "Proc"),
                AccessTools.Method(typeof(HSprite), "Update"),
                AccessTools.Method(typeof(HActionBase), "SetDefaultAnimatorFloat") })
            {
                if (method == null) continue;
                var patches = Harmony.GetPatchInfo(method);
                string target = method.DeclaringType?.FullName + "." + method.Name;
                if (patches == null) { TraceVibeEvent("PATCH_LIST", "target=" + target + " none"); continue; }
                TracePatches(target, "prefix", patches.Prefixes);
                TracePatches(target, "postfix", patches.Postfixes);
                TracePatches(target, "transpiler", patches.Transpilers);
                TracePatches(target, "finalizer", patches.Finalizers);
            }
        }

        private void TracePatches(string target, string kind, System.Collections.Generic.IEnumerable<Patch> patches)
        {
            foreach (var patch in patches)
                TraceVibeEvent("PATCH_LIST", "target=" + target + " kind=" + kind + " owner=" + patch.owner
                    + " priority=" + patch.priority + " method=" + patch.PatchMethod?.DeclaringType?.FullName
                    + "." + patch.PatchMethod?.Name + " before=" + string.Join(",", patch.before ?? new string[0])
                    + " after=" + string.Join(",", patch.after ?? new string[0]));
        }
    }
}
