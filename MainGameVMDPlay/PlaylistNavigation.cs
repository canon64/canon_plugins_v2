namespace MainGameVMDPlay
{
    internal static class PlaylistNavigation
    {
        internal static int Step(int index, int count, int delta, bool loop)
        {
            if (count <= 0) return -1;
            if (index < 0) return 0;
            int next = index + delta;
            if (next >= 0 && next < count) return next;
            return loop ? (next < 0 ? count - 1 : 0) : -1;
        }
    }
}
