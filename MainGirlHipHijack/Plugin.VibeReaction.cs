using System;
using System.Reflection;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private delegate bool ReadVibeMotion(out Vector3 direction, out float body,
            out float upper, out float slow, out float gauge);

        private ReadVibeMotion _vibeMotionReader;
        private float _vibeBridgeRetryAt;
        private Vector3 _vibeBodyApplied;
        private Vector3 _vibeLeftShoulderApplied;
        private Vector3 _vibeRightShoulderApplied;
        private bool _vibeOwnsBodyIk;
        private float _vibeBodyIkBlend;

        private bool TryReadVibeMotion(out Vector3 direction, out float body, out float upper)
        {
            direction = Vector3.zero;
            body = upper = 0f;
            if (_vibeMotionReader == null && Time.unscaledTime >= _vibeBridgeRetryAt)
            {
                _vibeBridgeRetryAt = Time.unscaledTime + 5f;
                Type type = Type.GetType("MainGameVrDildo.Plugin, MainGameVrDildo", false);
                MethodInfo method = type?.GetMethod("TryGetMotionState", BindingFlags.Public | BindingFlags.Static);
                if (method != null)
                {
                    try { _vibeMotionReader = (ReadVibeMotion)Delegate.CreateDelegate(typeof(ReadVibeMotion), method); }
                    catch (Exception ex) { LogWarn("Vibe reaction API binding failed: " + ex.Message); }
                }
            }
            if (_vibeMotionReader == null) return false;
            try
            {
                float slow, gauge;
                return _vibeMotionReader(out direction, out body, out upper, out slow, out gauge);
            }
            catch (Exception ex)
            {
                LogWarn("Vibe reaction read failed: " + ex.Message);
                _vibeMotionReader = null;
                return false;
            }
        }

        private void ApplyVibeReaction()
        {
            bool active = TryReadVibeMotion(out Vector3 direction, out float body, out float upper)
                && direction.sqrMagnitude > 0.5f && body != 0f;
            if (active && !_bikEff[BIK_BODY].Running)
            {
                SetBodyIK(BIK_BODY, true, saveSettings: false, reason: "vibe-reaction");
                _vibeOwnsBodyIk = _bikEff[BIK_BODY].Running;
                if (_vibeOwnsBodyIk)
                {
                    // A preserved proxy can still be at the previous insertion position.
                    // Start this temporary IK session at the current animated waist.
                    IKEffector eff = BIK_GetEffector(BIK_BODY);
                    if (eff != null && eff.bone != null && _bikEff[BIK_BODY].Proxy != null)
                        _bikEff[BIK_BODY].Proxy.SetPositionAndRotation(eff.bone.position, eff.bone.rotation);
                    _vibeBodyApplied = Vector3.zero;
                    _vibeBodyIkBlend = 0f;
                }
            }

            Vector3 bodyOffset = active ? direction.normalized * body : Vector3.zero;
            Vector3 upperOffset = active ? direction.normalized * upper : Vector3.zero;
            ApplyVibeOffset(BIK_BODY, bodyOffset, ref _vibeBodyApplied,
                _bodyCtrlLinkEnabled);
            ApplyVibeOffset(BIK_LS, upperOffset, ref _vibeLeftShoulderApplied, false);
            ApplyVibeOffset(BIK_RS, upperOffset, ref _vibeRightShoulderApplied, false);

            if (_vibeOwnsBodyIk)
            {
                _vibeBodyIkBlend = Mathf.MoveTowards(_vibeBodyIkBlend, active ? 1f : 0f,
                    Mathf.Max(0f, Time.unscaledDeltaTime) * 8f);
                IKEffector eff = BIK_GetEffector(BIK_BODY);
                if (eff != null)
                {
                    float weight = GetBodyIKWeight(BIK_BODY) * _vibeBodyIkBlend;
                    eff.positionWeight = weight;
                    eff.rotationWeight = weight;
                }
                if (!active && _vibeBodyIkBlend <= 0f)
                {
                    SetBodyIK(BIK_BODY, false, saveSettings: false, reason: "vibe-reaction-end");
                    _vibeOwnsBodyIk = false;
                }
            }
        }

        private void ApplyVibeOffset(int idx, Vector3 wanted, ref Vector3 previous, bool overwritten)
        {
            var state = _bikEff[idx];
            if (state == null || !state.Running || state.Proxy == null || state.GizmoDragging)
            {
                previous = Vector3.zero;
                return;
            }
            // The VR body link or a follow target already rewrote the proxy this frame.
            // Otherwise remove last frame's additive offset before applying this one.
            Vector3 basePosition = state.Proxy.position;
            if (!overwritten && state.FollowBone == null) basePosition -= previous;
            state.Proxy.position = basePosition + wanted;
            previous = wanted;
        }
    }
}
