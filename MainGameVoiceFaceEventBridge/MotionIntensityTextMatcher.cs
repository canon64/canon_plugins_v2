using System;
using System.Collections.Generic;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed class MotionIntensityHit
    {
        internal int Index;
        internal int Length;
        internal int Level;
    }

    internal static class MotionIntensityTextMatcher
    {
        internal static List<MotionIntensityHit> FindHits(string text, string[] targets, string[][] levels)
        {
            var hits = new List<MotionIntensityHit>();
            if (string.IsNullOrEmpty(text) || targets == null || levels == null) return hits;
            int sentenceStart = 0;
            while (sentenceStart < text.Length)
            {
                int sentenceEnd = sentenceStart;
                while (sentenceEnd < text.Length && "。！？!?\n\r".IndexOf(text[sentenceEnd]) < 0) sentenceEnd++;
                string sentence = text.Substring(sentenceStart, sentenceEnd - sentenceStart);
                bool hasTarget = false;
                foreach (string target in targets)
                    if (!string.IsNullOrEmpty(target) && sentence.IndexOf(target, StringComparison.Ordinal) >= 0) { hasTarget = true; break; }
                if (hasTarget)
                {
                    var candidates = new List<MotionIntensityHit>();
                    for (int level = 0; level < levels.Length; level++)
                    {
                        if (levels[level] == null) continue;
                        foreach (string phrase in levels[level])
                        {
                            if (string.IsNullOrEmpty(phrase)) continue;
                            int from = 0;
                            while (from < sentence.Length)
                            {
                                int found = sentence.IndexOf(phrase, from, StringComparison.Ordinal);
                                if (found < 0) break;
                                candidates.Add(new MotionIntensityHit { Index = sentenceStart + found, Length = phrase.Length, Level = level });
                                from = found + 1;
                            }
                        }
                    }
                    candidates.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : b.Length.CompareTo(a.Length));
                    int occupiedUntil = -1;
                    foreach (MotionIntensityHit hit in candidates)
                    {
                        if (hit.Index < occupiedUntil) continue;
                        hits.Add(hit);
                        occupiedUntil = hit.Index + hit.Length;
                    }
                }
                sentenceStart = sentenceEnd + 1;
            }
            return hits;
        }
    }
}
