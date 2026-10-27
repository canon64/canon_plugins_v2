namespace MainGameVMDPlay
{
    internal sealed class TouchVoiceRequests
    {
        private readonly int[] _values = { -1, -1, -1, -1 };
        private readonly int[] _frames = new int[4];
        internal void Record(int actor, bool shortVoice, int value, int frame)
        {
            if (actor < 0 || actor > 1) return;
            int slot = actor * 2 + (shortVoice ? 1 : 0);
            _values[slot] = value; _frames[slot] = frame;
        }
        internal bool Matches(int actor, bool shortVoice, int value, int frame)
        {
            if (actor < 0 || actor > 1 || value < 0) return false;
            int slot = actor * 2 + (shortVoice ? 1 : 0);
            return _values[slot] == value && frame >= _frames[slot] && frame - _frames[slot] <= 2;
        }
        internal void Clear(int actor, bool shortVoice) { Record(actor, shortVoice, -1, 0); }
        internal void Reset() { for (int i = 0; i < 4; i++) _values[i] = -1; }
    }
}
