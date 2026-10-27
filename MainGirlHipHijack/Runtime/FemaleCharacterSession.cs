using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlHipHijack.Runtime
{
    internal sealed class FemaleCharacterSession
    {
        internal ChaControl Character;
        internal Animator AnimationBody;
        internal FullBodyBipedIK FullBodyIk;
        internal Transform[] BoneCache;
    }
}
