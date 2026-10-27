using MainGameTransformGizmo;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlHipHijack.Runtime
{
    internal sealed class BodyIkOperationPointState
    {
        internal bool Running;
        internal bool IsBendGoal;
        internal FullBodyBipedChain BendChain;
        internal bool HasOriginalEffectorTarget;
        internal Transform OriginalEffectorTarget;
        internal bool HasOriginalBendGoal;
        internal Transform OriginalBendGoal;
        internal GameObject FallbackEffectorTargetGo;
        internal GameObject FallbackBendGoalGo;
        internal GameObject ProxyGo;
        internal Transform Proxy;
        internal TransformGizmo Gizmo;
        internal System.Action<bool> GizmoDragHandler;
        internal System.Action<GizmoMode> GizmoModeHandler;
        internal bool GizmoDragging;
        internal float BendGoalRadius;
        internal Vector3 BendGoalLocalDirection;
        internal Transform FollowBone;
        internal Vector3 FollowBonePositionOffset;
        internal Quaternion FollowBoneRotationOffset;
        internal Transform CandidateBone;
        internal GameObject BoneMarkerGo;
        internal LineRenderer FollowLine;
        internal Quaternion GrabStartCtrlRot;
        internal Quaternion GrabStartProxyRot;
        internal GameObject VRMarkerGo;
        internal Renderer VRMarkerRend;
        internal bool HasPostDragHold;
        internal int PostDragHoldFrames;
        internal Vector3 PostDragHoldPos;
        internal Quaternion PostDragHoldRot;
        internal bool HasLateUpdateBoneCache;
        internal Vector3 LateUpdateBonePos;
        internal Quaternion LateUpdateBoneRot;
        internal bool PendingFollowRebind;
        internal Transform PendingFollowBone;
        internal bool PendingFollowHasPresetOffset;
        internal Vector3 PendingFollowPosOffset;
        internal Quaternion PendingFollowRotOffset;
        internal bool HasFollowDistanceBaseline;
        internal Vector3 FollowDistanceBaselineTargetPos;
    }
}
