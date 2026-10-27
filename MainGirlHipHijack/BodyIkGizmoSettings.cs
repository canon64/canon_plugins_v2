using System;
using UnityEngine;

namespace MainGirlHipHijack
{
    internal enum PoseTransitionEasing
    {
        Linear = 0,
        SmoothStep = 1,
        EaseOutCubic = 2
    }

    internal enum MaleHeadBoneSelectionMode
    {
        Auto = 0,
        CmHead = 1,
        CfHead = 2
    }

    [Serializable]
    internal sealed class FemaleHandFkSettings
    {
        public bool Enabled = false;
        public FemaleHandFkLayerSettings[] Layers = new FemaleHandFkLayerSettings[]
        {
            new FemaleHandFkLayerSettings(),
            new FemaleHandFkLayerSettings(),
            new FemaleHandFkLayerSettings()
        };
    }

    [Serializable]
    internal sealed class FemaleHandFkLayerSettings
    {
        public bool Enabled = false;
        public int Index = 0;
        public float Weight = 0f;
    }

    [Serializable]
    internal sealed class BodyIkGizmoSettings
    {
        public bool UiVisible = false;
        public bool AutoEnableAllOnResolve = false;
        public bool ShowGizmo = false;
        public bool VerboseLog = false; // legacy (unused)
        public bool DetailLogEnabled = false;
        public bool BodyIkDiagnosticLog = false;
        public float BodyIkDiagnosticLogInterval = 0.2f;
        public float GizmoSizeMultiplier = 0.2f;
        public bool AutoPoseEnabled = false;
        public int AutoPoseSwitchAnimationLoops = 2;
        public float PoseTransitionSeconds = 1f;
        public PoseTransitionEasing PoseTransitionEasing = PoseTransitionEasing.SmoothStep;
        public float FollowSnapDistance = 0.3f;
        public float HmdFollowSnapDistance = 0.3f;
        public float VRGrabDistance = 0.15f;
        public float VRMarkerSize = 0.04f;
        public float FemaleHeadGrabDistance = 0.15f;
        public float BoneMarkerSize = 0.04f;

        // 女の頭角度（デスクトップ）
        public bool FemaleHeadAngleEnabled = false;
        public float FemaleHeadAngleX = 0f;
        public float FemaleHeadAngleY = 0f;
        public float FemaleHeadAngleZ = 0f;
        public bool FemaleHeadAngleGizmoVisible = false;
        public bool FemaleHeadAngleKeepOnMotionOrPostureChange = false;
        public float FemaleHeadOffsetFadeTime = 1f;
        public int   FemaleHeadOffsetEasing   = 2; // 0=Linear 1=EaseIn 2=EaseOut 3=EaseInOut

        // 追従ミラー自動化
        public bool FollowMirrorAutoEnabled = false;

        // 追従距離閾値
        public bool FollowDistanceThresholdEnabled = false;
        public float FollowDistanceThreshold = 0.3f;
        public float FollowDistanceFadeRange = 0.3f;
        public float FollowDistanceBlendSpeed = 3f;

        // VR時 頭ボーン→HMD変換
        public bool FollowHeadBoneToHmdEnabled = false;
        public bool FollowAllowAllHeadBonesForSnap = true;

        public bool SpeedHijackEnabled = false;
        public bool CutFemaleAnimSpeedEnabled = false;
        public float SpeedMoveAddPerSecond = 0.04f;
        public float SpeedDecayPerSecond = 0.08f;
        public float SpeedMovementThreshold = 0.002f;
        public float SpeedIdleDelay = 1f;
        public bool AutoInsertOnMoveEnabled = false;

        public float BodyCtrlChangeFactorX = 5f;
        public float BodyCtrlChangeFactorY = 4f;
        public float BodyCtrlChangeFactorZ = 5f;
        public float BodyCtrlDampen = 0.05f;

        public bool MaleHmdEnabled = false;
        public float MaleHmdHeadRotationWeight = 1f;
        public float MaleHmdPositionScale = 1f;
        public bool MaleHmdUseLocalDelta = true;
        public float MaleHmdLocalDeltaSmoothing = 0.35f;
        public bool MaleHmdSwapHorizontalAxes = true;
        public bool MaleHmdInvertHorizontalX = false;
        public bool MaleHmdInvertHorizontalZ = false;
        public bool MaleHeadIkEnabled = true;
        public bool MaleHeadIkGizmoEnabled = false;
        public bool MaleHeadIkGizmoVisible = true;
        public MaleHeadBoneSelectionMode MaleHeadBoneSelection = MaleHeadBoneSelectionMode.Auto;
        public float MaleHeadIkPositionWeight = 0.75f;
        public float MaleHeadIkNeckWeight = 0.8f;
        public int MaleHeadIkSolveIterations = 2;
        public float MaleHeadIkNearDistance = 0.26f;
        public float MaleHeadIkFarDistance = 0.62f;
        public float MaleHeadIkNearWaistWeight = 0.10f;
        public float MaleHeadIkNearSpine1Weight = 0.26f;
        public float MaleHeadIkNearSpine2Weight = 0.44f;
        public float MaleHeadIkNearSpine3Weight = 0.66f;
        public float MaleHeadIkNearNeckWeight = 0.90f;
        public float MaleHeadIkFarWaistWeight = 0.56f;
        public float MaleHeadIkFarSpine1Weight = 0.42f;
        public float MaleHeadIkFarSpine2Weight = 0.28f;
        public float MaleHeadIkFarSpine3Weight = 0.16f;
        public float MaleHeadIkFarNeckWeight = 0.10f;
        public bool MaleHeadIkBodyPullEnabled = true;
        public float MaleHeadIkBodyPullPositionWeight = 0.35f;
        public float MaleHeadIkBodyPullRotationWeight = 0.20f;
        public float MaleHeadIkBodyPullMaxStep = 0.06f;
        public bool MaleHeadIkCompensateBodyOffset = true;
        public float MaleHeadIkCompensateBodyOffsetWeight = 1f;
        public float MaleHeadIkCompensateBodyOffsetMax = 1.5f;
        public bool MaleHeadIkSpineBodyOffsetEnabled = true;
        public float MaleHeadIkSpineBodyOffsetSpine1Weight = 0.55f;
        public float MaleHeadIkSpineBodyOffsetSpine2Weight = 0.30f;
        public float MaleHeadIkSpineBodyOffsetSpine3Weight = 0.15f;
        public float MaleHeadIkSpineBodyOffsetMax = 0.35f;
        public bool MaleNeckShoulderFollowEnabled = true;
        public float MaleNeckShoulderFollowPositionWeight = 1f;
        public float MaleNeckShoulderFollowRotationWeight = 0.6f;
        public bool MaleSpine1MidpointEnabled = true;
        public float MaleSpine1MidpointT = 0.5f;
        public float MaleSpine1MidpointWeight = 1f;
        public float MaleSpine1MidpointRotationWeight = 0f;
        public bool MaleIkDebugVisible = true;
        public bool MaleHmdDiagnosticLog = false;
        public float MaleHmdDiagnosticLogInterval = 0.25f;
        public bool[] MaleIkEnabled = new bool[] { false, true, true, true, true, true };
        public float[] MaleIkWeights = new float[] { 0f, 0.30f, 0.45f, 0.65f, 0.85f, 1f };
        public bool MaleLeftHandFollowEnabled = false;
        public bool MaleRightHandFollowEnabled = false;
        public float MaleHandFollowSnapDistance = 0.2f;
        public bool[] MaleControlEnabled = new bool[13];
        public bool[] MaleControlGizmoVisible = new bool[]
        {
            true, true, true, true, true, true, true, true, true, true, true, true, true
        };
        public float[] MaleControlWeights = new float[]
        {
            1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f
        };

        public bool[] Enabled = new bool[12];
        public bool[] GizmoVisible = new bool[12];
        public float[] Weights = new float[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        public bool FemaleLeftHandPoseEnabled = false;
        public bool FemaleRightHandPoseEnabled = false;
        public int FemaleLeftHandPoseIndex = 0;
        public int FemaleRightHandPoseIndex = 0;
        public float FemaleLeftHandPoseWeight = 1f;
        public float FemaleRightHandPoseWeight = 1f;
        public FemaleHandFkSettings FemaleLeftHandFk = new FemaleHandFkSettings();
        public FemaleHandFkSettings FemaleRightHandFk = new FemaleHandFkSettings();

        // MainGameObjectComposer 連動: 各エフェクタが Composer オブジェクトを追従するか
        public bool[] FollowComposerEnabled = new bool[Plugin.BIK_TOTAL];
        public string[] FollowComposerObjectIds = new string[Plugin.BIK_TOTAL];
    }
}
