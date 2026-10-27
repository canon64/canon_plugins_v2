namespace MainGirlHipHijack.Runtime
{
    internal sealed class HSceneSessionState
    {
        internal HSceneProc Process;
        internal bool AutoEnabledInCurrentScene;
        internal bool HasAnimatorStateHash;
        internal int AnimatorStateHash;
        internal bool HasMotionStrength;
        internal string MotionStrengthTag;
        internal bool HasCurrentAnimationInfo;
        internal int CurrentAnimationInfoId;
        internal int CurrentAnimationInfoMode;
        internal string CurrentAnimationInfoName;
        internal bool HasSelectedAnimationInfo;
        internal int SelectedAnimationInfoId;
        internal int SelectedAnimationInfoMode;
        internal string SelectedAnimationInfoName;
    }
}
