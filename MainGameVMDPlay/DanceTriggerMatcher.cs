using System;
using System.Collections.Generic;

namespace MainGameVMDPlay
{
    internal static class DanceTriggerMatcher
    {
        internal const string RandomPhrase = "ランダムに踊るね";
        internal const string Suffix = "を踊るね";
        internal sealed class Hit { internal int Index; internal string Name; internal bool IsRandom; }
        internal static List<Hit> Find(string text, IEnumerable<string> names, string triggerSuffix = Suffix)
        {
            var hits = new List<Hit>();
            if (string.IsNullOrEmpty(text)) return hits;
            int randomOffset = 0;
            while ((randomOffset = text.IndexOf(RandomPhrase, randomOffset, StringComparison.Ordinal)) >= 0) {
                hits.Add(new Hit { Index = randomOffset, IsRandom = true });
                randomOffset += RandomPhrase.Length;
            }
            if (string.IsNullOrEmpty(triggerSuffix)) return hits;
            int offset = 0;
            while (offset < text.Length) {
                int suffix = text.IndexOf(triggerSuffix, offset, StringComparison.Ordinal);
                if (suffix < 0) break;
                string best = null;
                foreach (string name in names) {
                    if (string.IsNullOrEmpty(name) || name.Length > suffix) continue;
                    if (string.CompareOrdinal(text, suffix - name.Length, name, 0, name.Length) == 0 && (best == null || name.Length > best.Length)) best = name;
                }
                hits.Add(new Hit { Index = best == null ? suffix : suffix - best.Length, Name = best });
                offset = suffix + triggerSuffix.Length;
            }
            hits.Sort((a, b) => a.Index.CompareTo(b.Index));
            return hits;
        }
    }
}
