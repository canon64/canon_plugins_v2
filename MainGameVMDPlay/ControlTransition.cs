using System;

namespace MainGameVMDPlay
{
    // Shared by loading, delayed speech and the synchronous posture-change boundary.
    internal sealed class ControlTransition
    {
        internal int Generation { get; private set; }
        internal bool Releasing { get; private set; }
        internal void Invalidate() { unchecked { Generation++; } }
        internal bool TryBeginRelease()
        {
            if (Releasing) return false;
            Releasing = true; Invalidate(); return true;
        }
        internal void CompleteRelease() { Releasing = false; }
        internal static void Cleanup(Action<Exception> report, params Action[] steps)
        {
            foreach (var step in steps) {
                try { step(); }
                catch (Exception error) { try { report(error); } catch { /* Logging cannot prevent restoration. */ } }
            }
        }
    }
}
