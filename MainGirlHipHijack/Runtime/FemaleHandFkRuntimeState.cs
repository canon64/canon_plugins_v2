using UnityEngine;

namespace MainGirlHipHijack.Runtime
{
    internal sealed class FemaleHandFkRuntimeState
    {
        internal FemaleHandFkRuntimeState(int layerCount)
        {
            LayerRotations = new Quaternion[layerCount][];
            LayerCaptured = new bool[layerCount];
        }

        internal int TargetChaId;
        internal Transform[] Bones = new Transform[0];
        internal Quaternion[] BaseRotations = new Quaternion[0];
        internal Quaternion[][] LayerRotations;
        internal bool[] LayerCaptured;
        internal string Status = "-";
    }
}
