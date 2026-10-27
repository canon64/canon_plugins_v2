using System.Collections.Generic;
using HarmonyLib;
using MMD4U.MiniTween;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private static readonly AccessTools.FieldRef<Sequence, Tween[]> SequenceItems = AccessTools.FieldRefAccess<Sequence, Tween[]>("compiledTweens");
        private static readonly AccessTools.FieldRef<Sequence, int> SequenceIndex = AccessTools.FieldRefAccess<Sequence, int>("prevFrameIndex");
        private static readonly AccessTools.FieldRef<Sequence, float> SequenceEnd = AccessTools.FieldRefAccess<Sequence, float>("lastTime");
        private static readonly AccessTools.FieldRef<Tween, float> TweenStart = AccessTools.FieldRefAccess<Tween, float>("_startTime");
        private static readonly AccessTools.FieldRef<Tween, float> TweenEnd = AccessTools.FieldRefAccess<Tween, float>("_endTime");
        private readonly Dictionary<Sequence, Tween[]> _optimizedSequences = new Dictionary<Sequence, Tween[]>();
        private bool _sequencePatched;

        private void PrepareSequenceOptimization()
        {
            _optimizedSequences.Clear();
            var set = AccessTools.Field(_controller.GetType(), "sequenceSet").GetValue(_controller);
            PrepareMediaClock((MMD4U.MTSequenceSet)set);
            var items = AccessTools.Field(set.GetType(), "sequencesRaw").GetValue(set) as Tween[];
            if (items == null) return;
            foreach (var tween in items)
            {
                var sequence = tween as Sequence;
                if (sequence == null) continue;
                if (SequenceItems(sequence) == null) sequence.Compile();
                var compiled = SequenceItems(sequence);
                bool valid = compiled != null;
                float previousEnd = float.NegativeInfinity;
                foreach (var item in compiled ?? new Tween[0])
                {
                    if (item == null) { valid = false; break; }
                    float start = TweenStart(item), end = TweenEnd(item);
                    if (float.IsNaN(start) || float.IsInfinity(start) || float.IsNaN(end) || float.IsInfinity(end) ||
                        start < previousEnd || end < start) { valid = false; break; }
                    previousEnd = end;
                }
                if (valid) _optimizedSequences[sequence] = compiled;
            }
            if (!_sequencePatched)
            {
                _trialHarmony.Patch(AccessTools.Method(typeof(Sequence), "Apply"),
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(ApplySequenceFast)));
                _sequencePatched = true;
            }
            LogInfo("[Performance] indexed sequences=" + _optimizedSequences.Count);
        }

        private static bool ApplySequenceFast(Sequence __instance, float t)
        {
            var plugin = Instance;
            if (plugin == null || !plugin._optimizedSequences.TryGetValue(__instance, out var items) ||
                !ReferenceEquals(items, SequenceItems(__instance))) return true;
            if (items.Length == 0) return false;
            float last = SequenceEnd(__instance);
            if (t > last) { items[items.Length - 1].Apply(last); return false; }
            int previous = SequenceIndex(__instance);
            if (previous >= 0 && previous < items.Length && TweenStart(items[previous]) <= t && t <= TweenEnd(items[previous]))
            { items[previous].Apply(t); return false; }
            // Preserve the original first matching interval, including shared boundaries.
            int lo = 0, hi = items.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (TweenEnd(items[mid]) < t) lo = mid + 1; else hi = mid;
            }
            if (lo < items.Length && TweenStart(items[lo]) <= t && t <= TweenEnd(items[lo]))
            { items[lo].Apply(t); SequenceIndex(__instance) = lo; }
            return false;
        }
    }
}
