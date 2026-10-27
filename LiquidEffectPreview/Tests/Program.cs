using System;
using System.Collections.Generic;
using System.Linq;
using LiquidEffectPreview;

internal static class Program
{
    private static int _tests;
    private static readonly EmitterPose Pose = new EmitterPose(default, new V3(0, .7660444f, .6427876f), new V3(1, 0, 0));
    private static LiquidSpec Spec { get { var s = LiquidSpec.Default; s.Drops = 49; return s; } }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double actual, double expected, double tolerance, string label)
        => Check(Math.Abs(actual - expected) <= tolerance, $"{label}: {actual:R} != {expected:R}");
    private static void Near(V3 actual, V3 expected, float tolerance, string label)
        => Check((actual - expected).Length <= tolerance, $"{label}: distance {(actual - expected).Length:R}");
    private static void Test(string name, Action body) { body(); _tests++; Console.WriteLine("PASS " + name); }
    private static LiquidSimulation New() => new LiquidSimulation(41729);
    private static EmitterPose Moving(double time) => new EmitterPose(new V3((float)(time * .07), (float)(time * .03), 0), Pose.Forward, Pose.Right);
    private static LiquidSimulation Run(double fps, bool moving = false, bool stall = false, double end = .4)
    {
        var sim = New(); sim.Fire(Spec, moving ? Moving(0) : Pose);
        while (sim.Now < end - 1e-10)
        {
            double dt = Math.Min(end - sim.Now, stall && sim.Now < 1e-8 ? .173 : 1 / fps);
            sim.Advance(dt, moving ? Moving(sim.Now + dt) : Pose);
        }
        return sim;
    }
    private static LiquidSimulation.Drop[] Active(LiquidSimulation sim) => sim.Drops.Where(d => d.Active).OrderBy(d => d.Id).ToArray();
    private static void Compare(LiquidSimulation a, LiquidSimulation b)
    {
        var left = Active(a); var right = Active(b);
        Check(left.Length == 49 && right.Length == 49, "49 drops independent of fps");
        Check(a.TotalDropsCreated == b.TotalDropsCreated, "emitted quantity");
        for (int i = 0; i < left.Length; i++)
        {
            Check(left[i].Id == right[i].Id, "event identity");
            Near(left[i].Position(a.Now), right[i].Position(b.Now), 2e-6f, "flight position");
            Near(left[i].Launch.Velocity, right[i].Launch.Velocity, 2e-6f, "flight velocity");
            Near(left[i].Separated, right[i].Separated, 1e-8, "separation time");
            var p = new List<PathPoint>(); var q = new List<PathPoint>();
            a.DropTail(left[i], .05f, .5f, p); b.DropTail(right[i], .05f, .5f, q);
            Near(Length(p), Length(q), 2e-6, "tail length");
        }
    }
    private static float Length(List<PathPoint> path)
    { float sum = 0; for (int i = 1; i < path.Count; i++) sum += (path[i].Position - path[i - 1].Position).Length; return sum; }
    private static void Finite(TubeGeometry geometry)
    {
        foreach (var v in geometry.Vertices)
        {
            Check(float.IsFinite(v.Position.X) && float.IsFinite(v.Position.Y) && float.IsFinite(v.Position.Z), "finite position");
            Near(v.Normal.Length, 1, 2e-5, "unit surface normal");
        }
        for (int i = 1; i < geometry.Resampled.Count; i++)
            Check((geometry.Resampled[i].Position - geometry.Resampled[i - 1].Position).Length <= .005001f, "5mm maximum spacing");
    }
    public static int Main()
    {
        try
        {
            Test("30/60/120 fps: identical count, flight and tails", () => {
                Compare(Run(30), Run(60)); Compare(Run(60), Run(120));
            });
            Test("173ms stalled frame preserves count, flight and tails", () => Compare(Run(120), Run(60, stall: true)));
            Test("moving nozzle: same world-space launch history at all frame rates", () => {
                Compare(Run(30, true), Run(120, true)); Compare(Run(120, true), Run(60, true, true));
            });
            Test("drops emerge over time, not in one batch", () => {
                var sim = New(); sim.Fire(Spec, Pose); sim.Advance(.099, Pose);
                Check(sim.TotalDropsCreated == 0, "no drop before breakup");
                sim.Advance(.052, Pose); Check(sim.TotalDropsCreated > 20 && sim.TotalDropsCreated < 30, "partial release");
                sim.Advance(.05, Pose); Check(sim.TotalDropsCreated == 49, "complete release");
            });
            Test("3 streams remain 3 when drop count is increased", () => {
                var low = New(); var high = New(); var s = Spec; s.Drops = 1;
                low.Fire(s, Pose); high.Fire(Spec, Pose); low.Advance(.08, Pose); high.Advance(.08, Pose);
                Check(low.Bursts[0].Spec.Jets == 3 && high.Bursts[0].Spec.Jets == 3, "independent counts");
                for (int j = 0; j < 3; j++) {
                    var p = new List<PathPoint>(); var q = new List<PathPoint>();
                    low.JetPath(low.Bursts[0], j, p); high.JetPath(high.Bursts[0], j, q);
                    Check(p.Count == q.Count, "stream resolution unchanged");
                    for (int i = 0; i < p.Count; i++) Near(p[i].Position, q[i].Position, 1e-8f, "same stream");
                }
            });
            Test("breakup preserves stream position and velocity", () => {
                var sim = New(); sim.Fire(Spec, Pose); sim.Advance(.15, Pose);
                foreach (var drop in Active(sim)) {
                    var launch = LiquidSimulation.SampleLaunch(sim.Bursts[0], drop.Jet, drop.Launch.Born);
                    Near(drop.Position(drop.Separated), launch.Position(drop.Separated, Spec.Gravity), 1e-6f, "separation position");
                    Near(drop.Launch.SpeedAt(drop.Separated, Spec.Gravity), launch.SpeedAt(drop.Separated, Spec.Gravity), 1e-6f, "separation velocity");
                }
            });
            Test("launched liquid is not dragged by later nozzle motion", () => {
                var stationary = Run(60, end: .22); var moving = Run(60, end: .22);
                stationary.Advance(.1, Pose); moving.Advance(.1, new EmitterPose(new V3(2, 3, 4), Pose.Forward, Pose.Right));
                Compare(stationary, moving);
            });
            Test("each stream keeps its initial direction throughout one burst", () => {
                var sim = New(); sim.Fire(Spec, Pose);
                sim.Advance(.08, new EmitterPose(default, new V3(1, 0, 0), V3.Forward));
                var b = sim.Bursts[0];
                for (int j = 0; j < b.Spec.Jets; j++) for (int k = 1; k < b.Samples; k++)
                    Near(b.Launches[j, k].Velocity, b.Launches[j, 0].Velocity, 1e-7f, "stable stream direction");
            });
            Test("cancelling release keeps airborne liquid and no future launches", () => {
                var sim = New(); sim.Fire(Spec, Pose); sim.Advance(.051, Pose); sim.CancelEmission(); sim.Advance(.2, Pose);
                var drops = Active(sim); Check(drops.Length >= 24 && drops.Length <= 25, "only already launched quantity");
                Check(drops.All(d => d.Launch.Born <= .051), "cancelled future launch");
                sim.Advance(1, Pose); Check(Active(sim).Length == 0, "airborne drops expire");
            });
            Test("pause freezes clock and rejects queued fire", () => {
                var p = new LiquidPlayback(1); p.Tick(0, Pose, 1, true, false, true, Spec); p.Tick(.05, Pose, 1, true, false, false, Spec);
                double before = p.Simulation.Now;
                p.Tick(4, Pose, 1, true, true, true, Spec); Near(p.Simulation.Now, before, 0, "paused clock");
                p.Tick(.01, Pose, 1, true, false, true, Spec);
                Check(p.Simulation.Bursts.Count(b => b.Active) == 1, "no deferred burst after pause");
                Near(p.Simulation.Now, before + .01, 1e-9, "no catchup after pause");
            });
            Test("held trigger only fires once; release/repress fires again", () => {
                var p = new LiquidPlayback(1);
                for (int i = 0; i < 5; i++) p.Tick(.01, Pose, 1, true, false, true, Spec);
                Check(p.Simulation.Bursts.Count(b => b.Active) == 1, "held trigger");
                p.Tick(.01, Pose, 1, true, false, false, Spec); p.Tick(.01, Pose, 1, true, false, true, Spec);
                Check(p.Simulation.Bursts.Count(b => b.Active) == 2, "fresh trigger");
            });
            Test("OFF clears active and scheduled liquid immediately", () => {
                var p = new LiquidPlayback(1); p.Tick(0, Pose, 1, true, false, true, Spec); p.Tick(.15, Pose, 1, true, false, false, Spec);
                Check(Active(p.Simulation).Length > 0, "has airborne drops");
                p.Tick(0, Pose, 1, false, false, true, Spec);
                Check(!p.Simulation.Bursts.Any(b => b.Active) && Active(p.Simulation).Length == 0, "disabled state cleared");
                p.Tick(.1, Pose, 1, true, false, true, Spec); Check(!p.Simulation.Bursts.Any(b => b.Active), "no held-key replay");
            });
            Test("new context and nozzle teleport clear old geometry history", () => {
                var p = new LiquidPlayback(1); p.Tick(0, Pose, 1, true, false, true, Spec); p.Tick(.15, Pose, 1, true, false, false, Spec);
                p.Tick(.01, Pose, 2, true, false, false, Spec);
                Check(Active(p.Simulation).Length == 0 && !p.Simulation.Bursts.Any(b => b.Active), "context reset");
                p.Tick(.01, Pose, 2, true, false, true, Spec);
                p.Tick(.01, new EmitterPose(new V3(1, 0, 0), Pose.Forward, Pose.Right), 2, true, false, false, Spec);
                Check(!p.Simulation.Bursts.Any(b => b.Active), "teleport reset");
            });
            Test("stream pool evicts oldest at four active bursts", () => {
                var sim = New(); for (int i = 0; i < 6; i++) { sim.Fire(Spec, Pose); sim.Advance(.001, Pose); }
                Check(sim.Bursts.Count(b => b.Active) == 4, "bounded stream pool");
                Check(sim.Bursts.Min(b => b.Id) == 3 && sim.Bursts.Max(b => b.Id) == 6, "oldest stream evicted");
            });
            Test("500-drop cap keeps the newest drops", () => {
                var sim = New(); var s = Spec; s.Drops = 80; s.DropLife = 3;
                for (int i = 0; i < 10; i++) { sim.Fire(s, Pose); sim.Advance(.21, Pose); }
                var drops = Active(sim); Check(drops.Length == 500 && sim.TotalDropsCreated == 800, "bounded drop pool");
                Check(drops.First().Id == 301 && drops.Last().Id == 800, "oldest drop evicted");
            });
            Test("5-second stall leaves no expired liquid", () => {
                var sim = New(); sim.Fire(Spec, Pose); sim.Advance(5, Pose);
                Check(sim.TotalDropsCreated == 49 && Active(sim).Length == 0 && !sim.Bursts.Any(b => b.Active), "stall catchup cleanup");
            });
            Test("duration and breakup endpoints including maximum settings", () => {
                foreach (float duration in new[] { .02f, .1f, .3f }) foreach (float breakup in new[] { .02f, .1f, .3f }) {
                    var s = Spec; s.Duration = duration; s.Breakup = breakup; s.Jets = 5; s.Drops = 80;
                    var sim = New(); sim.Fire(s, Pose); sim.Advance(duration + breakup + .001, Pose);
                    Check(sim.TotalDropsCreated == 80, "all scheduled drops produced");
                    Check(sim.Bursts[0].Samples <= LiquidSimulation.MaxSamples, "bounded history");
                    Near(sim.Bursts[0].Launches[0, sim.Bursts[0].Samples - 1].Born, duration, 1e-8, "exact end sample");
                }
            });
            Test("tails respect 0/5/15cm bounds and taper to transparent point", () => {
                var sim = Run(60); var path = new List<PathPoint>();
                foreach (var drop in Active(sim)) foreach (float max in new[] { 0f, .05f, .15f }) {
                    sim.DropTail(drop, max, .5f, path);
                    Check(Length(path) <= max + 1e-6f, "tail cap");
                    if (max == 0) Check(path.Count == 0, "zero tail");
                    else {
                        Check(path.Count > 1 && path[path.Count - 1].Radius == 0 && path[path.Count - 1].Alpha == 0, "tapered end");
                        for (int i = 1; i < path.Count; i++) Check(path[i].Alpha <= path[i - 1].Alpha && path[i].Radius <= path[i - 1].Radius, "monotonic taper");
                    }
                }
            });
            Test("curved tubes have eight sides and at most 5mm segment spacing", () => {
                var geometry = new TubeGeometry(); var path = new List<PathPoint>();
                for (int i = 0; i <= 8; i++) { float t = i / 8f; path.Add(new PathPoint(new V3(t, (float)Math.Sin(t * 4) * .1f, .2f * t * t), .001f)); }
                geometry.AddPath(path, true); Finite(geometry);
                Check(geometry.Vertices.Count == geometry.Resampled.Count * 8, "eight sides");
                Check(geometry.Indices.Count == (geometry.Resampled.Count - 1) * 8 * 6, "triangle count");
                for (int ring = 0; ring < geometry.Resampled.Count; ring++) for (int side = 0; side < 8; side++)
                    Near((geometry.Vertices[ring * 8 + side].Position - geometry.Resampled[ring].Position).Length, .001, 2e-7, "round cross section");
            });
            Test("separate streams never share triangles", () => {
                var g = new TubeGeometry(); g.AddPath(new List<PathPoint> { new PathPoint(default, .001f), new PathPoint(new V3(0, 0, .1f), .001f) }, false);
                int vertexStart = g.Vertices.Count, indexStart = g.Indices.Count;
                g.AddPath(new List<PathPoint> { new PathPoint(new V3(1, 0, 0), .001f), new PathPoint(new V3(1, 0, .1f), .001f) }, false);
                Check(g.Indices.Take(indexStart).All(i => i < vertexStart), "first stream isolated");
                Check(g.Indices.Skip(indexStart).All(i => i >= vertexStart), "second stream isolated");
            });
            Test("tube winding faces outward", () => {
                var g = new TubeGeometry(); g.AddPath(new List<PathPoint> { new PathPoint(default, .001f), new PathPoint(new V3(0, 0, .1f), .001f) }, false);
                for (int i = 0; i < g.Indices.Count; i += 3) {
                    var a = g.Vertices[g.Indices[i]]; var b = g.Vertices[g.Indices[i + 1]]; var c = g.Vertices[g.Indices[i + 2]];
                    Check(V3.Dot(V3.Cross(b.Position - a.Position, c.Position - a.Position), a.Normal + b.Normal + c.Normal) > 0, "outward triangle");
                }
            });
            Test("vertical and apex paths have finite stable frames", () => {
                foreach (float tilt in new[] { 0f, .00001f, -.00001f, .1f }) {
                    var path = new List<PathPoint>(); var g = new TubeGeometry();
                    for (int i = 0; i <= 48; i++) { float t = i / 240f; path.Add(new PathPoint(new V3(tilt * t, t - 5 * t * t, 0), .001f)); }
                    g.AddPath(path, true); Finite(g);
                    for (int i = 1; i < g.Resampled.Count; i++)
                        Check(V3.Dot(g.Vertices[(i - 1) * 8].Normal, g.Vertices[i * 8].Normal) >= -.001f, "no cross-section sign flip");
                }
            });
            Test("degenerate emitter axes remain orthonormal", () => {
                foreach (var forward in new[] { V3.Up, V3.Forward, default(V3) }) {
                    var p = new EmitterPose(default, forward, forward);
                    Near(V3.Dot(p.Forward, p.Right), 0, 1e-6, "orthogonal"); Near(p.Up.Length, 1, 1e-6, "valid up vector");
                }
            });
            Console.WriteLine($"SUCCESS: {_tests} offline cases. No Unity/game process started.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine($"FAIL after {_tests} cases: {e}"); return 1; }
    }
}
