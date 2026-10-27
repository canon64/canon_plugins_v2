using System;
using System.IO;
using MainGameObjectComposer;

internal static class MovementTests
{
    internal static void Run(Action<bool, string> check)
    {
        var state = new MovementLineState();
        check(state.Accept("a", 0, 30, -1) && state.Score == 30 && !state.SpeechPriority, "first tag accepted");
        check(!state.Accept("a", 0, 90, 4) && state.Score == 30, "split audio chunk cannot change source-line state");
        state.Accept("a", 1, 50, 3);
        check(state.SpeechPriority && state.SpeechLevel == 3, "spoken level wins on specified source line");
        state.Accept("a", 2, 80, -1);
        check(state.SpeechPriority && state.Score == 80, "next source line still speech priority and latest tag retained");
        state.Accept("a", 3, -1, -1);
        check(!state.SpeechPriority && state.Score == 80, "second following source line resumes latest tag");
        state.Accept("a", 4, 0, -1);
        check(state.Score == 0, "zero is a score, not missing");
        state.Accept("a", 5, 200, -1);
        check(state.Score == 0, "invalid score preserves previous value");
        state.Accept("a", 6, 60, 1);
        state.Accept("a", 7, 70, 4);
        check(state.SpeechThroughLine == 8 && state.SpeechLevel == 4, "new spoken command renews two-line priority");
        state.Accept("b", 0, -1, -1);
        check(state.Score == -1 && !state.SpeechPriority, "new response clears previous tag and temporary priority");

        float Default(bool full, bool radius) => MovementFieldRule.Endpoint(null, 1, full, 2f, radius, 0.3f, 1.5f);
        check(Math.Abs(Default(false, true) - 0.6f) < 0.0001f && Default(true, true) == 3f, "unspecified radii use 0.3 and 1.5 multipliers");
        check(Default(false, false) == 2f && Default(true, false) == 2f, "unspecified position and rotation retain original values");
        var rule = new MovementFieldRule();
        rule.Set(1, true, 9f);
        check(MovementFieldRule.Endpoint(rule, 1, true, 2, true, 0.3f, 1.5f) == 9,
            "explicit endpoint overrides radius default");
        check(Math.Abs(MovementFieldRule.Endpoint(rule, 1, false, 2, true, 0.3f, 1.5f) - .6f) < .0001f,
            "other endpoint still uses default");
        check(!rule.Has(2, true), "L1 capture does not assign L2");
        rule.Clear(1, true);
        check(!rule.Has(1, true), "endpoint can return to default");

        string file = Path.GetTempFileName();
        try
        {
            var log = new SimpleFileLogger();
            log.Initialize(file, true);
            log.Write("INFO", "OLD_RUN");
            log.Initialize(file, true);
            log.Write("INFO", "NEW_RUN");
            string text = File.ReadAllText(file);
            check(!text.Contains("OLD_RUN") && text.Contains("NEW_RUN"), "movement log resets at next startup");
        }
        finally { File.Delete(file); }
    }
}
