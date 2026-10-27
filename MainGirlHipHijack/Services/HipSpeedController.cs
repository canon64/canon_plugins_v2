using System;
using MainGirlHipHijack.Runtime;
using UnityEngine;

namespace MainGirlHipHijack.Services
{
    /// <summary>
    /// 操作点の移動量からHシーン速度と自動挿入要求を更新する。
    /// Unity参照の解決は呼出側に残し、速度状態だけを直接更新する。
    /// </summary>
    internal sealed class HipSpeedController
    {
        internal void Tick(
            BodyIkGizmoSettings settings,
            HipSpeedRuntimeState state,
            HFlag flags,
            ChaControl targetFemale,
            Vector3 trackingPosition,
            float unscaledDeltaTime,
            float unscaledTime,
            Action<string> logDebug,
            Action<string> logInfo)
        {
            if (settings == null || !settings.SpeedHijackEnabled)
            {
                state.HasLastTrackingPosition = false;
                state.IsMoving = false;
                state.SpeedBootstrapSent = false;
                state.InsertBootstrapSent = false;
                return;
            }

            if (flags == null || unscaledDeltaTime <= 0f)
                return;

            if (!state.HasLastTrackingPosition)
            {
                state.LastTrackingPosition = trackingPosition;
                state.HasLastTrackingPosition = true;
                state.LastMovementTime = unscaledTime;
                return;
            }

            Vector3 delta = trackingPosition - state.LastTrackingPosition;
            state.LastTrackingPosition = trackingPosition;

            float threshold = settings.SpeedMovementThreshold;
            bool moved = delta.sqrMagnitude >= threshold * threshold;

            TryQueueSpeedBootstrap(flags, targetFemale, moved, state, logDebug);
            TryQueueIdleInsert(flags, targetFemale, moved, settings, state, logInfo);

            if (moved)
            {
                if (!state.IsMoving)
                {
                    state.IsMoving = true;
                    logDebug?.Invoke("[Speed] move start");
                }

                state.LastMovementTime = unscaledTime;
                ApplySpeedByMotion(flags, settings, unscaledDeltaTime, moving: true);
                return;
            }

            if (state.IsMoving && unscaledTime - state.LastMovementTime >= settings.SpeedIdleDelay)
            {
                state.IsMoving = false;
                logDebug?.Invoke("[Speed] move stop -> decay");
            }

            if (!state.IsMoving)
                ApplySpeedByMotion(flags, settings, unscaledDeltaTime, moving: false);
        }

        private static void ApplySpeedByMotion(
            HFlag flags,
            BodyIkGizmoSettings settings,
            float deltaTime,
            bool moving)
        {
            float addPerSecond = moving
                ? settings.SpeedMoveAddPerSecond
                : -settings.SpeedDecayPerSecond;
            float deltaPerFrame = addPerSecond * deltaTime;

            if (flags.mode == HFlag.EMode.aibu)
            {
                float max = Mathf.Max(0.0001f, flags.speedMaxAibuBody);
                flags.SpeedUpClickAibu(deltaPerFrame, max, _drag: false);
                flags.speed = Mathf.Clamp(flags.speed, 0f, max);
                flags.speedCalc = Mathf.Clamp01(flags.speed / max);
                return;
            }

            flags.SpeedUpClick(deltaPerFrame, 1f);
            flags.speedCalc = Mathf.Clamp01(flags.speedCalc);
            flags.speed = EvaluateSpeedByModeCurve(flags, flags.speedCalc);
        }

        private static void TryQueueSpeedBootstrap(
            HFlag flags,
            ChaControl targetFemale,
            bool moved,
            HipSpeedRuntimeState state,
            Action<string> logDebug)
        {
            if (!moved || state.SpeedBootstrapSent)
                return;

            HFlag.EMode mode = flags.mode;
            if (mode != HFlag.EMode.sonyu && mode != HFlag.EMode.sonyu3P && mode != HFlag.EMode.sonyu3PMMF)
            {
                state.SpeedBootstrapSent = false;
                return;
            }

            if (targetFemale == null)
                return;

            AnimatorStateInfo animationState;
            try { animationState = targetFemale.getAnimatorStateInfo(0); }
            catch { return; }

            if (!animationState.IsName("InsertIdle") && !animationState.IsName("A_InsertIdle"))
            {
                state.SpeedBootstrapSent = false;
                return;
            }

            if (flags.click != HFlag.ClickKind.none)
                return;

            flags.click = HFlag.ClickKind.speedup;
            state.SpeedBootstrapSent = true;
            logDebug?.Invoke("[Speed] bootstrap click=speedup");
        }

        private static void TryQueueIdleInsert(
            HFlag flags,
            ChaControl targetFemale,
            bool moved,
            BodyIkGizmoSettings settings,
            HipSpeedRuntimeState state,
            Action<string> logInfo)
        {
            if (!moved || state.InsertBootstrapSent || !settings.AutoInsertOnMoveEnabled)
                return;

            HFlag.EMode mode = flags.mode;
            if (mode != HFlag.EMode.sonyu && mode != HFlag.EMode.sonyu3P && mode != HFlag.EMode.sonyu3PMMF)
            {
                state.InsertBootstrapSent = false;
                return;
            }

            if (targetFemale == null)
                return;

            AnimatorStateInfo animationState;
            try { animationState = targetFemale.getAnimatorStateInfo(0); }
            catch { return; }

            if (!animationState.IsName("Idle") && !animationState.IsName("A_Idle"))
            {
                state.InsertBootstrapSent = false;
                return;
            }

            if (flags.click != HFlag.ClickKind.none)
                return;

            flags.click = HFlag.ClickKind.insert;
            state.InsertBootstrapSent = true;
            logInfo?.Invoke("[AutoInsert] idle move detected -> click=insert");
        }

        private static float EvaluateSpeedByModeCurve(HFlag flags, float speedCalculation)
        {
            float x = Mathf.Clamp01(speedCalculation);
            AnimationCurve curve;
            switch (flags.mode)
            {
                case HFlag.EMode.houshi:
                case HFlag.EMode.houshi3P:
                case HFlag.EMode.houshi3PMMF:
                    curve = flags.speedHoushiCurve;
                    break;
                default:
                    curve = flags.speedSonyuCurve;
                    break;
            }

            return curve != null && curve.length > 0 ? curve.Evaluate(x) : x;
        }
    }
}
