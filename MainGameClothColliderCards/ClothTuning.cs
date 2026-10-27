using System;
using UnityEngine;

namespace MainGameClothColliderCards
{
    public sealed class ClothTuning
    {
        public float PinnedTop = 0.12f;
        public float HemDistance = 0.65f;
        public float Damping = 0.01f;
        public float Friction = 0.1f;
        public float Stretch = 1f;
        public float Bend = 0f;
        public float SolverHz = 90f;
        public float ExtraDown = 3f;
        public float WorldVelocity = 1f;
        public float WorldAcceleration = 1f;
        public bool Gravity = true;
        public bool UseTethers = true;

        public ClothTuning Copy() => (ClothTuning)MemberwiseClone();

        public void Clamp()
        {
            PinnedTop = Mathf.Clamp(PinnedTop, 0.01f, 0.3f);
            HemDistance = Mathf.Clamp(HemDistance, 0.02f, 1f);
            Damping = Mathf.Clamp01(Damping);
            Friction = Mathf.Clamp01(Friction);
            Stretch = Mathf.Clamp01(Stretch);
            Bend = Mathf.Clamp01(Bend);
            SolverHz = Mathf.Clamp(SolverHz, 30f, 240f);
            ExtraDown = Mathf.Clamp(ExtraDown, 0f, 20f);
            WorldVelocity = Mathf.Clamp01(WorldVelocity);
            WorldAcceleration = Mathf.Clamp01(WorldAcceleration);
        }

        public bool SameAs(ClothTuning other)
        {
            return other != null && PinnedTop == other.PinnedTop && HemDistance == other.HemDistance &&
                   Damping == other.Damping && Friction == other.Friction && Stretch == other.Stretch &&
                   Bend == other.Bend && SolverHz == other.SolverHz && ExtraDown == other.ExtraDown &&
                   WorldVelocity == other.WorldVelocity && WorldAcceleration == other.WorldAcceleration &&
                   Gravity == other.Gravity && UseTethers == other.UseTethers;
        }
    }
}
