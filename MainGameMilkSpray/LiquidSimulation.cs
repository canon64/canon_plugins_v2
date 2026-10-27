using System;

namespace MainGameMilkSpray
{
    internal sealed class LiquidSimulation
    {
        public const int MaxBursts = 4, MaxDrops = 500, MaxGhosts = 3000, MaxGhostsPerDrop = 12;
        internal struct ScheduledDrop
        {
            public double Born;
            public int Side;
            public V3 Direction;
            public float Speed, Size;
        }
        internal sealed class Burst
        {
            public bool Active;
            public long Id;
            public double Start, End;
            public LiquidSpec Spec;
            public int Count, Next;
            public readonly ScheduledDrop[] Schedule = new ScheduledDrop[MaxDrops];
        }
        internal struct Drop
        {
            public bool Active;
            public long Id, BurstId;
            public int BurstSlot, NextGhost;
            public LaunchPoint Launch;
            public double End;
            public float Size, Length;
            public LiquidSpec Spec;
            public V3 Position(double now) => Launch.Position(now, Spec.Gravity);
            public float Fade(double now) => (float)Math.Max(0, Math.Min(1, (End - now) / Math.Min(.1, Spec.DropLife * .2)));
        }
        internal struct Ghost
        {
            public bool Active;
            public long OwnerId;
            public int OwnerSlot, Ring;
            public double Born;
            public V3 Position, Axis;
            public float Size, Length;
        }

        public readonly Burst[] Bursts = new Burst[MaxBursts];
        public readonly Drop[] Drops = new Drop[MaxDrops];
        public readonly Ghost[] Ghosts = new Ghost[MaxGhosts];
        public double Now { get; private set; }
        private EmitterPose _left, _right;
        private bool _hasPose;
        private long _nextBurstId, _nextDropId;
        private readonly Random _random;
        private readonly int[,] _ownedGhosts = new int[MaxDrops, MaxGhostsPerDrop];
        // Indexed minimum heap: oldest ghost can be evicted without scanning 3000 entries.
        private readonly int[] _ghostHeap = new int[MaxGhosts], _heapIndex = new int[MaxGhosts], _freeGhosts = new int[MaxGhosts];
        private int _heapCount, _freeCount;

        public LiquidSimulation(int seed)
        {
            _random = new Random(seed);
            for (int i = 0; i < MaxBursts; i++) Bursts[i] = new Burst();
            Clear();
        }
        public void Clear()
        {
            foreach (var b in Bursts) { b.Active = false; b.Count = b.Next = 0; }
            Array.Clear(Drops, 0, Drops.Length);
            Array.Clear(Ghosts, 0, Ghosts.Length);
            for (int i = 0; i < MaxDrops; i++)
                for (int j = 0; j < MaxGhostsPerDrop; j++) _ownedGhosts[i, j] = -1;
            for (int i = 0; i < MaxGhosts; i++) { _freeGhosts[i] = MaxGhosts - 1 - i; _heapIndex[i] = -1; }
            _heapCount = 0; _freeCount = MaxGhosts; _hasPose = false;
        }
        public void SetPoses(EmitterPose left, EmitterPose right) { _left = left; _right = right; _hasPose = true; }

        // A single trigger reserves ONE burst slot, including both nipples.
        public void Fire(LiquidSpec spec, EmitterPose left, EmitterPose right, bool emitLeft, bool emitRight)
        {
            if (!emitLeft && !emitRight) return;
            int slot = -1;
            for (int i = 0; i < MaxBursts; i++) if (!Bursts[i].Active) { slot = i; break; }
            if (slot < 0)
            {
                slot = 0;
                for (int i = 1; i < MaxBursts; i++) if (Bursts[i].Id < Bursts[slot].Id) slot = i;
                EndBurst(slot);
            }
            Burst b = Bursts[slot];
            b.Active = true; b.Id = ++_nextBurstId; b.Start = Now;
            b.Spec = spec; b.Count = b.Next = 0; b.End = Now + spec.Duration + spec.DropLife;
            if (emitLeft) ScheduleSide(b, 0);
            if (emitRight) ScheduleSide(b, 1);
            // At most 500 entries, already ordered within each side. No per-frame allocation.
            for (int i = 1; i < b.Count; i++)
            {
                ScheduledDrop p = b.Schedule[i];
                int j = i - 1;
                while (j >= 0 && b.Schedule[j].Born > p.Born) { b.Schedule[j + 1] = b.Schedule[j]; j--; }
                b.Schedule[j + 1] = p;
            }
            if (!_hasPose) SetPoses(left, right);
        }
        private void ScheduleSide(Burst b, int side)
        {
            LiquidSpec s = b.Spec;
            int count = s.RandomCount ? _random.Next(s.CountMin, s.CountMax + 1) : s.Drops;
            count = Math.Max(1, Math.Min(250, count));
            double minCos = Math.Cos(s.SpreadRadians);
            for (int i = 0; i < count; i++)
            {
                // Stratified random emission time preserves the exact chosen count over the burst.
                double born = b.Start + (i + _random.NextDouble()) / count * s.Duration;
                double angle = _random.NextDouble() * Math.PI * 2;
                double z = 1 - _random.NextDouble() * (1 - minCos), radial = Math.Sqrt(Math.Max(0, 1 - z * z));
                b.Schedule[b.Count++] = new ScheduledDrop {
                    Born = born, Side = side,
                    Direction = new V3((float)(radial * Math.Cos(angle)), (float)(radial * Math.Sin(angle)), (float)z),
                    Speed = s.Speed * (1 + ((float)_random.NextDouble() * 2 - 1) * s.SpeedVariation),
                    Size = s.SizeMin + (s.SizeMax - s.SizeMin) * (float)_random.NextDouble()
                };
            }
        }
        private void EndBurst(int slot)
        {
            long id = Bursts[slot].Id;
            for (int i = 0; i < MaxDrops; i++) if (Drops[i].Active && Drops[i].BurstId == id) EndDrop(i);
            Bursts[slot].Active = false; Bursts[slot].Next = Bursts[slot].Count;
        }
        private void EndDrop(int slot)
        {
            for (int j = 0; j < MaxGhostsPerDrop; j++)
                if (_ownedGhosts[slot, j] >= 0) RemoveGhost(_ownedGhosts[slot, j]);
            Drops[slot].Active = false;
        }
        private int DropSlot(double born)
        {
            int oldest = 0;
            for (int i = 0; i < MaxDrops; i++)
            {
                if (!Drops[i].Active) return i;
                if (Drops[i].End <= born) { EndDrop(i); return i; }
                if (Drops[i].Id < Drops[oldest].Id) oldest = i;
            }
            EndDrop(oldest);
            return oldest;
        }

        public void Advance(double dt, EmitterPose left, EmitterPose right, MilkPerformanceLog performance = null)
        {
            if (!_hasPose) SetPoses(left, right);
            if (dt <= 0) { SetPoses(left, right); return; }
            double before = Now, after = Now + dt;
            // Merge all pending births chronologically so cap eviction is frame-rate independent.
            while (true)
            {
                int next = -1;
                double born = double.PositiveInfinity;
                for (int i = 0; i < MaxBursts; i++)
                {
                    Burst b = Bursts[i];
                    if (!b.Active || b.Next >= b.Count) continue;
                    double time = b.Schedule[b.Next].Born;
                    if (time <= after && (time < born || (time == born && (next < 0 || b.Id < Bursts[next].Id))))
                    { next = i; born = time; }
                }
                if (next < 0) break;
                Burst burst = Bursts[next];
                ScheduledDrop scheduled = burst.Schedule[burst.Next++];
                float t = (float)Math.Max(0, Math.Min(1, (born - before) / dt));
                EmitterPose raw = EmitterPose.Lerp(scheduled.Side == 0 ? _left : _right, scheduled.Side == 0 ? left : right, t);
                EmitterPose nozzle = burst.Spec.Nozzle(raw);
                V3 d = scheduled.Direction;
                int slot = DropSlot(born);
                Drops[slot] = new Drop {
                    Active = true, Id = ++_nextDropId, BurstId = burst.Id, BurstSlot = next, NextGhost = 1,
                    Launch = new LaunchPoint { Born = born, Origin = nozzle.Position,
                        Velocity = (nozzle.Right * d.X + nozzle.Up * d.Y + nozzle.Forward * d.Z).Unit * scheduled.Speed },
                    End = born + burst.Spec.DropLife, Size = scheduled.Size, Length = scheduled.Size * Math.Max(1, burst.Spec.StretchRatio), Spec = burst.Spec
                };
            }
            Now = after; SetPoses(left, right);
            for (int i = 0; i < MaxDrops; i++) if (Drops[i].Active && Now >= Drops[i].End) EndDrop(i);
            for (int i = 0; i < MaxBursts; i++)
                if (Bursts[i].Active && Bursts[i].Next >= Bursts[i].Count && Now >= Bursts[i].End) EndBurst(i);
            using (MilkPerformanceLog.Measure(performance, MilkStage.Ghosts)) UpdateGhosts();
        }
        private void UpdateGhosts()
        {
            for (int i = 0; i < MaxGhosts; i++)
            {
                Ghost g = Ghosts[i];
                if (!g.Active) continue;
                Drop d = Drops[g.OwnerSlot];
                if (!d.Active || d.Id != g.OwnerId || Now - g.Born >= d.Spec.GhostLife ||
                    (d.Position(Now) - g.Position).Length > d.Spec.GhostDistance) RemoveGhost(i);
            }
            for (int i = 0; i < MaxDrops; i++)
            {
                Drop d = Drops[i];
                LiquidSpec s = d.Spec;
                if (!d.Active || !s.Afterimages || s.GhostDistance <= 0 || s.GhostOpacity <= 0) continue;
                double interval = (double)s.GhostLife / s.GhostCount;
                int last = (int)Math.Floor((Now - d.Launch.Born) / interval + 1e-9);
                int first = Math.Max(d.NextGhost, Math.Max(1, last - s.GhostCount + 1));
                V3 current = d.Position(Now);
                // Only materialize samples which can still be visible after a stalled frame.
                for (int k = first; k <= last; k++)
                {
                    double time = d.Launch.Born + k * interval;
                    V3 position = d.Position(time);
                    if (Now - time >= s.GhostLife || (current - position).Length > s.GhostDistance) continue;
                    int ring = (k - 1) % s.GhostCount;
                    int previous = _ownedGhosts[i, ring];
                    if (previous >= 0) RemoveGhost(previous);
                    InsertGhost(new Ghost { Active = true, OwnerId = d.Id, OwnerSlot = i, Ring = ring,
                        Born = time, Position = position, Axis = d.Launch.AxisAt(time, s.Gravity),
                        Size = d.Size * d.Fade(time), Length = d.Length * d.Fade(time) });
                }
                d.NextGhost = last + 1;
                Drops[i] = d;
            }
        }

        // Newest samples win even when parents are processed out of chronological order.
        private static bool Earlier(Ghost a, Ghost b)
            => a.Born < b.Born || (a.Born == b.Born && (a.OwnerId < b.OwnerId || (a.OwnerId == b.OwnerId && a.Ring < b.Ring)));
        private bool EarlierSlot(int a, int b) => Earlier(Ghosts[a], Ghosts[b]);
        private void InsertGhost(Ghost ghost)
        {
            if (_freeCount == 0)
            {
                int oldest = _ghostHeap[0];
                if (Earlier(ghost, Ghosts[oldest])) return;
                RemoveGhost(oldest);
            }
            int slot = _freeGhosts[--_freeCount];
            Ghosts[slot] = ghost; _ownedGhosts[ghost.OwnerSlot, ghost.Ring] = slot;
            _heapIndex[slot] = _heapCount; _ghostHeap[_heapCount++] = slot;
            SiftUp(_heapCount - 1);
        }
        private void RemoveGhost(int slot)
        {
            Ghost g = Ghosts[slot];
            if (!g.Active) return;
            if (_ownedGhosts[g.OwnerSlot, g.Ring] == slot) _ownedGhosts[g.OwnerSlot, g.Ring] = -1;
            int index = _heapIndex[slot], last = _ghostHeap[--_heapCount];
            if (index < _heapCount)
            {
                _ghostHeap[index] = last; _heapIndex[last] = index;
                if (index > 0 && EarlierSlot(last, _ghostHeap[(index - 1) / 2])) SiftUp(index);
                else SiftDown(index);
            }
            _heapIndex[slot] = -1; Ghosts[slot].Active = false; _freeGhosts[_freeCount++] = slot;
        }
        private void SwapHeap(int a, int b)
        {
            int t = _ghostHeap[a]; _ghostHeap[a] = _ghostHeap[b]; _ghostHeap[b] = t;
            _heapIndex[_ghostHeap[a]] = a; _heapIndex[_ghostHeap[b]] = b;
        }
        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!EarlierSlot(_ghostHeap[i], _ghostHeap[parent])) break;
                SwapHeap(i, parent); i = parent;
            }
        }
        private void SiftDown(int i)
        {
            while (i * 2 + 1 < _heapCount)
            {
                int child = i * 2 + 1;
                if (child + 1 < _heapCount && EarlierSlot(_ghostHeap[child + 1], _ghostHeap[child])) child++;
                if (!EarlierSlot(_ghostHeap[child], _ghostHeap[i])) break;
                SwapHeap(i, child); i = child;
            }
        }
    }
}

