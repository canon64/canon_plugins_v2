namespace MainGameMilkSpray
{
    // No game-specific state: a camera/nozzle context, an explicit burst request, and a pause clock.
    internal sealed class LiquidPlayback
    {
        public readonly LiquidSimulation Simulation;
        private long _context;
        private bool _hasContext, _wasHeld;
        private EmitterPose _lastPose;

        public LiquidPlayback(int seed) { Simulation = new LiquidSimulation(seed); }
        public void Clear()
        {
            Simulation.Clear(); _hasContext = false;
        }
        public void Tick(double dt, EmitterPose pose, long context, bool enabled, bool paused, bool fireHeld, LiquidSpec spec)
        {
            bool request = fireHeld && !_wasHeld;
            _wasHeld = fireHeld;
            if (!enabled) { Clear(); return; }
            if (!_hasContext || context != _context || (pose.Position - _lastPose.Position).Length > .25f)
                Simulation.Clear();
            _context = context; _hasContext = true; _lastPose = pose;
            if (paused) { Simulation.SetPose(pose); return; }
            Simulation.Advance(dt, pose);
            if (request) Simulation.Fire(spec, pose);
        }
    }
}
