using System;
using System.Collections.Generic;
using MainGameVoiceFaceEventBridge.Audio;
using MainGameVoiceFaceEventBridge.Diagnostics;
using MainGameVoiceFaceEventBridge.External;
using MainGameVoiceFaceEventBridge.Game;
using MainGameVoiceFaceEventBridge.Scheduling;

namespace MainGameVoiceFaceEventBridge.RegressionHarness
{
    internal static partial class Program
    {
        private static void CharacterizeAstralMethodCache()
        {
            double now = 0;
            int resolutions = 0;
            bool loaded = false;
            AstralStub.Active = false;
            var cache = new AstralMethodCache(name =>
            {
                Assert(name == "AstralMode.Plugin, AstralMode", "assembly-qualified Astral lookup");
                resolutions++;
                return loaded ? typeof(AstralStub) : null;
            }, () => now);
            Assert(!cache.IsActive(), "Astral absent is false");
            loaded = true;
            for (int i = 0; i < 49; i++) Assert(!cache.IsActive(), "missing Astral retry throttled");
            now = 4.999;
            Assert(!cache.TryTransition(true, out _) && resolutions == 1, "no early Astral retry");
            now = 5;
            Assert(cache.TryTransition(true, out bool accepted) && accepted && cache.IsActive(), "late Astral available");
            Assert(resolutions == 2, "retry at five seconds");
            for (int i = 0; i < 49; i++) Assert(cache.IsActive(), "cached target reads live state");
            now = 100;
            Assert(cache.TryTransition(false, out accepted) && accepted && !cache.IsActive(), "Astral returns to normal");
            Assert(resolutions == 2, "all entry points resolved once after success");

            var guard = new GameVoiceGuardState();
            guard.CaptureProcStop(false, cache.IsActive());
            cache.TryTransition(true, out _);
            Assert(!guard.ResolveRestoreValue(cache.IsActive(), out _), "normal capture remains normal");
            guard.CompleteRestore();
            guard.CaptureProcStop(true, cache.IsActive());
            cache.TryTransition(false, out _);
            Assert(!guard.ResolveRestoreValue(cache.IsActive(), out bool stale) && stale, "Astral captured true restored after return");
            cache.Dispose();
            Assert(!cache.IsActive() && !cache.TryTransition(true, out _) && resolutions == 2, "disposed cache releases targets");

            int failures = 0;
            var failing = new AstralMethodCache(_ => { failures++; throw new InvalidOperationException("expected"); }, () => 0);
            try { failing.IsActive(); } catch (InvalidOperationException) { }
            Assert(!failing.IsActive() && failures == 1, "resolver failure also throttled");
        }

        private static class AstralStub
        {
            internal static bool Active;
            internal static bool IsAstralActive() => Active;
            public static bool RequestEnterAstral() { Active = true; return true; }
            public static bool RequestExitAstral() { Active = false; return true; }
        }

        private static void CharacterizeNamedPosePrefilter()
        {
            var logs = new List<string>();
            var warnings = new List<string>();
            int lookups = 0;
            PoseStub.Calls = 0;
            PoseStub.Throw = false;
            var resolver = new NamedPoseTextResolver(logs.Add, warnings.Add, () => { lookups++; return typeof(PoseStub); });
            for (int i = 0; i < 49; i++)
                Assert(!resolver.TryResolve("今日は晴れです。", "のポーズになるね", false, out _, out _), "ordinary text rejected before bridge");
            Assert(lookups == 0 && PoseStub.Calls == 0 && warnings.Count == 0 && logs.Count == 0,
                "49 ordinary lines cause no bridge lookup or miss logging");
            Assert(resolver.TryResolve("前文。休憩のポーズになるね。", " のポーズになるね ", false, out string name, out int index)
                && name == "休憩" && index == 3, "named pose output and position preserved");
            Assert(!resolver.TryResolve("未登録のポーズになるね。", "のポーズになるね", false, out _, out _)
                && warnings.Count == 0 && logs.Count == 0, "ordinary mismatch is silent with verbose off");
            Assert(!resolver.TryResolve("未登録のポーズになるね。", "のポーズになるね", true, out _, out _)
                && warnings.Count == 0 && logs.Exists(x => x.Contains("resolve miss")), "mismatch is detailed logging only");
            int before = lookups;
            Assert(!resolver.TryResolve("休憩のポーズになるね。", "に切り替える", false, out _, out _) && lookups == before,
                "changed suffix takes effect without stale cache");
            Assert(resolver.TryResolve("休憩に切り替える。", "に切り替える", false, out _, out _), "new suffix resolves");
            Assert(!resolver.TryResolve("休憩END", "end", false, out _, out _), "suffix comparison remains ordinal");
            Assert(!resolver.TryResolve("休憩", "", false, out _, out _), "empty suffix disabled");
            var unavailable = new NamedPoseTextResolver(logs.Add, warnings.Add, () => null);
            Assert(!unavailable.TryResolve("休憩のポーズになるね", "のポーズになるね", false, out _, out _)
                && warnings.Count == 1, "missing bridge remains warning");
            PoseStub.Throw = true;
            Assert(!resolver.TryResolve("休憩のポーズになるね", "のポーズになるね", false, out _, out _)
                && warnings.Count == 2, "bridge exception remains warning");
            PoseStub.Throw = false;
        }

        public static class PoseStub
        {
            public static int Calls;
            public static bool Throw;
            public static bool TryResolveNamedPoseFromText(string text, string suffix, out string name, out int index, out string reason)
            {
                Calls++;
                if (Throw) throw new InvalidOperationException("expected");
                index = text.IndexOf("休憩" + suffix, StringComparison.Ordinal);
                name = index >= 0 ? "休憩" : string.Empty;
                reason = index >= 0 ? "matched" : "unregistered";
                return index >= 0;
            }
        }

        private static void CharacterizeReceiveTiming()
        {
            double now = 0;
            int idleClockReads = 0;
            var idleRecorder = new ReceiveTimingRecorder(_ => { }, () => { idleClockReads++; return now; });
            for (int frame = 0; frame < 1000; frame++) idleRecorder.Tick(false, frame);
            Assert(idleClockReads == 0, "disabled timing does not read the clock every frame");
            idleRecorder.Tick(true, 1000);
            idleRecorder.Tick(false, 1001);
            Assert(idleClockReads == 2, "timing state transitions still sample the clock");
            var batches = new List<ReceiveTimingRecorder.Batch>();
            var recorder = new ReceiveTimingRecorder(batches.Add, () => now);
            recorder.Tick(false, 0);
            using (recorder.BeginCommand("speak_sequence", "off", 1, 0, false)) { now = 1; }
            recorder.Flush();
            Assert(batches.Count == 0, "timing disabled creates no samples");
            now = 0;
            recorder.Tick(true, 1);
            using (recorder.BeginCommand("speak_sequence", "test", 1, 1, false))
            {
                using (recorder.Measure(ReceiveTimingStage.Guard))
                {
                    now = .001;
                    using (recorder.Measure(ReceiveTimingStage.Astral)) { now = .003; }
                    now = .004;
                }
                using (recorder.Measure(ReceiveTimingStage.Text)) { now = .006; }
                recorder.SetVoiceAfter(true);
            }
            now = .020;
            recorder.Tick(true, 2);
            now = 1.999;
            recorder.Tick(true, 3);
            Assert(batches.Count == 0, "no flush before two-second quiet period");
            now = 2;
            recorder.Tick(true, 4);
            Assert(batches.Count == 1 && batches[0].Dropped == 0, "quiet period flushes once");
            var sample = batches[0].Samples[0];
            Assert(sample.IsCommand && Math.Abs(sample.TotalMs - 6) < .00001 && Math.Abs(sample.GuardMs - 4) < .00001,
                "total and guard durations recorded");
            Assert(Math.Abs(sample.AstralMs - 2) < .00001 && Math.Abs(sample.TextMs - 2) < .00001 && sample.AstralCalls == 1,
                "nested timings and state call count recorded");
            Assert(!sample.VoiceBefore && sample.VoiceAfter && Math.Abs(batches[0].Samples[1].FrameMs - 20) < .00001,
                "playback state and Update interval captured");
            var limited = new ReceiveTimingRecorder(batches.Add, () => now, 1);
            limited.Tick(true, 10);
            using (limited.BeginCommand("speak_sequence", "bounded", 1, 10, false)) { }
            using (limited.BeginCommand("speak_sequence_append", "bounded", 2, 10, true))
            using (limited.Measure(ReceiveTimingStage.Astral)) { }
            limited.Flush();
            Assert(batches[1].Samples.Length == 1 && batches[1].Dropped == 1, "fixed capacity counts overflow without corrupting sample");
        }

        private static void Characterize49PartSequence()
        {
            var audio = new AudioSequenceState();
            var scheduler = new StableActionScheduler();
            var sessions = new SequenceSessionCoordinator(scheduler);
            var fired = new List<int>();
            sessions.Activate("49");
            for (int i = 1; i <= 49; i++)
            {
                int index = i;
                var item = new ExternalVoicePlaybackItem { Index = i, Path = "neutral.wav", Subtitle = "line " + i, DurationSeconds = 1 };
                int count = i == 1 ? audio.Begin(new[] { item }, "49") : audio.Append(new[] { item }, "49", out _);
                Assert(count == 1, "49-part sequence accepts each item");
                sessions.ScheduleLineAction("49", i - 1, .5f, "effect", () => fired.Add(index), null, out _);
            }
            Assert(scheduler.DrainDue(100, null) == 0, "effects wait for actual audio start");
            for (int i = 1; i <= 49; i++)
            {
                Assert(audio.TryBeginNextLoad(out var item) && item.Index == i && item.Subtitle == "line " + i, "audio/subtitle order preserved");
                audio.TakePending();
                sessions.RecordLineStart("49", i - 1, i * 10, null);
                Assert(scheduler.DrainDue(i * 10 + .49f, null) == 0, "effect not early");
                scheduler.DrainDue(i * 10 + .5f, null);
                Assert(fired.Count == i && fired[i - 1] == i, "effect follows the matching audio line");
            }
            audio.Begin(new[] { new ExternalVoicePlaybackItem { Path = "pending.wav" } }, "old");
            audio.TryBeginNextLoad(out _);
            audio.Begin(new[] { new ExternalVoicePlaybackItem { Path = "replacement.wav" } }, "new");
            Assert(audio.TryBeginNextLoad(out var replacement) && replacement.Path == "replacement.wav", "new session replaces pending playback");
            Assert(audio.Append(new[] { new ExternalVoicePlaybackItem { Path = "stale.wav" } }, "old", out _) == -1, "old session append rejected");
            audio.Clear();
            sessions.ClearTransientState();
            sessions.Deactivate();
            Assert(!audio.HasQueuedOrPending && sessions.PendingLineActionCount == 0 && scheduler.Count == 0, "stop clears audio and timed effects");
        }
    }
}
