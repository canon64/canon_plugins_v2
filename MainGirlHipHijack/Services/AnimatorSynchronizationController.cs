using System;
using UnityEngine;

namespace MainGirlHipHijack.Services
{
    internal sealed class AnimatorSynchronizationController
    {
        internal void ResetLoopAnimators(
            ChaControl female,
            ChaControl male,
            Action<string> logDebug)
        {
            if (female == null)
                return;

            AnimatorStateInfo femaleState;
            try { femaleState = female.getAnimatorStateInfo(0); }
            catch { return; }

            if (!IsLoopMotion(femaleState))
            {
                logDebug?.Invoke("[SyncReset] skipped — not in loop motion");
                return;
            }

            ResetAnimatorToStart(female, "female", logDebug);
            ResetAnimatorToStart(male, "male", logDebug);
        }

        private static bool IsLoopMotion(AnimatorStateInfo state)
        {
            return state.IsName("SLoop") || state.IsName("A_SLoop")
                || state.IsName("SS_IN_Loop") || state.IsName("SF_IN_Loop")
                || state.IsName("WLoop") || state.IsName("A_WLoop")
                || state.IsName("WS_IN_Loop") || state.IsName("WF_IN_Loop");
        }

        private static void ResetAnimatorToStart(
            ChaControl character,
            string label,
            Action<string> logDebug)
        {
            if (character == null)
                return;

            Animator animator = character.animBody != null
                ? character.animBody.GetComponent<Animator>()
                : character.GetComponentInChildren<Animator>(true);
            if (animator == null)
                return;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(state.fullPathHash, 0, 0f);
            logDebug?.Invoke("[SyncReset] " + label + " animator reset to 0");
        }
    }
}
