using MainGirlHipHijack.Runtime;

namespace MainGirlHipHijack.Services
{
    internal sealed class HSceneSessionTracker
    {
        internal HSceneSessionState Session { get; } = new HSceneSessionState();
        internal FemaleCharacterSession Female { get; } = new FemaleCharacterSession();

        internal void Clear(string unknownMotionStrength)
        {
            Session.Process = null;
            Session.AutoEnabledInCurrentScene = false;
            Session.HasAnimatorStateHash = false;
            Session.AnimatorStateHash = 0;
            Session.HasMotionStrength = false;
            Session.MotionStrengthTag = unknownMotionStrength;
            Session.HasCurrentAnimationInfo = false;
            Session.CurrentAnimationInfoId = 0;
            Session.CurrentAnimationInfoMode = 0;
            Session.CurrentAnimationInfoName = null;
            Session.HasSelectedAnimationInfo = false;
            Session.SelectedAnimationInfoId = 0;
            Session.SelectedAnimationInfoMode = 0;
            Session.SelectedAnimationInfoName = null;

            Female.Character = null;
            Female.AnimationBody = null;
            Female.FullBodyIk = null;
            Female.BoneCache = null;
        }
    }
}
