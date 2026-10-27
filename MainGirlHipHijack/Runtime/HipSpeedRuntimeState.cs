using UnityEngine;

namespace MainGirlHipHijack.Runtime
{
    internal sealed class HipSpeedRuntimeState
    {
        internal Vector3 LastTrackingPosition;
        internal bool HasLastTrackingPosition;
        internal float LastMovementTime;
        internal bool IsMoving;
        internal bool SpeedBootstrapSent;
        internal bool InsertBootstrapSent;
    }
}
