using System;

namespace MainGameObjectComposer
{
    // Only identities are captured. Bone positions must be read by BP at replay time.
    // Unity object liveness and current agent membership are checked by the caller.
    internal struct PenetrationFollowContext
    {
        internal object Scene;
        internal object Female;
        internal object Male;
        internal object FemaleBody;
        internal object MaleBody;
        internal object ReferenceTarget;
        internal object ComposerTarget;
        internal object Animation;
        internal object Preset;

        internal bool Matches(PenetrationFollowContext other)
        {
            return ReferenceEquals(Scene, other.Scene) && ReferenceEquals(Female, other.Female)
                && ReferenceEquals(Male, other.Male) && ReferenceEquals(FemaleBody, other.FemaleBody)
                && ReferenceEquals(MaleBody, other.MaleBody)
                && ReferenceEquals(ReferenceTarget, other.ReferenceTarget)
                && ReferenceEquals(ComposerTarget, other.ComposerTarget)
                && ReferenceEquals(Animation, other.Animation) && ReferenceEquals(Preset, other.Preset);
        }
    }
}
