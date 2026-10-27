using System;

namespace MainGameVoiceFaceEventBridge.Game
{
    internal sealed class GameVoiceGuardState
    {
        public float BlockUntil { get; private set; }
        public bool ProcStopOverridden { get; private set; }
        public bool ProcStopOriginal { get; private set; }
        public bool ProcStopCapturedAstral { get; private set; }
        public float NextRestorePendingLogTime { get; private set; }

        public void ExtendBlockUntil(float until)
        {
            if (until > BlockUntil) BlockUntil = until;
        }

        public void ClearBlock()
        {
            BlockUntil = 0f;
        }

        public bool IsWithinBlock(float now)
        {
            return now < BlockUntil;
        }

        public bool CaptureProcStop(bool originalValue, bool capturedAstral)
        {
            if (ProcStopOverridden) return false;
            ProcStopOriginal = originalValue;
            ProcStopCapturedAstral = capturedAstral;
            ProcStopOverridden = true;
            return true;
        }

        public bool ResolveRestoreValue(bool isAstralModeActive, out bool staleAstralTrue)
        {
            staleAstralTrue = ProcStopOriginal && ProcStopCapturedAstral && !isAstralModeActive;
            return staleAstralTrue ? false : ProcStopOriginal;
        }

        public bool ShouldLogRestorePending(float now)
        {
            if (now < NextRestorePendingLogTime) return false;
            NextRestorePendingLogTime = now + 1f;
            return true;
        }

        public void CompleteRestore()
        {
            ProcStopOverridden = false;
            ProcStopOriginal = false;
            ProcStopCapturedAstral = false;
            NextRestorePendingLogTime = 0f;
        }

        public void Reset()
        {
            ClearBlock();
            CompleteRestore();
        }
    }
}
