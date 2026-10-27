using UnityEngine;

namespace MainGirlHipHijack.Runtime
{
    internal sealed class BodyControlLinkState
    {
        internal Vector3 BaseProxyPosition;
        internal Quaternion BaseProxyRotation;
        internal Vector3 BaseControllerPosition;
        internal Quaternion BaseControllerRotation;
        internal Vector3 SmoothVelocity;
    }
}
