using System;

namespace MainGirlHipHijack.Services
{
    internal static class MotionStrengthClassifier
    {
        internal const string Unknown = "unknown";
        internal const string Weak = "weak";
        internal const string Strong = "strong";

        private static readonly string[] StrongStates =
        {
            "SLoop", "A_SLoop", "SS_IN_Loop", "SF_IN_Loop", "sameS", "orgS"
        };

        private static readonly string[] WeakStates =
        {
            "WLoop", "A_WLoop", "WS_IN_Loop", "WF_IN_Loop", "sameW", "orgW"
        };

        internal static string Classify(Func<string, bool> isState)
        {
            if (isState == null)
                return Unknown;
            if (MatchesAny(isState, StrongStates))
                return Strong;
            if (MatchesAny(isState, WeakStates))
                return Weak;
            return Unknown;
        }

        internal static bool IsStrongWeakSwitch(string previous, string next)
        {
            bool previousKnown = previous == Strong || previous == Weak;
            bool nextKnown = next == Strong || next == Weak;
            return previousKnown && nextKnown && !string.Equals(previous, next, StringComparison.Ordinal);
        }

        private static bool MatchesAny(Func<string, bool> isState, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (isState(candidates[i]))
                    return true;
            }
            return false;
        }
    }
}
