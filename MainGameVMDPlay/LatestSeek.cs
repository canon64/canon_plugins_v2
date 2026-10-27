namespace MainGameVMDPlay
{
    // Single in-flight operation; intervening requests replace the desired target.
    internal sealed class LatestSeek
    {
        internal float Target { get; private set; }
        internal bool Pending { get; private set; }
        private bool dirty;
        internal void Request(float target) { Target = target; dirty = true; }
        internal bool TryBegin(out float target)
        {
            target = Target;
            if (Pending || !dirty) return false;
            dirty = false; Pending = true; return true;
        }
        internal void Complete() { Pending = false; }
        internal void Reset() { Pending = false; dirty = false; }
    }
}
