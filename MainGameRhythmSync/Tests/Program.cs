using System;
using MainGameRhythmSync;

internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string name)
    {
        _checks++;
        if (!ok) throw new Exception(name);
    }
    private static void Main()
    {
        foreach (int fps in new[] { 30, 60, 120 })
        foreach (int beats in new[] { 1, 2, 4 })
        foreach (double bpm in new[] { 60d, 138d, 180d })
        {
            var clock = new SharedRhythmClock();
            double time = 0d, normalized = 0.37d, dt = 1d / fps, intrinsic = 0.75d;
            float speed = 1f;
            double cycle = 60d / bpm * beats;
            for (int frame = 0; frame < fps * 15; frame++)
            {
                time += dt;
                normalized += dt * intrinsic * speed;
                speed = clock.Step(1, normalized, 1d / intrinsic, time, true, time / cycle, cycle, 0.8d);
                Check(speed >= 0.1f && speed <= 8f, "bounded speed");
            }
            Check(Math.Abs(clock.BeatError) < 0.02d, $"phase convergence {fps}/{beats}/{bpm}: {clock.BeatError}");
            Check(Math.Abs(speed * intrinsic - 1d / cycle) < 0.03d, "cycle rate");
            speed = clock.Step(1, normalized, 1d / intrinsic, time + dt, false, 0, cycle, 0.8d);
            Check(speed == 1f, "stop must release immediately");
            for (int i = 0; i < 5; i++)
            {
                Check(clock.Step(1, normalized, 1d / intrinsic, time + i, false, 0, cycle, .8) == 1f, "no music stays neutral");
            }
            for (int frame = 0; frame < fps * 6; frame++)
            {
                double elapsed = frame == fps * 2 ? 0.8d : dt;
                time += elapsed;
                normalized += elapsed * intrinsic * speed;
                speed = clock.Step(1, normalized, 1d / intrinsic, time, true, time / cycle, cycle, .8);
            }
            Check(Math.Abs(clock.BeatError) < 0.04d, "resume and frame stall recovery");
            clock.Reset();
            Check(clock.Speed == 1f, "reset releases multiplier");
        }
        var changed = new SharedRhythmClock();
        Check(changed.Step(1, 0, 1, 1, false, 0, 0, .8) == 1, "no BPM fallback");
        Check(changed.Step(1, double.NaN, 1, 1, true, 0, 1, .8) == 1, "invalid sample");
        Check(Math.Abs(SharedRhythmClock.PhaseDifference(.01, .99) - .02) < 1e-8, "wrap forward");
        Check(Math.Abs(SharedRhythmClock.PhaseDifference(.99, .01) + .02) < 1e-8, "wrap backward");
        Console.WriteLine("PASS " + _checks + " checks: shared clock, 30/60/120 fps, BPM 60/138/180, 1/2/4 beats, stop/resume, stalls");
    }
}
