using System;
using UnityEngine;

namespace MainGamePortraitCapture
{
    public sealed partial class Plugin
    {
        private bool _diagnosticBatchActive;
        private int _diagnosticBatchCount;
        private int _diagnosticShotIndex;
        private Transform _diagnosticHead;
        private Animator _diagnosticAnimator;
        private Vector3 _diagnosticLastHeadPosition;
        private bool _diagnosticHasLastHeadPosition;

        private void BeginBoneDiagnostic(SubjectPoints subject)
        {
            if (!_runtimeLoggingEnabled) return;
            if (!_diagnosticBatchActive)
            {
                _diagnosticShotIndex = 1;
                _diagnosticBatchCount = 1;
            }

            _diagnosticHead = subject != null ? subject.Head : null;
            _diagnosticAnimator = null;
            if (subject != null && subject.Female != null)
            {
                try
                {
                    Transform animationRoot = subject.Female.animBody != null
                        ? subject.Female.animBody.transform
                        : subject.Female.transform;
                    _diagnosticAnimator = animationRoot.GetComponent<Animator>();
                    if (_diagnosticAnimator == null)
                        _diagnosticAnimator = animationRoot.GetComponentInChildren<Animator>(true);
                }
                catch { }
            }

            _diagnosticHasLastHeadPosition = false;
            TraceBoneDiagnostic("sequence-start", true);
        }

        private void EndBoneDiagnostic()
        {
            TraceBoneDiagnostic("diagnostic-end", true);
            _diagnosticHead = null;
            _diagnosticAnimator = null;
            _diagnosticHasLastHeadPosition = false;
        }

        private void TraceBoneDiagnostic(string phase, bool force)
        {
            if (!_runtimeLoggingEnabled || _cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value || _diagnosticHead == null) return;

            Vector3 headPosition = _diagnosticHead.position;
            Vector3 delta = _diagnosticHasLastHeadPosition
                ? headPosition - _diagnosticLastHeadPosition
                : Vector3.zero;
            bool changed = !_diagnosticHasLastHeadPosition || delta.sqrMagnitude > 0.000001f;
            if (!force && !changed) return;

            string animatorState = "animator=none";
            if (_diagnosticAnimator != null)
            {
                try
                {
                    AnimatorStateInfo state = _diagnosticAnimator.GetCurrentAnimatorStateInfo(0);
                    animatorState = "animatorEnabled=" + _diagnosticAnimator.enabled
                        + " animatorSpeed=" + _diagnosticAnimator.speed.ToString("0.000")
                        + " state=" + state.fullPathHash
                        + " normalized=" + state.normalizedTime.ToString("0.000")
                        + " transition=" + _diagnosticAnimator.IsInTransition(0);
                }
                catch (Exception ex)
                {
                    animatorState = "animatorError=" + ex.GetType().Name;
                }
            }

            LogDebug("bone-trace shot=" + _diagnosticShotIndex + "/" + _diagnosticBatchCount
                + " frame=" + Time.frameCount
                + " realtime=" + Time.realtimeSinceStartup.ToString("0.000")
                + " phase=" + phase
                + " head=" + headPosition.ToString("F4")
                + " delta=" + delta.ToString("F4")
                + " " + animatorState);

            _diagnosticLastHeadPosition = headPosition;
            _diagnosticHasLastHeadPosition = true;
        }
    }
}
