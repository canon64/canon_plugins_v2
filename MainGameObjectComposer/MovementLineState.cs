using System;

namespace MainGameObjectComposer
{
    // Counts original response lines, never audio chunks. State is scoped to a response.
    internal sealed class MovementLineState
    {
        internal string Session = "";
        internal int SourceLine = -1;
        internal int Score = -1;
        internal int SpeechLevel = -1;
        internal int SpeechThroughLine = -1;
        internal bool SpeechPriority => SourceLine >= 0 && SourceLine <= SpeechThroughLine;

        internal bool Accept(string session, int line, int score, int speechLevel)
        {
            if (string.IsNullOrEmpty(session) || line < 0) return false;
            if (!string.Equals(Session, session, StringComparison.Ordinal))
            {
                Session = session;
                SourceLine = Score = SpeechLevel = SpeechThroughLine = -1;
            }
            if (line <= SourceLine) return false;
            SourceLine = line;
            if (score >= 0 && score <= 100) Score = score;
            if (speechLevel >= 0 && speechLevel <= 4)
            {
                SpeechLevel = speechLevel;
                SpeechThroughLine = line + 1;
            }
            return true;
        }
    }

    [Serializable]
    internal sealed class MovementFieldRule
    {
        public string objectId;
        public string field;
        public bool enabled = true;
        public bool l1ZeroSet, l1FullSet, l2ZeroSet, l2FullSet;
        public float l1Zero, l1Full, l2Zero, l2Full;

        internal bool Has(int slot, bool full) => slot == 2
            ? (full ? l2FullSet : l2ZeroSet) : (full ? l1FullSet : l1ZeroSet);
        internal float Get(int slot, bool full) => slot == 2
            ? (full ? l2Full : l2Zero) : (full ? l1Full : l1Zero);
        internal void Set(int slot, bool full, float value)
        {
            if (slot == 2 && full) { l2FullSet = true; l2Full = value; }
            else if (slot == 2) { l2ZeroSet = true; l2Zero = value; }
            else if (full) { l1FullSet = true; l1Full = value; }
            else { l1ZeroSet = true; l1Zero = value; }
        }
        internal void Clear(int slot, bool full)
        {
            if (slot == 2 && full) l2FullSet = false;
            else if (slot == 2) l2ZeroSet = false;
            else if (full) l1FullSet = false;
            else l1ZeroSet = false;
        }
        internal static float Endpoint(MovementFieldRule rule, int slot, bool full, float original,
            bool radius, float zeroMultiplier, float fullMultiplier)
        {
            return rule != null && rule.Has(slot, full) ? rule.Get(slot, full)
                : radius ? original * (full ? fullMultiplier : zeroMultiplier) : original;
        }
    }
}
