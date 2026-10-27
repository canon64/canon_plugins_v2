using MainGameTransformGizmo;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlHipHijack.Runtime
{
    internal class MaleCharacterSession
    {
        internal ChaControl TargetMaleCha;
        internal Transform[] MaleBoneCache;
        internal Transform MaleHeadBone;
        internal string MaleHeadBoneName;
        internal Transform MaleNeckBone;
        internal Transform MaleWaistBone;
        internal Transform MaleSpine1Bone;
        internal Transform MaleSpine2Bone;
        internal Transform MaleSpineBone;
        internal Transform MaleLeftHandBone;
        internal Transform MaleRightHandBone;
        internal Transform MaleLeftFootBone;
        internal Transform MaleRightFootBone;
        internal Transform MaleLeftShoulderBone;
        internal Transform MaleRightShoulderBone;
        internal Transform MaleLeftUpperArmBone;
        internal Transform MaleRightUpperArmBone;
        internal Transform MaleLeftUpperLegBone;
        internal Transform MaleRightUpperLegBone;
        internal Transform MaleLeftLowerArmBone;
        internal Transform MaleRightLowerArmBone;
        internal Transform MaleLeftLowerLegBone;
        internal Transform MaleRightLowerLegBone;
        internal Transform MaleHipsBone;
        internal FullBodyBipedIK MaleFbbik;
        internal bool MaleFbbikInjectedByPlugin;
        internal GameObject MaleSyntheticIkTargetRootGo;
        internal Transform[] MaleSyntheticIkTargets = new Transform[Plugin.BIK_TOTAL];
        internal Transform MaleIkHipsTarget;
        internal Transform MalePvHandL;
        internal Transform MalePvHandR;
        internal Transform MalePvElboL;
        internal Transform MalePvElboR;
        internal Transform MalePvLegL;
        internal Transform MalePvLegR;
        internal Transform MalePvKneeL;
        internal Transform MalePvKneeR;
        internal Transform MalePvShoulderL;
        internal Transform MalePvShoulderR;
        internal Transform MalePvWaistL;
        internal Transform MalePvWaistR;
        internal Transform MaleLeftHandFollowBone;
        internal Transform MaleRightHandFollowBone;
        internal Vector3 MaleLeftHandFollowPosOffset;
        internal Vector3 MaleRightHandFollowPosOffset;
        internal Quaternion MaleLeftHandFollowRotOffset;
        internal Quaternion MaleRightHandFollowRotOffset;
        internal bool HasMaleHmdBaseline;
        internal Vector3 MaleHmdBaseHMDPos;
        internal Vector3 MaleHmdBaseHeadPosOffset;
        internal Quaternion MaleHmdBaseHeadRotOffset;
        internal Vector3 MaleHmdBaseSpinePos;
        internal Vector3 MaleHmdBaseBodyPos;
        internal Vector3 MaleHmdBaseBodyPosLocal;
        internal bool HasMaleNeckShoulderBaseline;
        internal Vector3 MaleLeftShoulderFromNeckPosLocal;
        internal Vector3 MaleRightShoulderFromNeckPosLocal;
        internal Quaternion MaleLeftShoulderFromNeckRotLocal;
        internal Quaternion MaleRightShoulderFromNeckRotLocal;
        internal bool HasMaleNeckShoulderPrevPose;
        internal Vector3 MaleNeckShoulderPrevPos;
        internal Quaternion MaleNeckShoulderPrevRot;
        internal bool HasMaleNeckFromHeadBaseline;
        internal Vector3 MaleNeckFromHeadPosLocal;
        internal Quaternion MaleNeckFromHeadRotLocal;
        internal bool HasMaleHmdLocalDelta;
        internal Vector3 MaleHmdLocalDeltaSmoothed;
        internal int MaleHeadBoneSelectionCached;
        internal GameObject MaleHeadTargetGo;
        internal Transform MaleHeadTarget;
        internal TransformGizmo MaleHeadTargetGizmo;
        internal System.Action<bool> MaleHeadTargetGizmoDragHandler;
        internal System.Action<GizmoMode> MaleHeadTargetGizmoModeHandler;
        internal bool MaleHeadTargetGizmoDragging;

        internal void ResetReferences()
        {
            TargetMaleCha = null;
            MaleBoneCache = null;
            MaleHeadBone = null;
            MaleHeadBoneName = null;
            MaleNeckBone = null;
            MaleWaistBone = null;
            MaleSpine1Bone = null;
            MaleSpine2Bone = null;
            MaleSpineBone = null;
            MaleLeftHandBone = null;
            MaleRightHandBone = null;
            MaleLeftFootBone = null;
            MaleRightFootBone = null;
            MaleLeftShoulderBone = null;
            MaleRightShoulderBone = null;
            MaleLeftUpperArmBone = null;
            MaleRightUpperArmBone = null;
            MaleLeftUpperLegBone = null;
            MaleRightUpperLegBone = null;
            MaleLeftLowerArmBone = null;
            MaleRightLowerArmBone = null;
            MaleLeftLowerLegBone = null;
            MaleRightLowerLegBone = null;
            MaleHipsBone = null;
            MaleFbbik = null;
            MaleFbbikInjectedByPlugin = false;
            MaleSyntheticIkTargetRootGo = null;
            if (MaleSyntheticIkTargets != null)
            {
                for (int i = 0; i < MaleSyntheticIkTargets.Length; i++)
                    MaleSyntheticIkTargets[i] = null;
            }

            MaleIkHipsTarget = null;
            MalePvHandL = null;
            MalePvHandR = null;
            MalePvElboL = null;
            MalePvElboR = null;
            MalePvLegL = null;
            MalePvLegR = null;
            MalePvKneeL = null;
            MalePvKneeR = null;
            MalePvShoulderL = null;
            MalePvShoulderR = null;
            MalePvWaistL = null;
            MalePvWaistR = null;
            MaleLeftHandFollowBone = null;
            MaleRightHandFollowBone = null;
            MaleLeftHandFollowPosOffset = Vector3.zero;
            MaleRightHandFollowPosOffset = Vector3.zero;
            MaleLeftHandFollowRotOffset = Quaternion.identity;
            MaleRightHandFollowRotOffset = Quaternion.identity;
            MaleHeadBoneSelectionCached = int.MinValue;
            MaleHeadTargetGo = null;
            MaleHeadTarget = null;
            MaleHeadTargetGizmo = null;
            MaleHeadTargetGizmoDragHandler = null;
            MaleHeadTargetGizmoModeHandler = null;
            MaleHeadTargetGizmoDragging = false;
            HasMaleHmdBaseline = false;
            MaleHmdBaseHMDPos = Vector3.zero;
            MaleHmdBaseHeadPosOffset = Vector3.zero;
            MaleHmdBaseHeadRotOffset = Quaternion.identity;
            MaleHmdBaseSpinePos = Vector3.zero;
            MaleHmdBaseBodyPos = Vector3.zero;
            MaleHmdBaseBodyPosLocal = Vector3.zero;
            HasMaleNeckShoulderBaseline = false;
            MaleLeftShoulderFromNeckPosLocal = Vector3.zero;
            MaleRightShoulderFromNeckPosLocal = Vector3.zero;
            MaleLeftShoulderFromNeckRotLocal = Quaternion.identity;
            MaleRightShoulderFromNeckRotLocal = Quaternion.identity;
            HasMaleNeckShoulderPrevPose = false;
            MaleNeckShoulderPrevPos = Vector3.zero;
            MaleNeckShoulderPrevRot = Quaternion.identity;
            HasMaleNeckFromHeadBaseline = false;
            MaleNeckFromHeadPosLocal = Vector3.zero;
            MaleNeckFromHeadRotLocal = Quaternion.identity;
            HasMaleHmdLocalDelta = false;
            MaleHmdLocalDeltaSmoothed = Vector3.zero;
        }
    }
}
