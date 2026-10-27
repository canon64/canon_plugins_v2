using MainGameTransformGizmo;
using UnityEngine;

namespace MainGirlHipHijack.Runtime
{
    internal sealed class FemaleHeadRuntimeState
    {
        internal bool Grabbing;
        internal Quaternion GrabControllerOffset;
        internal Quaternion AdditiveOffset;
        internal bool HasAdditive;
        internal Quaternion DesiredRotation;
        internal bool Released;
        internal Transform HeadBone;
        internal Transform ControllerTransform;
        internal bool InRange;
        internal GameObject GizmoProxyObject;
        internal Transform GizmoProxy;
        internal TransformGizmo Gizmo;
        internal bool GizmoDragging;
        internal Quaternion GizmoBaseRotation;
        internal System.Action<GizmoMode> GizmoModeHandler;
        internal string PreviousNeckTargetName;
        internal int PreviousNeckPatternNumber = -999;
        internal float OffsetWeight = 1f;
        internal float FadeStartWeight = 1f;
        internal float FadeTargetWeight = 1f;
        internal float FadeElapsed;
    }
}
