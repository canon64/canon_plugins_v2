using System;
using System.Collections.Generic;

namespace LiquidEffectPreview
{
    internal sealed class LiquidSimulation
    {
        public const int MaxBursts = 4, MaxDrops = 500, MaxJets = 5, MaxSamples = 74;
        public const double SampleStep = 1.0 / 240.0;
        internal sealed class Burst
        {
            public bool Active, Emitting;
            public long Id;
            public double Start, End;
            public LiquidSpec Spec;
            public int Samples, NextDrop;
            public readonly LaunchPoint[,] Launches = new LaunchPoint[MaxJets, MaxSamples];
            public readonly V3[] Directions = new V3[MaxJets];
            public readonly float[] Speeds = new float[MaxJets];
        }
        internal struct Drop
        {
            public bool Active;
            public long Id, BurstId;
            public int Jet;
            public LaunchPoint Launch;
            public double Separated, End;
            public float Gravity, Size;
            public V3 Position(double now) => Launch.Position(now, Gravity);
            public float Fade(double now) => (float)Math.Max(0, Math.Min(1,
                Math.Min((now - Separated) / .008, (End - now) / Math.Min(.1, (End - Separated) * .2))));
        }
        public readonly Burst[] Bursts = new Burst[MaxBursts];
        public readonly Drop[] Drops = new Drop[MaxDrops];
        public double Now { get; private set; }
        public int TotalDropsCreated { get; private set; }
        private EmitterPose _pose;
        private bool _hasPose;
        private long _nextBurstId, _nextDropId;
        private readonly Random _random;

        public LiquidSimulation(int seed)
        {
            _random = new Random(seed);
            for (int i = 0; i < MaxBursts; i++) Bursts[i] = new Burst();
        }
        public void Clear()
        {
            foreach (var b in Bursts) b.Active = b.Emitting = false;
            Array.Clear(Drops, 0, Drops.Length);
            _hasPose = false;
        }
        public void CancelEmission()
        {
            foreach (var b in Bursts)
                if (b.Active && b.Emitting)
                {
                    b.End = Math.Min(b.End, b.Launches[0, b.Samples - 1].Born);
                    b.Emitting = false;
                }
        }
        public void SetPose(EmitterPose pose) { _pose = pose; _hasPose = true; }
        public void Fire(LiquidSpec spec, EmitterPose pose)
        {
            spec.Jets = Math.Max(1, Math.Min(MaxJets, spec.Jets));
            spec.Drops = Math.Max(1, Math.Min(80, spec.Drops));
            spec.Duration = Math.Max(.02f, Math.Min(.3f, spec.Duration));
            spec.Breakup = Math.Max(.02f, Math.Min(.3f, spec.Breakup));
            Burst slot = null;
            foreach (var b in Bursts) if (!b.Active) { slot = b; break; }
            if (slot == null)
            {
                slot = Bursts[0];
                foreach (var b in Bursts) if (b.Id < slot.Id) slot = b;
            }
            slot.Spec = spec; slot.Id = ++_nextBurstId;
            slot.Start = Now; slot.End = Now + spec.Duration;
            slot.Active = slot.Emitting = true; slot.Samples = 0; slot.NextDrop = 0;
            for (int j = 0; j < spec.Jets; j++)
            {
                double angle = _random.NextDouble() * Math.PI * 2;
                float radius = (float)Math.Sqrt(_random.NextDouble()) * (float)Math.Tan(spec.SpreadRadians);
                // Freeze each jet's world-space direction for this burst; only the nozzle origin moves.
                slot.Directions[j] = (pose.Right * ((float)Math.Cos(angle) * radius)
                    + pose.Up * ((float)Math.Sin(angle) * radius) + pose.Forward).Unit;
                slot.Speeds[j] = spec.Speed * (.95f + (float)_random.NextDouble() * .1f);
            }
            AddSample(slot, Now, pose);
            SetPose(pose);
        }
        private static void AddSample(Burst b, double time, EmitterPose pose)
        {
            if (b.Samples >= MaxSamples) return;
            for (int j = 0; j < b.Spec.Jets; j++)
            {
                b.Launches[j, b.Samples] = new LaunchPoint { Born = time, Origin = pose.Position,
                    Velocity = b.Directions[j] * b.Speeds[j] };
            }
            b.Samples++;
        }
        public void Advance(double dt, EmitterPose pose)
        {
            if (!_hasPose) SetPose(pose);
            if (dt <= 0) { SetPose(pose); return; }
            double before = Now, after = Now + dt;
            foreach (var b in Bursts)
            {
                if (!b.Active) continue;
                // Produce the same launch samples regardless of render-frame duration.
                while (b.Emitting && b.Samples < MaxSamples)
                {
                    double next = Math.Min(b.Start + b.Samples * SampleStep, b.End);
                    if (next > after + 1e-9) break;
                    float t = (float)Math.Max(0, Math.Min(1, (next - before) / dt));
                    AddSample(b, next, EmitterPose.Lerp(_pose, pose, t));
                    if (next >= b.End - 1e-9) { b.Emitting = false; break; }
                }
            }
            // Merge breakup events chronologically, including across a long render-frame stall.
            while (true)
            {
                Burst nextBurst = null;
                double nextBorn = 0, nextSeparation = double.PositiveInfinity;
                foreach (var b in Bursts)
                {
                    if (!b.Active || b.NextDrop >= b.Spec.Drops) continue;
                    double born = b.Start + (b.NextDrop + .5) / b.Spec.Drops * b.Spec.Duration;
                    if (born > b.End + 1e-9) { b.NextDrop = b.Spec.Drops; continue; }
                    double separation = born + b.Spec.Breakup;
                    if (separation <= after + 1e-9 && (separation < nextSeparation ||
                        (separation == nextSeparation && (nextBurst == null || b.Id < nextBurst.Id))))
                    { nextBurst = b; nextBorn = born; nextSeparation = separation; }
                }
                if (nextBurst == null) break;
                int k = nextBurst.NextDrop++, jet = k % nextBurst.Spec.Jets;
                TotalDropsCreated++;
                long id = ++_nextDropId;
                if (nextSeparation + nextBurst.Spec.DropLife <= after) continue;
                int slot = FindDropSlot(after);
                Drops[slot] = new Drop { Active = true, Id = id, BurstId = nextBurst.Id, Jet = jet,
                    Launch = SampleLaunch(nextBurst, jet, nextBorn), Separated = nextSeparation,
                    End = nextSeparation + nextBurst.Spec.DropLife, Gravity = nextBurst.Spec.Gravity,
                    Size = nextBurst.Spec.DropSize * (.85f + .3f * (k % 7) / 6f) };
            }
            foreach (var b in Bursts) if (b.Active && after > b.End + b.Spec.Breakup) b.Active = false;
            Now = after; _pose = pose;
            for (int i = 0; i < Drops.Length; i++) if (Drops[i].Active && Now >= Drops[i].End) Drops[i].Active = false;
        }
        private int FindDropSlot(double now)
        {
            int oldest = 0;
            for (int i = 0; i < Drops.Length; i++)
            {
                if (!Drops[i].Active || Drops[i].End <= now) return i;
                if (Drops[i].Separated < Drops[oldest].Separated ||
                    (Drops[i].Separated == Drops[oldest].Separated && Drops[i].Id < Drops[oldest].Id)) oldest = i;
            }
            return oldest;
        }
        internal static LaunchPoint SampleLaunch(Burst b, int jet, double born)
        {
            if (b.Samples == 0) return default;
            int index = Math.Min(b.Samples - 1, Math.Max(0, (int)Math.Floor((born - b.Start) / SampleStep)));
            if (index + 1 >= b.Samples) return b.Launches[jet, index];
            LaunchPoint a = b.Launches[jet, index], c = b.Launches[jet, index + 1];
            float t = (float)((born - a.Born) / Math.Max(1e-9, c.Born - a.Born));
            return LaunchPoint.Lerp(a, c, t);
        }
        public void JetPath(Burst b, int jet, List<PathPoint> path)
        {
            path.Clear();
            if (!b.Active || b.Samples < 2) return;
            double from = Math.Max(b.Start, Now - b.Spec.Breakup);
            double to = Math.Min(b.End, b.Launches[jet, b.Samples - 1].Born);
            if (to - from < 1e-8) return;
            AddJetPoint(b, jet, from, path);
            int k = (int)Math.Floor((from - b.Start) / SampleStep) + 1;
            for (; k < b.Samples; k++)
            {
                double born = b.Launches[jet, k].Born;
                if (born >= to) break;
                AddJetPoint(b, jet, born, path);
            }
            AddJetPoint(b, jet, to, path);
        }
        private void AddJetPoint(Burst b, int jet, double born, List<PathPoint> path)
        {
            LaunchPoint p = SampleLaunch(b, jet, born);
            float phase = (float)((born - b.Start) / b.Spec.Duration);
            float envelope = (float)Math.Sqrt(Math.Max(0, Math.Sin(Math.PI * Math.Min(1, phase))));
            float tip = (float)Math.Max(0, Math.Min(1, (b.Spec.Breakup - (Now - born)) / .015));
            path.Add(new PathPoint(p.Position(Now, b.Spec.Gravity), b.Spec.Diameter * .5f * envelope * tip));
        }
        public void DropTail(Drop drop, float maxLength, float width, List<PathPoint> path)
        {
            path.Clear();
            if (!drop.Active || maxLength <= 0) return;
            double time = Now;
            V3 p = drop.Position(time);
            float remaining = maxLength;
            path.Add(new PathPoint(p, drop.Size * .5f * width * drop.Fade(Now), 1));
            // Walk back along the analytic trajectory by <= 2.5mm each step, bounded by separation.
            while (time > drop.Separated + 1e-8 && remaining > .000001f && path.Count < 128)
            {
                float speed = drop.Launch.SpeedAt(time, drop.Gravity).Length;
                double step = Math.Min(time - drop.Separated, Math.Min(1.0 / 240.0, .0025 / Math.Max(.1, speed + .1)));
                double nextTime = time - step;
                V3 next = drop.Position(nextTime);
                float distance = (next - p).Length;
                if (distance > remaining) { next = V3.Lerp(p, next, remaining / distance); distance = remaining; }
                remaining -= distance;
                float f = Math.Max(0, remaining / maxLength);
                path.Add(new PathPoint(next, drop.Size * .5f * width * f * drop.Fade(Now), f * f));
                time = nextTime; p = next;
            }
            // The detached tail always ends in a point, even immediately after breakup.
            if (path.Count > 1) { var end = path[path.Count - 1]; end.Radius = 0; end.Alpha = 0; path[path.Count - 1] = end; }
        }
    }
}
