using UnityEngine;
using RootMotion;
using RootMotion.FinalIK;
using VRGIN.Core;
using MainGameTransformGizmo;
namespace MainGirlHipHijack
{
    /// <summary>
    /// 男キャラ上半身 HMD 追従。
    /// cf_j_spine03（肩の親）をデルタ追従、cf_j_head を回転上書き。
    /// 腰から下・手はアニメーションそのまま。
    /// OnAfterHSceneLateUpdate から毎フレーム呼ぶ。
    /// </summary>
    public sealed partial class Plugin
    {
        internal const int MALE_IK_BONE_TOTAL = 6;
        private const int MALE_IK_WAIST = 0;
        private const int MALE_IK_SPINE1 = 1;
        private const int MALE_IK_SPINE2 = 2;
        private const int MALE_IK_SPINE3 = 3;
        private const int MALE_IK_NECK = 4;
        private const int MALE_IK_HEAD = 5;

        private static readonly string[] MaleIkLabels =
        {
            "腰", "spine01", "spine02", "spine03", "首", "頭"
        };

        private static readonly string[] MaleHeadBoneSelectionLabels =
        {
            "自動", "cm_j_head", "cf_j_head"
        };

        private static readonly string[] MaleSyntheticTargetNamesByIndex =
        {
            "cf_t_hand_L",
            "cf_t_hand_R",
            "cf_t_leg_L",
            "cf_t_leg_R",
            "cf_t_shoulder_L",
            "cf_t_shoulder_R",
            "cf_t_waist_L",
            "cf_t_waist_R",
            "cf_t_elbo_L",
            "cf_t_elbo_R",
            "cf_t_knee_L",
            "cf_t_knee_R",
            "cf_t_hips"
        };

        private readonly GameObject[] _maleIkDebugMarkers = new GameObject[MALE_IK_BONE_TOTAL];

        private sealed class MaleControlState
        {
            public GameObject ProxyGo;
            public Transform Proxy;
            public TransformGizmo Gizmo;
            public System.Action<bool> GizmoDragHandler;
            public System.Action<GizmoMode> GizmoModeHandler;
            public bool GizmoDragging;
        }

        private readonly MaleControlState[] _maleControlStates = new MaleControlState[BIK_TOTAL];
        private float _nextMaleHmdDiagLogTime;
        private bool _maleHmdDiagHasPrev;
        private Vector3 _maleHmdDiagPrevHmdPos;
        private Vector3 _maleHmdDiagPrevHeadPos;
        private float _nextMaleControlDiagLogTime;
        private bool _maleControlDiagHasPrev;
        private int _maleControlDiagPrevIndex = -1;
        private int _maleControlDiagPrevFrame = -1;
        private Vector3 _maleControlDiagPrevBonePos;
        private Vector3 _maleControlDiagPrevProxyPos;
        private string _maleResolvedRefsSnapshot;
        private bool _maleLegacyControlSuppressedByHmd;
        private bool _maleFeaturesSealedApplied;

        private void UpdateMaleHMD()
        {
            if (MaleFeaturesTemporarilySealed)
            {
                if (!_maleFeaturesSealedApplied)
                {
                    if (_settings != null)
                        _settings.MaleHmdEnabled = false;
                    ClearMaleRefs();
                    _maleFeaturesSealedApplied = true;
                }
                return;
            }

            _maleFeaturesSealedApplied = false;
            ResolveMaleRefs();
            UpdateMaleIkDebugMarkers();
            EnsureMaleHeadTargetGizmo();
            UpdateMaleHeadTargetGizmoVisibility();
            UpdateMaleHandFollow();
            bool hmdEnabled = _settings != null && _settings.MaleHmdEnabled;
            if (hmdEnabled)
                SuppressLegacyMaleControlLayerForHmdMode();
            else
            {
                _maleLegacyControlSuppressedByHmd = false;
                ApplyMaleControlOverrides();
            }

            if (_settings == null || !_settings.MaleHmdEnabled)
            {
                _maleHmdDiagHasPrev = false;
                _maleSession.HasMaleNeckShoulderPrevPose = false;
                return;
            }

            if (_maleSession.MaleSpineBone == null || _maleSession.MaleHeadBone == null)
            {
                LogMaleHmdSkipDiagnostics("refs-missing");
                _maleSession.HasMaleNeckShoulderPrevPose = false;
                return;
            }

            if (!TryGetMaleHeadSourcePose(out Vector3 sourcePos, out Quaternion sourceRot, out bool sourceFromGizmo, out string sourceUnavailableReason))
            {
            LogMaleHmdSkipDiagnostics("source-unavailable:" + sourceUnavailableReason);
                _maleSession.HasMaleNeckShoulderPrevPose = false;
                return;
            }

            Vector3 spineBefore = _maleSession.MaleSpineBone != null ? _maleSession.MaleSpineBone.position : Vector3.zero;
            Vector3 headBefore = _maleSession.MaleHeadBone != null ? _maleSession.MaleHeadBone.position : Vector3.zero;
            Vector3 targetHeadPos = _maleSession.MaleHeadBone != null
                ? (sourcePos + _maleSession.MaleHmdBaseHeadPosOffset)
                : sourcePos;
            Transform maleRoot = _maleSession.TargetMaleCha != null ? _maleSession.TargetMaleCha.transform : null;

            // ベースライン取得（初回 or リセット後）
            if (!_maleSession.HasMaleHmdBaseline)
            {
                _maleSession.MaleHmdBaseHMDPos   = sourcePos;
                _maleSession.MaleHmdBaseSpinePos = _maleSession.MaleSpineBone.position;
                _maleSession.MaleHmdBaseHeadPosOffset = _maleSession.MaleHeadBone != null
                    ? (_maleSession.MaleHeadBone.position - sourcePos)
                    : Vector3.zero;
                _maleSession.MaleHmdBaseHeadRotOffset = _maleSession.MaleHeadBone != null
                    ? (Quaternion.Inverse(sourceRot) * _maleSession.MaleHeadBone.rotation)
                    : Quaternion.identity;
                Transform bodySource = GetMaleControlSourceBoneByIndex(BIK_BODY);
                if (bodySource != null)
                    _maleSession.MaleHmdBaseBodyPos = bodySource.position;
                else if (_maleSession.MaleWaistBone != null)
                    _maleSession.MaleHmdBaseBodyPos = _maleSession.MaleWaistBone.position;
                else if (_maleSession.MaleSpine1Bone != null)
                    _maleSession.MaleHmdBaseBodyPos = _maleSession.MaleSpine1Bone.position;
                else
                    _maleSession.MaleHmdBaseBodyPos = _maleSession.MaleSpineBone != null ? _maleSession.MaleSpineBone.position : Vector3.zero;
                if (maleRoot != null)
                    _maleSession.MaleHmdBaseBodyPosLocal = maleRoot.InverseTransformPoint(_maleSession.MaleHmdBaseBodyPos);
                else
                    _maleSession.MaleHmdBaseBodyPosLocal = Vector3.zero;
                _maleSession.HasMaleNeckShoulderPrevPose = false;
                if (_maleSession.MaleHeadBone != null && _maleSession.MaleNeckBone != null)
                {
                    _maleSession.MaleNeckFromHeadPosLocal =
                        Quaternion.Inverse(_maleSession.MaleHeadBone.rotation) * (_maleSession.MaleNeckBone.position - _maleSession.MaleHeadBone.position);
                    _maleSession.MaleNeckFromHeadRotLocal =
                        Quaternion.Inverse(_maleSession.MaleHeadBone.rotation) * _maleSession.MaleNeckBone.rotation;
                    _maleSession.HasMaleNeckFromHeadBaseline = true;
                }
                else
                {
                    _maleSession.MaleNeckFromHeadPosLocal = Vector3.zero;
                    _maleSession.MaleNeckFromHeadRotLocal = Quaternion.identity;
                    _maleSession.HasMaleNeckFromHeadBaseline = false;
                }
                _maleSession.HasMaleHmdBaseline  = true;
                _maleSession.HasMaleHmdLocalDelta = false;
                LogDebug("[MaleHMD] baseline captured spine=" + _maleSession.MaleHmdBaseSpinePos + " source=" + (sourceFromGizmo ? "gizmo" : "hmd"));
                targetHeadPos = sourcePos + _maleSession.MaleHmdBaseHeadPosOffset;
            }
            Vector3 worldDeltaRaw = sourcePos - _maleSession.MaleHmdBaseHMDPos;
            bool useLocalDelta = _settings.MaleHmdUseLocalDelta && maleRoot != null;
            Vector3 localDeltaRaw = useLocalDelta ? maleRoot.InverseTransformVector(worldDeltaRaw) : worldDeltaRaw;
            Vector3 localDeltaMapped = localDeltaRaw;
            if (_settings.MaleHmdSwapHorizontalAxes)
            {
                float tmp = localDeltaMapped.x;
                localDeltaMapped.x = localDeltaMapped.z;
                localDeltaMapped.z = tmp;
            }
            if (_settings.MaleHmdInvertHorizontalX)
                localDeltaMapped.x = -localDeltaMapped.x;
            if (_settings.MaleHmdInvertHorizontalZ)
                localDeltaMapped.z = -localDeltaMapped.z;
            if (!_maleSession.HasMaleHmdLocalDelta)
            {
                _maleSession.MaleHmdLocalDeltaSmoothed = localDeltaMapped;
                _maleSession.HasMaleHmdLocalDelta = true;
            }
            else
            {
                float response = Mathf.Clamp01(_settings.MaleHmdLocalDeltaSmoothing);
                _maleSession.MaleHmdLocalDeltaSmoothed = Vector3.Lerp(_maleSession.MaleHmdLocalDeltaSmoothed, localDeltaMapped, response);
            }

            Vector3 worldDeltaApplied = useLocalDelta
                ? maleRoot.TransformVector(_maleSession.MaleHmdLocalDeltaSmoothed)
                : _maleSession.MaleHmdLocalDeltaSmoothed;

            // 旧挙動互換: 頭IKを使わない場合のみ spine03 へ位置デルタを直接適用。
            // 頭IK使用時は、首/背骨は頭ターゲットへの連鎖結果として動かす。
            float posScale = _settings.MaleHmdPositionScale;
            float spinePosWeight = 0f;
            if (posScale != 0f && !_settings.MaleHeadIkEnabled)
            {
                Vector3 delta = worldDeltaApplied * posScale;
                spinePosWeight = 1f;
                if (spinePosWeight > 0f)
                    _maleSession.MaleSpineBone.position = _maleSession.MaleHmdBaseSpinePos + (delta * spinePosWeight);
            }

            if (_settings.MaleHeadIkEnabled)
            {
                ApplyMaleHeadIk(sourcePos, sourceRot);
            }
            else
            {
                // 旧挙動: 頭の回転のみ追従
                float rotW = _settings.MaleHmdHeadRotationWeight * GetMaleIkWeight(MALE_IK_HEAD);
                if (_maleSession.MaleHeadBone != null && rotW > 0f)
                {
                    Quaternion targetHeadRot = sourceRot * _maleSession.MaleHmdBaseHeadRotOffset;
                    _maleSession.MaleHeadBone.rotation = Quaternion.Slerp(_maleSession.MaleHeadBone.rotation, targetHeadRot, rotW);
                }
            }

            LogMaleHmdDiagnostics(
                sourcePos,
                targetHeadPos,
                spineBefore,
                headBefore,
                posScale,
                spinePosWeight,
                useLocalDelta,
                worldDeltaRaw,
                localDeltaRaw,
                localDeltaMapped,
                _maleSession.MaleHmdLocalDeltaSmoothed,
                worldDeltaApplied,
                sourceFromGizmo);
        }

        private bool TryGetMaleHeadSourcePose(out Vector3 sourcePos, out Quaternion sourceRot, out bool sourceFromGizmo, out string unavailableReason)
        {
            sourcePos = Vector3.zero;
            sourceRot = Quaternion.identity;
            sourceFromGizmo = false;
            unavailableReason = null;

            if (_settings != null && _settings.MaleHeadIkGizmoEnabled)
            {
                if (_maleSession.MaleHeadTarget == null)
                {
                    unavailableReason = "gizmo-target-missing";
                    return false;
                }

                sourcePos = _maleSession.MaleHeadTarget.position;
                sourceRot = _maleSession.MaleHeadTarget.rotation;
                sourceFromGizmo = true;
                return true;
            }

            if (!VR.Active)
            {
                unavailableReason = "vr-inactive";
                return false;
            }

            uint hmdIdx = _steamVrPoseProvider.GetHmdDeviceIndex();
            if (!_steamVrPoseProvider.TryGetDevicePose(hmdIdx, out sourcePos, out sourceRot))
            {
                unavailableReason = "hmd-pose-missing";
                return false;
            }

            return true;
        }

        private void ResolveMaleRefs()
        {
            if (_hSceneSession.Process == null)
                return;

            if (_maleSession.TargetMaleCha == null)
            {
                _maleSession.TargetMaleCha = ResolveMainMale(_hSceneSession.Process);
                if (_maleSession.TargetMaleCha != null)
                {
                    _maleSession.HasMaleNeckShoulderBaseline = false;
                    LogInfo("[MaleHMD] male ready: " + (_maleSession.TargetMaleCha.name ?? "(unnamed)"));
                }
            }

            if (_maleSession.TargetMaleCha == null)
                return;

            if (_maleSession.MaleBoneCache == null)
            {
                Transform root = _maleSession.TargetMaleCha.objBodyBone != null
                    ? _maleSession.TargetMaleCha.objBodyBone.transform
                    : _maleSession.TargetMaleCha.transform;
                if (root != null)
                {
                    _maleSession.MaleBoneCache = root.GetComponentsInChildren<Transform>(true);
                    LogDebug("[MaleHMD] bone cache ready count=" + _maleSession.MaleBoneCache.Length);
                }
            }

            if (_maleSession.MaleBoneCache == null)
                return;

            int selection = (int)GetMaleHeadBoneSelection();
            if (_maleSession.MaleHeadBoneSelectionCached != selection)
            {
                _maleSession.MaleHeadBoneSelectionCached = selection;
                _maleSession.MaleHeadBone = null;
                _maleSession.MaleHeadBoneName = null;
                _maleSession.HasMaleHmdBaseline = false;
                _maleSession.HasMaleHmdLocalDelta = false;
                _maleSession.HasMaleNeckShoulderBaseline = false;
                _maleSession.HasMaleNeckShoulderPrevPose = false;
                _maleSession.HasMaleNeckFromHeadBaseline = false;
                _maleResolvedRefsSnapshot = null;
                LogInfo("[MaleHMD] head bone selection changed -> " + GetMaleHeadBoneSelectionLabel());
            }

            if (_maleSession.MaleWaistBone == null)
                _maleSession.MaleWaistBone = FindFirstBoneInCache(_maleSession.MaleBoneCache,
                    "cf_j_waist01", "cf_J_Waist01", "cm_j_waist01", "cm_J_Waist01");
            if (_maleSession.MaleSpine1Bone == null)
                _maleSession.MaleSpine1Bone = FindFirstBoneInCache(_maleSession.MaleBoneCache,
                    "cf_j_spine01", "cf_J_Spine01", "cm_j_spine01", "cm_J_Spine01");
            if (_maleSession.MaleSpine2Bone == null)
                _maleSession.MaleSpine2Bone = FindFirstBoneInCache(_maleSession.MaleBoneCache,
                    "cf_j_spine02", "cf_J_Spine02", "cm_j_spine02", "cm_J_Spine02");
            if (_maleSession.MaleSpineBone == null)
                _maleSession.MaleSpineBone = FindFirstBoneInCache(_maleSession.MaleBoneCache,
                    "cf_j_spine03", "cf_J_Spine03", "cm_j_spine03", "cm_J_Spine03");
            if (_maleSession.MaleNeckBone == null)
                _maleSession.MaleNeckBone = FindFirstBoneInCache(_maleSession.MaleBoneCache,
                    "cf_j_neck", "cf_J_Neck", "cm_j_neck", "cm_J_Neck");
            if (_maleSession.MaleHeadBone == null)
            {
                _maleSession.MaleHeadBone = ResolveMaleHeadBoneBySelection(_maleSession.MaleBoneCache, _maleSession.MaleNeckBone);
                _maleSession.MaleHeadBoneName = _maleSession.MaleHeadBone != null ? _maleSession.MaleHeadBone.name : null;
                if (_maleSession.MaleHeadBone != null)
                    LogInfo("[MaleHMD] head bone resolved: " + _maleSession.MaleHeadBoneName);
            }
            if (_maleSession.MaleLeftHandBone == null)
                _maleSession.MaleLeftHandBone = FindFirstBoneInCache(_maleSession.MaleBoneCache,
                    "cf_j_hand_L", "cf_J_Hand_L", "cm_j_hand_L", "cm_J_Hand_L");
            if (_maleSession.MaleRightHandBone == null)
                _maleSession.MaleRightHandBone = FindFirstBoneInCache(_maleSession.MaleBoneCache,
                    "cf_j_hand_R", "cf_J_Hand_R", "cm_j_hand_R", "cm_J_Hand_R");

            Animator animator = _maleSession.TargetMaleCha.animBody != null
                ? _maleSession.TargetMaleCha.animBody.GetComponent<Animator>()
                : _maleSession.TargetMaleCha.GetComponentInChildren<Animator>(true);
            if (animator == null)
                animator = _maleSession.TargetMaleCha.GetComponentInChildren<Animator>(true);

            if (_maleSession.MaleLeftFootBone == null)
                _maleSession.MaleLeftFootBone = GetHumanoidBone(animator, HumanBodyBones.LeftFoot)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_foot_L", "cm_J_Foot_L", "cf_j_foot_L", "cf_J_Foot_L");
            if (_maleSession.MaleRightFootBone == null)
                _maleSession.MaleRightFootBone = GetHumanoidBone(animator, HumanBodyBones.RightFoot)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_foot_R", "cm_J_Foot_R", "cf_j_foot_R", "cf_J_Foot_R");
            if (_maleSession.MaleLeftShoulderBone == null)
                _maleSession.MaleLeftShoulderBone = GetHumanoidBone(animator, HumanBodyBones.LeftShoulder)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_shoulder_L", "cm_J_Shoulder_L", "cf_j_shoulder_L", "cf_J_Shoulder_L");
            if (_maleSession.MaleRightShoulderBone == null)
                _maleSession.MaleRightShoulderBone = GetHumanoidBone(animator, HumanBodyBones.RightShoulder)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_shoulder_R", "cm_J_Shoulder_R", "cf_j_shoulder_R", "cf_J_Shoulder_R");
            if (_maleSession.MaleLeftUpperArmBone == null)
                _maleSession.MaleLeftUpperArmBone = GetHumanoidBone(animator, HumanBodyBones.LeftUpperArm)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_arm00_L", "cm_j_arm01_L", "cf_j_arm00_L", "cf_j_arm01_L");
            if (_maleSession.MaleRightUpperArmBone == null)
                _maleSession.MaleRightUpperArmBone = GetHumanoidBone(animator, HumanBodyBones.RightUpperArm)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_arm00_R", "cm_j_arm01_R", "cf_j_arm00_R", "cf_j_arm01_R");
            if (_maleSession.MaleLeftUpperLegBone == null)
                _maleSession.MaleLeftUpperLegBone = GetHumanoidBone(animator, HumanBodyBones.LeftUpperLeg)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_thigh00_L", "cm_j_thigh01_L", "cf_j_thigh00_L", "cf_j_thigh01_L");
            if (_maleSession.MaleRightUpperLegBone == null)
                _maleSession.MaleRightUpperLegBone = GetHumanoidBone(animator, HumanBodyBones.RightUpperLeg)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_thigh00_R", "cm_j_thigh01_R", "cf_j_thigh00_R", "cf_j_thigh01_R");
            if (_maleSession.MaleLeftLowerArmBone == null)
                _maleSession.MaleLeftLowerArmBone = GetHumanoidBone(animator, HumanBodyBones.LeftLowerArm)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_forearm01_L", "cm_j_forearm02_L", "cf_j_forearm01_L", "cf_j_forearm02_L");
            if (_maleSession.MaleRightLowerArmBone == null)
                _maleSession.MaleRightLowerArmBone = GetHumanoidBone(animator, HumanBodyBones.RightLowerArm)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_forearm01_R", "cm_j_forearm02_R", "cf_j_forearm01_R", "cf_j_forearm02_R");
            if (_maleSession.MaleLeftLowerLegBone == null)
                _maleSession.MaleLeftLowerLegBone = GetHumanoidBone(animator, HumanBodyBones.LeftLowerLeg)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_leg01_L", "cm_j_leg02_L", "cf_j_leg01_L", "cf_j_leg02_L");
            if (_maleSession.MaleRightLowerLegBone == null)
                _maleSession.MaleRightLowerLegBone = GetHumanoidBone(animator, HumanBodyBones.RightLowerLeg)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_leg01_R", "cm_j_leg02_R", "cf_j_leg01_R", "cf_j_leg02_R");
            if (_maleSession.MaleHipsBone == null)
                _maleSession.MaleHipsBone = GetHumanoidBone(animator, HumanBodyBones.Hips)
                    ?? FindFirstBoneInCache(_maleSession.MaleBoneCache, "cm_j_waist01", "cm_J_Waist01", "cf_j_waist01", "cf_J_Waist01");

            if (_maleSession.MaleIkHipsTarget == null)
                _maleSession.MaleIkHipsTarget = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_ik_hips");
            if (_maleSession.MalePvHandL == null)
                _maleSession.MalePvHandL = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_hand_L");
            if (_maleSession.MalePvHandR == null)
                _maleSession.MalePvHandR = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_hand_R");
            if (_maleSession.MalePvElboL == null)
                _maleSession.MalePvElboL = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_elbo_L");
            if (_maleSession.MalePvElboR == null)
                _maleSession.MalePvElboR = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_elbo_R");
            if (_maleSession.MalePvLegL == null)
                _maleSession.MalePvLegL = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_leg_L");
            if (_maleSession.MalePvLegR == null)
                _maleSession.MalePvLegR = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_leg_R");
            if (_maleSession.MalePvKneeL == null)
                _maleSession.MalePvKneeL = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_knee_L");
            if (_maleSession.MalePvKneeR == null)
                _maleSession.MalePvKneeR = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_knee_R");
            if (_maleSession.MalePvShoulderL == null)
                _maleSession.MalePvShoulderL = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_shoulder_L");
            if (_maleSession.MalePvShoulderR == null)
                _maleSession.MalePvShoulderR = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_shoulder_R");
            if (_maleSession.MalePvWaistL == null)
                _maleSession.MalePvWaistL = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_waist_L");
            if (_maleSession.MalePvWaistR == null)
                _maleSession.MalePvWaistR = FindFirstBoneInCache(_maleSession.MaleBoneCache, "cf_pv_waist_R");

            EnsureMaleFullBodyIkReady();
            EnsureMaleSyntheticTargetsReady();

            LogMaleResolvedRefsIfChanged();
        }

        private bool EnsureMaleFullBodyIkReady()
        {
            if (_maleSession.TargetMaleCha == null)
                return false;

            if (_maleSession.MaleFbbik != null)
                return true;

            FullBodyBipedIK existing = ResolveFbbik(_maleSession.TargetMaleCha);
            if (existing != null)
            {
                _maleSession.MaleFbbik = existing;
                _maleSession.MaleFbbikInjectedByPlugin = false;
            }
            else
            {
                Transform host = _maleSession.TargetMaleCha.animBody != null
                    ? _maleSession.TargetMaleCha.animBody.transform
                    : _maleSession.TargetMaleCha.transform;
                if (host == null)
                    return false;

                _maleSession.MaleFbbik = host.gameObject.AddComponent<FullBodyBipedIK>();
                _maleSession.MaleFbbikInjectedByPlugin = _maleSession.MaleFbbik != null;
                if (_maleSession.MaleFbbik != null)
                    LogInfo("[MaleIK] FullBodyBipedIK created on male body host");
            }

            if (_maleSession.MaleFbbik == null)
                return false;

            if (!ConfigureMaleFullBodyIkReferences(_maleSession.MaleFbbik))
            {
                if (_maleSession.MaleFbbikInjectedByPlugin)
                    Destroy(_maleSession.MaleFbbik);
                _maleSession.MaleFbbik = null;
                _maleSession.MaleFbbikInjectedByPlugin = false;
                return false;
            }

            _maleSession.MaleFbbik.fixTransforms = false;
            _maleSession.MaleFbbik.enabled = true;
            return true;
        }

        private bool ConfigureMaleFullBodyIkReferences(FullBodyBipedIK maleIk)
        {
            if (maleIk == null)
                return false;

            BipedReferences refs = BuildMaleBipedReferences();
            if (refs == null)
                return false;

            string setupError = string.Empty;
            if (BipedReferences.SetupError(refs, ref setupError))
            {
                LogWarn("[MaleIK] references invalid: " + setupError);
                LogMaleHmdDiag("[MaleIK-DETAIL] references invalid: " + setupError + " refs=" + BuildMaleBipedReferenceSummary(refs));
                return false;
            }

            Transform rootNode = refs.pelvis;
            if (rootNode == null && refs.spine != null && refs.spine.Length > 0)
                rootNode = refs.spine[0];
            if (rootNode == null)
            {
                LogWarn("[MaleIK] root node missing for male FullBodyBipedIK");
                return false;
            }

            try
            {
                maleIk.SetReferences(refs, rootNode);
                maleIk.solver.IKPositionWeight = 1f;
                LogMaleHmdDiag("[MaleIK-DETAIL] configured fullbody references rootNode="
                    + (rootNode != null ? rootNode.name : "(null)")
                    + " refs=" + BuildMaleBipedReferenceSummary(refs));
                return true;
            }
            catch (System.Exception ex)
            {
                LogError("[MaleIK] SetReferences failed: " + ex.Message);
                LogMaleHmdDiag("[MaleIK-DETAIL] SetReferences failed: " + ex);
                return false;
            }
        }

        private BipedReferences BuildMaleBipedReferences()
        {
            Transform root = _maleSession.TargetMaleCha != null
                ? (_maleSession.TargetMaleCha.animBody != null
                    ? _maleSession.TargetMaleCha.animBody.transform
                    : _maleSession.TargetMaleCha.transform)
                : null;
            if (root == null)
                return null;

            var refs = new BipedReferences();
            refs.root = root;
            refs.pelvis = _maleSession.MaleHipsBone != null ? _maleSession.MaleHipsBone : _maleSession.MaleWaistBone;
            refs.leftThigh = _maleSession.MaleLeftUpperLegBone;
            refs.leftCalf = _maleSession.MaleLeftLowerLegBone;
            refs.leftFoot = _maleSession.MaleLeftFootBone;
            refs.rightThigh = _maleSession.MaleRightUpperLegBone;
            refs.rightCalf = _maleSession.MaleRightLowerLegBone;
            refs.rightFoot = _maleSession.MaleRightFootBone;
            refs.leftUpperArm = _maleSession.MaleLeftUpperArmBone != null ? _maleSession.MaleLeftUpperArmBone : _maleSession.MaleLeftShoulderBone;
            refs.leftForearm = _maleSession.MaleLeftLowerArmBone;
            refs.leftHand = _maleSession.MaleLeftHandBone;
            refs.rightUpperArm = _maleSession.MaleRightUpperArmBone != null ? _maleSession.MaleRightUpperArmBone : _maleSession.MaleRightShoulderBone;
            refs.rightForearm = _maleSession.MaleRightLowerArmBone;
            refs.rightHand = _maleSession.MaleRightHandBone;
            refs.head = _maleSession.MaleHeadBone;
            refs.eyes = new Transform[0];

            // spine は常時 spine01 -> spine02 -> spine03 -> neck の順で使う。
            // フォールバック運用は行わず、取得できたものを順にチェーンへ含める。
            var spineList = new System.Collections.Generic.List<Transform>(4);
            AddUniqueSpineRef(spineList, _maleSession.MaleSpine1Bone, refs.pelvis);
            AddUniqueSpineRef(spineList, _maleSession.MaleSpine2Bone, refs.pelvis);
            AddUniqueSpineRef(spineList, _maleSession.MaleSpineBone, refs.pelvis);
            AddUniqueSpineRef(spineList, _maleSession.MaleNeckBone, refs.pelvis);
            refs.spine = spineList.ToArray();
            return refs;
        }

        private static void AddUniqueSpineRef(System.Collections.Generic.List<Transform> list, Transform candidate, Transform pelvis)
        {
            if (list == null || candidate == null || ReferenceEquals(candidate, pelvis))
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], candidate))
                    return;
            }
            list.Add(candidate);
        }

        private static string BuildMaleBipedReferenceSummary(BipedReferences refs)
        {
            if (refs == null)
                return "(null)";

            string spine = "-";
            if (refs.spine != null && refs.spine.Length > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < refs.spine.Length; i++)
                {
                    if (i > 0)
                        sb.Append("|");
                    sb.Append(refs.spine[i] != null ? refs.spine[i].name : "(null)");
                }
                spine = sb.ToString();
            }

            return "root=" + (refs.root != null ? refs.root.name : "(null)")
                + ", pelvis=" + (refs.pelvis != null ? refs.pelvis.name : "(null)")
                + ", lArm=" + (refs.leftUpperArm != null ? refs.leftUpperArm.name : "(null)") + "/" + (refs.leftForearm != null ? refs.leftForearm.name : "(null)") + "/" + (refs.leftHand != null ? refs.leftHand.name : "(null)")
                + ", rArm=" + (refs.rightUpperArm != null ? refs.rightUpperArm.name : "(null)") + "/" + (refs.rightForearm != null ? refs.rightForearm.name : "(null)") + "/" + (refs.rightHand != null ? refs.rightHand.name : "(null)")
                + ", lLeg=" + (refs.leftThigh != null ? refs.leftThigh.name : "(null)") + "/" + (refs.leftCalf != null ? refs.leftCalf.name : "(null)") + "/" + (refs.leftFoot != null ? refs.leftFoot.name : "(null)")
                + ", rLeg=" + (refs.rightThigh != null ? refs.rightThigh.name : "(null)") + "/" + (refs.rightCalf != null ? refs.rightCalf.name : "(null)") + "/" + (refs.rightFoot != null ? refs.rightFoot.name : "(null)")
                + ", head=" + (refs.head != null ? refs.head.name : "(null)")
                + ", spine=" + spine;
        }

        private void EnsureMaleSyntheticTargetsReady()
        {
            if (_maleSession.TargetMaleCha == null)
                return;

            if (_maleSession.MaleSyntheticIkTargets == null || _maleSession.MaleSyntheticIkTargets.Length != BIK_TOTAL)
                _maleSession.MaleSyntheticIkTargets = new Transform[BIK_TOTAL];

            if (_maleSession.MaleSyntheticIkTargetRootGo == null)
            {
                Transform parent = _maleSession.TargetMaleCha.objBodyBone != null
                    ? _maleSession.TargetMaleCha.objBodyBone.transform
                    : _maleSession.TargetMaleCha.transform;
                if (parent == null)
                    return;

                GameObject rootGo = new GameObject("__MaleSyntheticIkTargets");
                rootGo.hideFlags = HideFlags.HideAndDontSave;
                rootGo.transform.SetParent(parent, false);
                _maleSession.MaleSyntheticIkTargetRootGo = rootGo;
            }

            int createdCount = 0;
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                Transform target = _maleSession.MaleSyntheticIkTargets[i];
                bool created = false;
                if (target == null)
                {
                    string targetName = MaleSyntheticTargetNamesByIndex[i];
                    target = FindFirstBoneInCache(_maleSession.MaleBoneCache, targetName);
                    if (target == null && _maleSession.MaleSyntheticIkTargetRootGo != null)
                    {
                        GameObject go = new GameObject(targetName);
                        go.hideFlags = HideFlags.HideAndDontSave;
                        go.transform.SetParent(_maleSession.MaleSyntheticIkTargetRootGo.transform, false);
                        target = go.transform;
                        created = true;
                    }
                    _maleSession.MaleSyntheticIkTargets[i] = target;
                }

                Transform sourceBone = GetMaleControlSourceBoneByIndex(i);
                if (target != null && sourceBone != null && (created || !GetMaleControlEnabled(i)))
                    target.SetPositionAndRotation(sourceBone.position, sourceBone.rotation);

                if (created)
                    createdCount++;
            }

            if (createdCount > 0)
                LogMaleHmdDiag("[MaleIK-DETAIL] synthetic cf_t targets created count=" + createdCount);
        }

        private MaleHeadBoneSelectionMode GetMaleHeadBoneSelection()
        {
            if (_settings == null)
                return MaleHeadBoneSelectionMode.Auto;
            return _settings.MaleHeadBoneSelection;
        }

        internal string GetMaleHeadBoneSelectionLabel()
        {
            int idx = Mathf.Clamp((int)GetMaleHeadBoneSelection(), 0, MaleHeadBoneSelectionLabels.Length - 1);
            return MaleHeadBoneSelectionLabels[idx];
        }

        internal void SetMaleHeadBoneSelection(MaleHeadBoneSelectionMode nextSelection)
        {
            if (_settings == null)
                return;
            if (_settings.MaleHeadBoneSelection == nextSelection)
                return;

            _settings.MaleHeadBoneSelection = nextSelection;
            _maleSession.MaleHeadBoneSelectionCached = int.MinValue;
            _maleSession.MaleHeadBone = null;
            _maleSession.MaleHeadBoneName = null;
            _maleSession.HasMaleHmdBaseline = false;
            _maleSession.HasMaleHmdLocalDelta = false;
            _maleSession.HasMaleNeckShoulderBaseline = false;
            _maleSession.HasMaleNeckShoulderPrevPose = false;
            _maleSession.HasMaleNeckFromHeadBaseline = false;
            ResolveMaleRefs();
            if (_settings.MaleHeadIkGizmoEnabled)
                SnapMaleHeadTargetToCurrentHead();
            SaveSettings();
            LogInfo("[MaleHMD] head bone selection set: " + GetMaleHeadBoneSelectionLabel());
        }

        internal void SetMaleHeadIkGizmoEnabled(bool enabled)
        {
            if (_settings == null)
                return;
            if (_settings.MaleHeadIkGizmoEnabled == enabled)
                return;

            _settings.MaleHeadIkGizmoEnabled = enabled;
            if (enabled)
            {
                ResolveMaleRefs();
                EnsureMaleHeadTargetGizmo();
                SnapMaleHeadTargetToCurrentHead();
            }
            else
            {
                if (_maleSession.MaleHeadTargetGizmoDragging)
                    OnMaleHeadTargetGizmoDragStateChanged(false);
            }

            _maleSession.HasMaleHmdBaseline = false;
            _maleSession.HasMaleHmdLocalDelta = false;
            _maleSession.HasMaleNeckShoulderPrevPose = false;
            _maleSession.HasMaleNeckFromHeadBaseline = false;
            UpdateMaleHeadTargetGizmoVisibility();
            SaveSettings();
            LogInfo("[MaleHMD] head target gizmo: " + (enabled ? "ON" : "OFF"));
        }

        private void EnsureMaleHeadTargetGizmo()
        {
            if (_settings == null || !_settings.MaleHeadIkGizmoEnabled)
                return;
            if (_maleSession.MaleHeadBone == null && _maleSession.TargetMaleCha == null)
                return;

            if (_maleSession.MaleHeadTargetGo == null)
            {
                GameObject go = new GameObject("__MaleHeadIkTarget");
                go.hideFlags = HideFlags.HideAndDontSave;
                _maleSession.MaleHeadTargetGo = go;
                _maleSession.MaleHeadTarget = go.transform;
                SnapMaleHeadTargetToCurrentHead();
            }

            if (_maleSession.MaleHeadTargetGizmo == null && _maleSession.MaleHeadTargetGo != null)
            {
                _maleSession.MaleHeadTargetGizmo = TransformGizmoApi.Attach(_maleSession.MaleHeadTargetGo);
                if (_maleSession.MaleHeadTargetGizmo != null)
                {
                    ApplyConfiguredGizmoSize(_maleSession.MaleHeadTargetGizmo);
                    EnforceNoScaleMode(_maleSession.MaleHeadTargetGizmo);
                    _maleSession.MaleHeadTargetGizmoDragHandler = dragging => OnMaleHeadTargetGizmoDragStateChanged(dragging);
                    _maleSession.MaleHeadTargetGizmoModeHandler = CreateNoScaleModeHandler(_maleSession.MaleHeadTargetGizmo);
                    _maleSession.MaleHeadTargetGizmo.DragStateChanged += _maleSession.MaleHeadTargetGizmoDragHandler;
                    _maleSession.MaleHeadTargetGizmo.ModeChanged += _maleSession.MaleHeadTargetGizmoModeHandler;
                }
            }

            UpdateMaleHeadTargetGizmoVisibility();
        }

        private void SnapMaleHeadTargetToCurrentHead()
        {
            if (_maleSession.MaleHeadTarget == null)
                return;

            Transform src = _maleSession.MaleHeadBone;
            if (src == null)
                src = _maleSession.MaleNeckBone != null ? _maleSession.MaleNeckBone : _maleSession.MaleSpineBone;
            if (src == null && _maleSession.TargetMaleCha != null)
                src = _maleSession.TargetMaleCha.transform;
            if (src == null)
                return;

            _maleSession.MaleHeadTarget.SetPositionAndRotation(src.position, src.rotation);
        }

        private void UpdateMaleHeadTargetGizmoVisibility()
        {
            if (_maleSession.MaleHeadTargetGizmo == null || _settings == null)
                return;

            bool visible = _settings.MaleHeadIkGizmoEnabled
                && _settings.MaleHeadIkGizmoVisible;
            _maleSession.MaleHeadTargetGizmo.SetVisible(visible);
        }

        private void DestroyMaleHeadTargetGizmo()
        {
            bool wasDragging = _maleSession.MaleHeadTargetGizmoDragging;

            if (_maleSession.MaleHeadTargetGizmo != null && _maleSession.MaleHeadTargetGizmoDragHandler != null)
                _maleSession.MaleHeadTargetGizmo.DragStateChanged -= _maleSession.MaleHeadTargetGizmoDragHandler;
            if (_maleSession.MaleHeadTargetGizmo != null && _maleSession.MaleHeadTargetGizmoModeHandler != null)
                _maleSession.MaleHeadTargetGizmo.ModeChanged -= _maleSession.MaleHeadTargetGizmoModeHandler;

            _maleSession.MaleHeadTargetGizmoDragHandler = null;
            _maleSession.MaleHeadTargetGizmoModeHandler = null;
            _maleSession.MaleHeadTargetGizmo = null;
            _maleSession.MaleHeadTargetGizmoDragging = false;

            if (_maleSession.MaleHeadTargetGo != null)
                Destroy(_maleSession.MaleHeadTargetGo);
            _maleSession.MaleHeadTargetGo = null;
            _maleSession.MaleHeadTarget = null;

            if (wasDragging)
                OnMaleHeadTargetGizmoDragStateChanged(false);
            else
                RecomputeGizmoDraggingState();
        }

        private Transform ResolveMaleHeadBoneBySelection(Transform[] cache, Transform neck)
        {
            Transform cmHead = FindFirstBoneInCache(cache, "cm_j_head", "cm_J_Head");
            Transform cfHead = FindFirstBoneInCache(cache, "cf_j_head", "cf_J_Head");

            switch (GetMaleHeadBoneSelection())
            {
                case MaleHeadBoneSelectionMode.CmHead:
                    return cmHead ?? cfHead;
                case MaleHeadBoneSelectionMode.CfHead:
                    return cfHead ?? cmHead;
                default:
                    if (cmHead != null && cfHead != null && neck != null)
                    {
                        float cmDist = (cmHead.position - neck.position).sqrMagnitude;
                        float cfDist = (cfHead.position - neck.position).sqrMagnitude;
                        return cmDist <= cfDist ? cmHead : cfHead;
                    }
                    return cmHead ?? cfHead;
            }
        }

        private static Transform GetHumanoidBone(Animator animator, HumanBodyBones bone)
        {
            if (animator == null || !animator.isHuman)
                return null;
            if (bone == HumanBodyBones.LastBone)
                return null;
            return animator.GetBoneTransform(bone);
        }

        private MaleControlState GetMaleControlState(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return null;
            MaleControlState state = _maleControlStates[idx];
            if (state == null)
            {
                state = new MaleControlState();
                _maleControlStates[idx] = state;
            }
            return state;
        }

        private Transform GetMaleControlBoneByIndex(int idx)
        {
            return GetMaleControlSourceBoneByIndex(idx);
        }

        private Transform GetMaleControlSourceBoneByIndex(int idx)
        {
            switch (idx)
            {
                case BIK_LH: return _maleSession.MaleLeftHandBone != null ? _maleSession.MaleLeftHandBone : _maleSession.MalePvHandL;
                case BIK_RH: return _maleSession.MaleRightHandBone != null ? _maleSession.MaleRightHandBone : _maleSession.MalePvHandR;
                case BIK_LF: return _maleSession.MaleLeftFootBone != null ? _maleSession.MaleLeftFootBone : _maleSession.MalePvLegL;
                case BIK_RF: return _maleSession.MaleRightFootBone != null ? _maleSession.MaleRightFootBone : _maleSession.MalePvLegR;
                case BIK_LS: return _maleSession.MaleLeftShoulderBone != null ? _maleSession.MaleLeftShoulderBone : _maleSession.MalePvShoulderL;
                case BIK_RS: return _maleSession.MaleRightShoulderBone != null ? _maleSession.MaleRightShoulderBone : _maleSession.MalePvShoulderR;
                case BIK_LT: return _maleSession.MaleLeftUpperLegBone != null ? _maleSession.MaleLeftUpperLegBone : _maleSession.MalePvWaistL;
                case BIK_RT: return _maleSession.MaleRightUpperLegBone != null ? _maleSession.MaleRightUpperLegBone : _maleSession.MalePvWaistR;
                case BIK_LE: return _maleSession.MaleLeftLowerArmBone != null ? _maleSession.MaleLeftLowerArmBone : _maleSession.MalePvElboL;
                case BIK_RE: return _maleSession.MaleRightLowerArmBone != null ? _maleSession.MaleRightLowerArmBone : _maleSession.MalePvElboR;
                case BIK_LK: return _maleSession.MaleLeftLowerLegBone != null ? _maleSession.MaleLeftLowerLegBone : _maleSession.MalePvKneeL;
                case BIK_RK: return _maleSession.MaleRightLowerLegBone != null ? _maleSession.MaleRightLowerLegBone : _maleSession.MalePvKneeR;
                case BIK_BODY:
                    if (_maleSession.MaleHipsBone != null)
                        return _maleSession.MaleHipsBone;
                    if (_maleSession.MaleWaistBone != null)
                        return _maleSession.MaleWaistBone;
                    if (_maleSession.MaleSpine1Bone != null)
                        return _maleSession.MaleSpine1Bone;
                    return _maleSession.MaleIkHipsTarget;
                default: return null;
            }
        }

        private Transform GetMaleControlTargetByIndex(int idx)
        {
            if (_maleSession.MaleSyntheticIkTargets != null
                && idx >= 0
                && idx < _maleSession.MaleSyntheticIkTargets.Length
                && _maleSession.MaleSyntheticIkTargets[idx] != null)
            {
                return _maleSession.MaleSyntheticIkTargets[idx];
            }

            switch (idx)
            {
                case BIK_LH: return _maleSession.MalePvHandL;
                case BIK_RH: return _maleSession.MalePvHandR;
                case BIK_LF: return _maleSession.MalePvLegL;
                case BIK_RF: return _maleSession.MalePvLegR;
                case BIK_LS: return _maleSession.MalePvShoulderL;
                case BIK_RS: return _maleSession.MalePvShoulderR;
                case BIK_LT: return _maleSession.MalePvWaistL;
                case BIK_RT: return _maleSession.MalePvWaistR;
                case BIK_LE: return _maleSession.MalePvElboL;
                case BIK_RE: return _maleSession.MalePvElboR;
                case BIK_LK: return _maleSession.MalePvKneeL;
                case BIK_RK: return _maleSession.MalePvKneeR;
                case BIK_BODY: return _maleSession.MaleIkHipsTarget;
                default: return null;
            }
        }

        private IKEffector GetMaleControlEffector(int idx)
        {
            if (_maleSession.MaleFbbik == null)
                return null;

            switch (idx)
            {
                case BIK_LH: return _maleSession.MaleFbbik.solver.GetEffector(FullBodyBipedEffector.LeftHand);
                case BIK_RH: return _maleSession.MaleFbbik.solver.GetEffector(FullBodyBipedEffector.RightHand);
                case BIK_LF: return _maleSession.MaleFbbik.solver.GetEffector(FullBodyBipedEffector.LeftFoot);
                case BIK_RF: return _maleSession.MaleFbbik.solver.GetEffector(FullBodyBipedEffector.RightFoot);
                case BIK_LS: return _maleSession.MaleFbbik.solver.GetEffector(FullBodyBipedEffector.LeftShoulder);
                case BIK_RS: return _maleSession.MaleFbbik.solver.GetEffector(FullBodyBipedEffector.RightShoulder);
                case BIK_LT: return _maleSession.MaleFbbik.solver.GetEffector(FullBodyBipedEffector.LeftThigh);
                case BIK_RT: return _maleSession.MaleFbbik.solver.GetEffector(FullBodyBipedEffector.RightThigh);
                case BIK_BODY: return _maleSession.MaleFbbik.solver.bodyEffector;
                default: return null;
            }
        }

        private IKConstraintBend GetMaleControlBendConstraint(int idx)
        {
            if (_maleSession.MaleFbbik == null)
                return null;

            switch (idx)
            {
                case BIK_LE: return _maleSession.MaleFbbik.solver.GetBendConstraint(FullBodyBipedChain.LeftArm);
                case BIK_RE: return _maleSession.MaleFbbik.solver.GetBendConstraint(FullBodyBipedChain.RightArm);
                case BIK_LK: return _maleSession.MaleFbbik.solver.GetBendConstraint(FullBodyBipedChain.LeftLeg);
                case BIK_RK: return _maleSession.MaleFbbik.solver.GetBendConstraint(FullBodyBipedChain.RightLeg);
                default: return null;
            }
        }

        private void ApplyMaleControlSolverBinding(int idx, bool enabled, float weight, Transform target)
        {
            float w = enabled ? Mathf.Clamp01(weight) : 0f;

            if (idx >= BIK_BEND_START && idx != BIK_BODY)
            {
                IKConstraintBend bc = GetMaleControlBendConstraint(idx);
                if (bc == null)
                    return;
                if (target != null && !ReferenceEquals(bc.bendGoal, target))
                    bc.bendGoal = target;
                bc.weight = w;
                return;
            }

            IKEffector eff = GetMaleControlEffector(idx);
            if (eff == null)
                return;
            if (target != null && !ReferenceEquals(eff.target, target))
                eff.target = target;
            eff.positionWeight = w;
            eff.rotationWeight = IsRotationDrivenEffector(idx) ? w : 0f;
        }

        private bool IsMaleControlPositionDriven(int idx)
        {
            return idx >= 0 && idx < BIK_TOTAL;
        }

        private bool GetMaleControlEnabled(int idx)
        {
            if (_settings == null || _settings.MaleControlEnabled == null || idx < 0 || idx >= _settings.MaleControlEnabled.Length)
                return false;
            return _settings.MaleControlEnabled[idx];
        }

        private void SetMaleControlEnabled(int idx, bool enabled)
        {
            if (_settings == null || _settings.MaleControlEnabled == null || idx < 0 || idx >= _settings.MaleControlEnabled.Length)
                return;
            if (_settings.MaleControlEnabled[idx] == enabled)
                return;

            _settings.MaleControlEnabled[idx] = enabled;
            if (enabled)
            {
                EnsureMaleControlProxy(idx);
                // Enable時のみ現在アニメ姿勢へ初期同期する。
                ResetMaleControlPartToAnimationPose(idx);
                UpdateMaleControlGizmoVisibility(idx);
            }
            else
            {
                UpdateMaleControlGizmoVisibility(idx);
            }

            SaveSettings();
        }

        private bool GetMaleControlGizmoVisible(int idx)
        {
            if (_settings == null || _settings.MaleControlGizmoVisible == null || idx < 0 || idx >= _settings.MaleControlGizmoVisible.Length)
                return true;
            return _settings.MaleControlGizmoVisible[idx];
        }

        private void SetMaleControlGizmoVisible(int idx, bool visible)
        {
            if (_settings == null || _settings.MaleControlGizmoVisible == null || idx < 0 || idx >= _settings.MaleControlGizmoVisible.Length)
                return;
            if (_settings.MaleControlGizmoVisible[idx] == visible)
                return;
            _settings.MaleControlGizmoVisible[idx] = visible;
            UpdateMaleControlGizmoVisibility(idx);
            SaveSettings();
        }

        private void SetAllMaleControlGizmoVisible(bool visible)
        {
            if (_settings == null || _settings.MaleControlGizmoVisible == null)
                return;

            int count = Mathf.Min(BIK_TOTAL, _settings.MaleControlGizmoVisible.Length);
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                if (_settings.MaleControlGizmoVisible[i] == visible)
                    continue;
                _settings.MaleControlGizmoVisible[i] = visible;
                changed = true;
            }

            if (!changed)
                return;

            UpdateAllMaleControlGizmoVisibility();
            SaveSettings();
        }

        private void SetAllMaleControlEnabled(bool enabled)
        {
            if (_settings == null || _settings.MaleControlEnabled == null)
                return;

            int count = Mathf.Min(BIK_TOTAL, _settings.MaleControlEnabled.Length);
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                if (_settings.MaleControlEnabled[i] == enabled)
                    continue;

                _settings.MaleControlEnabled[i] = enabled;
                if (enabled)
                {
                    EnsureMaleControlProxy(i);
                    ResetMaleControlPartToAnimationPose(i);
                }
                UpdateMaleControlGizmoVisibility(i);
                changed = true;
            }

            if (!changed)
                return;

            SaveSettings();
        }

        private void ResetAllMaleControlPartsToAnimationPose()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
                ResetMaleControlPartToAnimationPose(i);
        }

        private float GetMaleControlWeight(int idx)
        {
            if (_settings == null || _settings.MaleControlWeights == null || idx < 0 || idx >= _settings.MaleControlWeights.Length)
                return 1f;
            return Mathf.Clamp01(_settings.MaleControlWeights[idx]);
        }

        private void SetMaleControlWeight(int idx, float weight)
        {
            if (_settings == null || _settings.MaleControlWeights == null || idx < 0 || idx >= _settings.MaleControlWeights.Length)
                return;
            float clamped = Mathf.Clamp01(weight);
            if (Mathf.Approximately(_settings.MaleControlWeights[idx], clamped))
                return;
            _settings.MaleControlWeights[idx] = clamped;
            SaveSettings();
        }

        private bool IsMaleControlGizmoVisible(int idx)
        {
            return _settings != null
                && GetMaleControlEnabled(idx)
                && GetMaleControlGizmoVisible(idx);
        }

        private void UpdateAllMaleControlGizmoVisibility()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
                UpdateMaleControlGizmoVisibility(i);
        }

        private void UpdateMaleControlGizmoVisibility(int idx)
        {
            MaleControlState state = GetMaleControlState(idx);
            if (state == null || state.Gizmo == null)
                return;
            state.Gizmo.SetVisible(IsMaleControlGizmoVisible(idx));
        }

        private void EnsureMaleControlProxy(int idx)
        {
            if (!GetMaleControlEnabled(idx))
                return;

            Transform bone = GetMaleControlBoneByIndex(idx);
            if (bone == null)
                return;

            MaleControlState state = GetMaleControlState(idx);
            if (state == null)
                return;

            if (state.ProxyGo == null)
            {
                GameObject go = new GameObject("__MaleCtrlProxy_" + idx);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetPositionAndRotation(bone.position, bone.rotation);
                state.ProxyGo = go;
                state.Proxy = go.transform;
            }

            if (state.Gizmo == null && state.ProxyGo != null)
            {
                state.Gizmo = TransformGizmoApi.Attach(state.ProxyGo);
                if (state.Gizmo != null)
                {
                    ApplyConfiguredGizmoSize(state.Gizmo);
                    EnforceNoScaleMode(state.Gizmo);
                    int capturedIdx = idx;
                    state.GizmoDragHandler = dragging => OnMaleControlGizmoDragStateChanged(capturedIdx, dragging);
                    state.GizmoModeHandler = CreateNoScaleModeHandler(state.Gizmo);
                    state.Gizmo.DragStateChanged += state.GizmoDragHandler;
                    state.Gizmo.ModeChanged += state.GizmoModeHandler;
                }
            }

            UpdateMaleControlGizmoVisibility(idx);
        }

        private void OnMaleControlGizmoDragStateChanged(int idx, bool dragging)
        {
            MaleControlState state = GetMaleControlState(idx);
            if (state == null || state.GizmoDragging == dragging)
                return;
            state.GizmoDragging = dragging;
            LogInfo("male control gizmo drag " + (dragging ? "ON" : "OFF") + " idx=" + idx);
            RecomputeGizmoDraggingState();
        }

        private struct MaleControlDiagSnapshot
        {
            public int Index;
            public string Label;
            public bool Enabled;
            public bool PositionDriven;
            public float Weight;
            public bool BoneExists;
            public bool ProxyExists;
            public Vector3 BonePos;
            public Vector3 ProxyPos;
            public float DistToProxy;
        }

        private bool ShouldLogMaleControlDiagnostics(out int idx)
        {
            idx = -1;
            if (_settings == null || !_settings.MaleHmdDiagnosticLog)
            {
                _maleControlDiagHasPrev = false;
                return false;
            }

            float interval = Mathf.Clamp(_settings.MaleHmdDiagnosticLogInterval, 0.05f, 2f);
            if (Time.unscaledTime < _nextMaleControlDiagLogTime)
                return false;

            _nextMaleControlDiagLogTime = Time.unscaledTime + interval;
            idx = ResolveMaleControlDiagnosticIndex();
            return idx >= 0;
        }

        private int ResolveMaleControlDiagnosticIndex()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!GetMaleControlEnabled(i))
                    continue;

                MaleControlState state = _maleControlStates[i];
                if (state != null && state.GizmoDragging)
                    return i;
            }

            if (GetMaleControlEnabled(BIK_BODY))
                return BIK_BODY;

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (GetMaleControlEnabled(i))
                    return i;
            }

            return -1;
        }

        private bool TryCaptureMaleControlDiagSnapshot(int idx, out MaleControlDiagSnapshot snap)
        {
            snap = default(MaleControlDiagSnapshot);
            if (idx < 0 || idx >= BIK_TOTAL)
                return false;

            snap.Index = idx;
            snap.Label = BIK_Labels[idx];
            snap.Enabled = GetMaleControlEnabled(idx);
            snap.PositionDriven = IsMaleControlPositionDriven(idx);
            snap.Weight = GetMaleControlWeight(idx);

            Transform bone = GetMaleControlBoneByIndex(idx);
            MaleControlState state = _maleControlStates[idx];
            Transform proxy = state != null ? state.Proxy : null;

            snap.BoneExists = bone != null;
            snap.ProxyExists = proxy != null;
            snap.BonePos = bone != null ? bone.position : Vector3.zero;
            snap.ProxyPos = proxy != null ? proxy.position : Vector3.zero;
            snap.DistToProxy = (bone != null && proxy != null)
                ? Vector3.Distance(bone.position, proxy.position)
                : -1f;
            return true;
        }

        private void LogMaleControlDiagnostics(MaleControlDiagSnapshot before, MaleControlDiagSnapshot after)
        {
            bool hasCarry = _maleControlDiagHasPrev
                && _maleControlDiagPrevIndex == before.Index
                && _maleControlDiagPrevFrame == Time.frameCount - 1;
            float carryBone = hasCarry ? Vector3.Distance(_maleControlDiagPrevBonePos, before.BonePos) : -1f;
            float carryProxy = hasCarry ? Vector3.Distance(_maleControlDiagPrevProxyPos, before.ProxyPos) : -1f;

            string verdict = BuildMaleControlDiagVerdict(before, after, hasCarry, carryBone, carryProxy);
            string msg = "[MaleCTRL-DIAG] frame=" + Time.frameCount
                + " idx=" + before.Index + "(" + before.Label + ")"
                + " verdict=" + verdict
                + " enabled=" + before.Enabled
                + " posDriven=" + before.PositionDriven
                + " weight=" + before.Weight.ToString("F3")
                + " boneExists=" + before.BoneExists
                + " proxyExists=" + before.ProxyExists
                + " bone(before->after)=" + Vec3(before.BonePos) + "->" + Vec3(after.BonePos)
                + " proxy(before->after)=" + Vec3(before.ProxyPos) + "->" + Vec3(after.ProxyPos)
                + " dist(before->after)=" + before.DistToProxy.ToString("F4") + "->" + after.DistToProxy.ToString("F4")
                + " carry(bone/proxy)=" + carryBone.ToString("F4") + "/" + carryProxy.ToString("F4");
            LogMaleHmdDiag(msg);

            if (after.BoneExists && after.ProxyExists)
            {
                _maleControlDiagHasPrev = true;
                _maleControlDiagPrevIndex = after.Index;
                _maleControlDiagPrevFrame = Time.frameCount;
                _maleControlDiagPrevBonePos = after.BonePos;
                _maleControlDiagPrevProxyPos = after.ProxyPos;
            }
            else
            {
                _maleControlDiagHasPrev = false;
            }
        }

        private static string BuildMaleControlDiagVerdict(
            MaleControlDiagSnapshot before,
            MaleControlDiagSnapshot after,
            bool hasCarry,
            float carryBone,
            float carryProxy)
        {
            if (!before.Enabled)
                return "skip:disabled";
            if (before.Weight <= 0.0001f)
                return "skip:weight-zero";
            if (!before.BoneExists || !after.BoneExists)
                return "skip:bone-missing";
            if (!before.ProxyExists || !after.ProxyExists)
                return "skip:proxy-missing";
            if (hasCarry && carryProxy <= 0.001f && carryBone >= 0.01f)
                return "suspect:overwritten-between-frames";
            if (after.DistToProxy < before.DistToProxy - 0.001f)
                return "apply:path-active";
            return "warn:no-approach";
        }

        private void ApplyMaleControlOverrides()
        {
            if (_settings == null)
                return;

            int diagIdx;
            bool doDiag = ShouldLogMaleControlDiagnostics(out diagIdx);
            MaleControlDiagSnapshot diagBefore = default(MaleControlDiagSnapshot);
            if (doDiag && !TryCaptureMaleControlDiagSnapshot(diagIdx, out diagBefore))
                doDiag = false;

            bool solverReady = EnsureMaleFullBodyIkReady();
            if (solverReady)
            {
                EnsureMaleSyntheticTargetsReady();
                if (_maleSession.MaleFbbik != null && !_maleSession.MaleFbbik.enabled)
                    _maleSession.MaleFbbik.enabled = true;
            }

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                bool enabled = GetMaleControlEnabled(i);
                float w = GetMaleControlWeight(i);
                Transform sourceBone = GetMaleControlSourceBoneByIndex(i);
                Transform target = GetMaleControlTargetByIndex(i);

                if (!enabled)
                {
                    UpdateMaleControlGizmoVisibility(i);
                    if (target != null && sourceBone != null)
                        target.SetPositionAndRotation(sourceBone.position, sourceBone.rotation);
                    if (solverReady)
                        ApplyMaleControlSolverBinding(i, enabled: false, 0f, target);
                    continue;
                }

                EnsureMaleControlProxy(i);
                MaleControlState state = GetMaleControlState(i);
                if (state == null || state.Proxy == null)
                    continue;

                if (solverReady)
                {
                    if (target != null)
                    {
                        target.SetPositionAndRotation(state.Proxy.position, state.Proxy.rotation);
                        ApplyMaleControlSolverBinding(i, enabled: true, w, target);
                    }
                    else if (sourceBone != null)
                    {
                        if (IsMaleControlPositionDriven(i))
                            sourceBone.position = Vector3.Lerp(sourceBone.position, state.Proxy.position, w);
                        sourceBone.rotation = Quaternion.Slerp(sourceBone.rotation, state.Proxy.rotation, w);
                    }
                }
                else if (sourceBone != null)
                {
                    if (IsMaleControlPositionDriven(i))
                        sourceBone.position = Vector3.Lerp(sourceBone.position, state.Proxy.position, w);
                    sourceBone.rotation = Quaternion.Slerp(sourceBone.rotation, state.Proxy.rotation, w);
                }
            }

            if (solverReady && _maleSession.MaleFbbik != null)
            {
                _maleSession.MaleFbbik.UpdateSolverExternal();
            }

            if (doDiag)
            {
                MaleControlDiagSnapshot diagAfter;
                if (TryCaptureMaleControlDiagSnapshot(diagIdx, out diagAfter))
                    LogMaleControlDiagnostics(diagBefore, diagAfter);
            }
        }

        private void ResetMaleControlPartToAnimationPose(int idx)
        {
            Transform bone = GetMaleControlBoneByIndex(idx);
            MaleControlState state = GetMaleControlState(idx);
            if (bone == null || state == null || state.Proxy == null)
                return;
            state.Proxy.SetPositionAndRotation(bone.position, bone.rotation);
        }

        private void DestroyMaleControlState(int idx)
        {
            MaleControlState state = GetMaleControlState(idx);
            if (state == null)
                return;

            bool wasDragging = state.GizmoDragging;
            if (state.Gizmo != null && state.GizmoDragHandler != null)
                state.Gizmo.DragStateChanged -= state.GizmoDragHandler;
            if (state.Gizmo != null && state.GizmoModeHandler != null)
                state.Gizmo.ModeChanged -= state.GizmoModeHandler;

            state.GizmoDragHandler = null;
            state.GizmoModeHandler = null;
            state.Gizmo = null;
            state.GizmoDragging = false;

            if (state.ProxyGo != null)
                Destroy(state.ProxyGo);
            state.ProxyGo = null;
            state.Proxy = null;

            if (wasDragging)
                RecomputeGizmoDraggingState();
        }

        private void DestroyAllMaleControlStates()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
                DestroyMaleControlState(i);
        }

        private ChaControl ResolveMainMale(HSceneProc proc)
        {
            if (proc == null)
                return null;

            if (_gameReflection.HSceneMaleList != null)
            {
                System.Collections.IList listObj = _gameReflection.HSceneMaleList.GetValue(proc) as System.Collections.IList;
                if (listObj != null)
                {
                    for (int i = 0; i < listObj.Count; i++)
                    {
                        ChaControl cha = listObj[i] as ChaControl;
                        if (cha != null)
                            return cha;
                    }
                }
            }

            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].sex == 0)
                    return all[i];
            }

            return null;
        }

        internal void ClearMaleRefs()
        {
            if (_maleSession.MaleFbbikInjectedByPlugin && _maleSession.MaleFbbik != null)
                Destroy(_maleSession.MaleFbbik);
            if (_maleSession.MaleSyntheticIkTargetRootGo != null)
                Destroy(_maleSession.MaleSyntheticIkTargetRootGo);

            _maleResolvedRefsSnapshot    = null;
            DestroyMaleHeadTargetGizmo();
            DestroyAllMaleControlStates();
            _maleSession.ResetReferences();
            _maleHmdDiagHasPrev = false;
            _nextMaleHmdDiagLogTime = 0f;
            _maleControlDiagHasPrev = false;
            _nextMaleControlDiagLogTime = 0f;
            _maleControlDiagPrevIndex = -1;
            _maleControlDiagPrevFrame = -1;
            _maleControlDiagPrevBonePos = Vector3.zero;
            _maleControlDiagPrevProxyPos = Vector3.zero;
            _maleLegacyControlSuppressedByHmd = false;
            DestroyMaleIkDebugMarkers();
        }

        private void LogMaleHmdDiagnostics(
            Vector3 hmdPos,
            Vector3 targetHeadPos,
            Vector3 spineBefore,
            Vector3 headBefore,
            float posScale,
            float spinePosWeight,
            bool useLocalDelta,
            Vector3 worldDeltaRaw,
            Vector3 localDeltaRaw,
            Vector3 localDeltaMapped,
            Vector3 localDeltaSmoothed,
            Vector3 worldDeltaApplied,
            bool sourceFromGizmo)
        {
            if (_settings == null || !_settings.MaleHmdDiagnosticLog)
            {
                _maleHmdDiagHasPrev = false;
                return;
            }

            float interval = Mathf.Clamp(_settings.MaleHmdDiagnosticLogInterval, 0.05f, 2f);
            if (Time.unscaledTime < _nextMaleHmdDiagLogTime)
                return;
            _nextMaleHmdDiagLogTime = Time.unscaledTime + interval;

            if (_maleSession.MaleHeadBone == null || _maleSession.MaleSpineBone == null)
            {
                LogMaleHmdDiag("[MaleHMD-DIAG] refs missing head=" + (_maleSession.MaleHeadBone != null) + " spine03=" + (_maleSession.MaleSpineBone != null));
                return;
            }

            Vector3 spineAfter = _maleSession.MaleSpineBone.position;
            Vector3 headAfter = _maleSession.MaleHeadBone.position;
            Vector3 hmdMove = Vector3.zero;
            Vector3 headMove = Vector3.zero;
            float moveDot = 0f;
            bool inverseMove = false;
            if (_maleHmdDiagHasPrev)
            {
                hmdMove = hmdPos - _maleHmdDiagPrevHmdPos;
                headMove = headAfter - _maleHmdDiagPrevHeadPos;
                if (hmdMove.sqrMagnitude > 0.0000001f && headMove.sqrMagnitude > 0.0000001f)
                {
                    moveDot = Vector3.Dot(hmdMove.normalized, headMove.normalized);
                    inverseMove = moveDot < -0.2f;
                }
            }

            float distBefore = Vector3.Distance(headBefore, targetHeadPos);
            float distAfter = Vector3.Distance(headAfter, targetHeadPos);
            Vector3 spineDelta = spineAfter - spineBefore;
            Vector3 headDelta = headAfter - headBefore;

            string msg = "[MaleHMD-DIAG] "
                + "headIk=" + _settings.MaleHeadIkEnabled
                + " source=" + (sourceFromGizmo ? "gizmo" : "hmd")
                + " localDelta=" + useLocalDelta
                + " posScale=" + posScale.ToString("F3")
                + " localResp=" + _settings.MaleHmdLocalDeltaSmoothing.ToString("F3")
                + " spineW=" + spinePosWeight.ToString("F3")
                + " sourcePos=" + Vec3(hmdPos)
                + " targetHead=" + Vec3(targetHeadPos)
                + " headBefore=" + Vec3(headBefore)
                + " headAfter=" + Vec3(headAfter)
                + " headDelta=" + Vec3(headDelta)
                + " spineDelta=" + Vec3(spineDelta)
                + " distBefore=" + distBefore.ToString("F4")
                + " distAfter=" + distAfter.ToString("F4")
                + " hmdMove=" + Vec3(hmdMove)
                + " headMove=" + Vec3(headMove)
                + " dot=" + moveDot.ToString("F3")
                + " inverse=" + inverseMove
                + " worldDeltaRaw=" + Vec3(worldDeltaRaw)
                + " localDeltaRaw=" + Vec3(localDeltaRaw)
                + " localDeltaMapped=" + Vec3(localDeltaMapped)
                + " localDeltaSmoothed=" + Vec3(localDeltaSmoothed)
                + " worldDeltaApplied=" + Vec3(worldDeltaApplied)
                + " headBone=" + (_maleSession.MaleHeadBoneName ?? "-")
                + " swapXZ=" + _settings.MaleHmdSwapHorizontalAxes
                + " invX=" + _settings.MaleHmdInvertHorizontalX
                + " invZ=" + _settings.MaleHmdInvertHorizontalZ
                + " ikEn=[" + BoolFlags(_settings.MaleIkEnabled) + "]"
                + " ikW=[" + WeightFlags(_settings.MaleIkWeights) + "]";
            LogMaleHmdDiag(msg);

            _maleHmdDiagPrevHmdPos = hmdPos;
            _maleHmdDiagPrevHeadPos = headAfter;
            _maleHmdDiagHasPrev = true;
        }

        private void LogMaleHmdSkipDiagnostics(string reason)
        {
            if (_settings == null || !_settings.MaleHmdDiagnosticLog)
                return;

            float interval = Mathf.Clamp(_settings.MaleHmdDiagnosticLogInterval, 0.05f, 2f);
            if (Time.unscaledTime < _nextMaleHmdDiagLogTime)
                return;

            _nextMaleHmdDiagLogTime = Time.unscaledTime + interval;
            _maleHmdDiagHasPrev = false;
            LogMaleHmdDiag("[MaleHMD-DIAG] skip reason=" + (reason ?? "unknown")
                + " male=" + (_maleSession.TargetMaleCha != null)
                + " spine03=" + (_maleSession.MaleSpineBone != null)
                + " head=" + (_maleSession.MaleHeadBone != null)
                + " headTarget=" + (_maleSession.MaleHeadTarget != null)
                + " vrActive=" + VR.Active
                + " gizmoSrc=" + (_settings != null && _settings.MaleHeadIkGizmoEnabled));
        }

        private static string Vec3(Vector3 v)
        {
            return v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3");
        }

        private static string BoolFlags(bool[] flags)
        {
            if (flags == null)
                return "-";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(flags.Length * 2);
            for (int i = 0; i < flags.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(flags[i] ? "1" : "0");
            }

            return sb.ToString();
        }

        private static string WeightFlags(float[] values)
        {
            if (values == null)
                return "-";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(values.Length * 6);
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(values[i].ToString("F2"));
            }

            return sb.ToString();
        }

        private void UpdateMaleHandFollow()
        {
            if (_settings == null)
                return;

            if (_settings.MaleLeftHandFollowEnabled)
            {
                if (_maleSession.MaleLeftHandBone != null && _maleSession.MaleLeftHandFollowBone != null)
                {
                    _maleSession.MaleLeftHandBone.position = _maleSession.MaleLeftHandFollowBone.position
                        + (_maleSession.MaleLeftHandFollowBone.rotation * _maleSession.MaleLeftHandFollowPosOffset);
                    _maleSession.MaleLeftHandBone.rotation = _maleSession.MaleLeftHandFollowBone.rotation * _maleSession.MaleLeftHandFollowRotOffset;
                }
            }

            if (_settings.MaleRightHandFollowEnabled)
            {
                if (_maleSession.MaleRightHandBone != null && _maleSession.MaleRightHandFollowBone != null)
                {
                    _maleSession.MaleRightHandBone.position = _maleSession.MaleRightHandFollowBone.position
                        + (_maleSession.MaleRightHandFollowBone.rotation * _maleSession.MaleRightHandFollowPosOffset);
                    _maleSession.MaleRightHandBone.rotation = _maleSession.MaleRightHandFollowBone.rotation * _maleSession.MaleRightHandFollowRotOffset;
                }
            }
        }

        private bool TrySetMaleHandFollow(bool left)
        {
            Transform maleHand = left ? _maleSession.MaleLeftHandBone : _maleSession.MaleRightHandBone;
            if (maleHand == null || _femaleSession.BoneCache == null || _femaleSession.BoneCache.Length == 0)
                return false;

            float snapDistance = Mathf.Max(0.02f, _settings != null ? _settings.MaleHandFollowSnapDistance : 0.2f);
            Transform nearest = null;
            float best = snapDistance;
            for (int i = 0; i < _femaleSession.BoneCache.Length; i++)
            {
                Transform bone = _femaleSession.BoneCache[i];
                if (bone == null)
                    continue;
                string name = bone.name ?? string.Empty;
                if (!name.StartsWith("cf_j_", System.StringComparison.Ordinal))
                    continue;

                float dist = Vector3.Distance(bone.position, maleHand.position);
                if (dist < best)
                {
                    best = dist;
                    nearest = bone;
                }
            }

            if (nearest == null)
                return false;

            if (left)
            {
                _maleSession.MaleLeftHandFollowBone = nearest;
                _maleSession.MaleLeftHandFollowPosOffset = Quaternion.Inverse(nearest.rotation) * (maleHand.position - nearest.position);
                _maleSession.MaleLeftHandFollowRotOffset = Quaternion.Inverse(nearest.rotation) * maleHand.rotation;
                _settings.MaleLeftHandFollowEnabled = true;
            }
            else
            {
                _maleSession.MaleRightHandFollowBone = nearest;
                _maleSession.MaleRightHandFollowPosOffset = Quaternion.Inverse(nearest.rotation) * (maleHand.position - nearest.position);
                _maleSession.MaleRightHandFollowRotOffset = Quaternion.Inverse(nearest.rotation) * maleHand.rotation;
                _settings.MaleRightHandFollowEnabled = true;
            }

            return true;
        }

        private void ClearMaleHandFollow(bool left)
        {
            if (left)
            {
                _settings.MaleLeftHandFollowEnabled = false;
                _maleSession.MaleLeftHandFollowBone = null;
                _maleSession.MaleLeftHandFollowPosOffset = Vector3.zero;
                _maleSession.MaleLeftHandFollowRotOffset = Quaternion.identity;
            }
            else
            {
                _settings.MaleRightHandFollowEnabled = false;
                _maleSession.MaleRightHandFollowBone = null;
                _maleSession.MaleRightHandFollowPosOffset = Vector3.zero;
                _maleSession.MaleRightHandFollowRotOffset = Quaternion.identity;
            }
        }

        private void ApplyMaleHeadIk(Vector3 hmdPos, Quaternion hmdRot)
        {
            Transform head = _maleSession.MaleHeadBone;
            if (head == null)
                return;

            Vector3 targetHeadPos = hmdPos + _maleSession.MaleHmdBaseHeadPosOffset;
            float posW = Mathf.Clamp01(_settings.MaleHeadIkPositionWeight);
            if (posW > 0f)
            {
                Transform neckPivot = _maleSession.MaleNeckBone != null ? _maleSession.MaleNeckBone : head;
                float targetDistance = Vector3.Distance(neckPivot.position, targetHeadPos);
                float nearDistance = Mathf.Max(0.05f, _settings.MaleHeadIkNearDistance);
                float farDistance = Mathf.Max(nearDistance + 0.01f, _settings.MaleHeadIkFarDistance);
                float distanceLerp = Mathf.InverseLerp(nearDistance, farDistance, targetDistance);

                float neckCurveBase = Mathf.Lerp(_settings.MaleHeadIkNearNeckWeight, _settings.MaleHeadIkFarNeckWeight, distanceLerp);
                float neckBase = neckCurveBase * _settings.MaleHeadIkNeckWeight;

                int solveIterations = Mathf.Clamp(_settings.MaleHeadIkSolveIterations, 1, 8);
                for (int iter = 0; iter < solveIterations; iter++)
                {
                    ApplyMaleIkBoneTowardTarget(MALE_IK_NECK, _maleSession.MaleNeckBone, head, targetHeadPos, posW * neckBase);
                }
            }

            Quaternion targetHeadRot = hmdRot * _maleSession.MaleHmdBaseHeadRotOffset;

            float rotW = Mathf.Clamp01(_settings.MaleHmdHeadRotationWeight * GetMaleIkWeight(MALE_IK_HEAD));
            if (rotW > 0f)
            {
                head.rotation = Quaternion.Slerp(head.rotation, targetHeadRot, rotW);
            }

            ApplyMaleShoulderFollowFromNeckDriver(targetHeadPos, targetHeadRot);
            ApplyMaleSpine1MidpointConstraint();
        }

        private void SuppressLegacyMaleControlLayerForHmdMode()
        {
            if (_maleLegacyControlSuppressedByHmd)
                return;
            if (!EnsureMaleFullBodyIkReady() || _maleSession.MaleFbbik == null)
                return;

            EnsureMaleSyntheticTargetsReady();
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                Transform target = GetMaleControlTargetByIndex(i);
                ApplyMaleControlSolverBinding(i, enabled: false, weight: 0f, target: target);
            }
            _maleSession.MaleFbbik.UpdateSolverExternal();
            _maleLegacyControlSuppressedByHmd = true;
        }

        private void ApplyMaleSpine1MidpointConstraint()
        {
            if (_settings == null || !_settings.MaleSpine1MidpointEnabled)
                return;

            Transform spine1 = _maleSession.MaleSpine1Bone;
            Transform spine2 = _maleSession.MaleSpine2Bone;
            Transform lower = _maleSession.MaleWaistBone != null ? _maleSession.MaleWaistBone : _maleSession.MaleHipsBone;
            if (spine1 == null || spine2 == null || lower == null)
                return;
            if (ReferenceEquals(spine1, spine2) || ReferenceEquals(spine1, lower))
                return;

            float t = Mathf.Clamp01(_settings.MaleSpine1MidpointT);
            float posW = Mathf.Clamp01(_settings.MaleSpine1MidpointWeight);
            if (posW <= 0f)
                return;

            Vector3 targetPos = Vector3.Lerp(lower.position, spine2.position, t);
            spine1.position = Vector3.Lerp(spine1.position, targetPos, posW);

            float rotW = Mathf.Clamp01(_settings.MaleSpine1MidpointRotationWeight);
            if (rotW > 0f)
            {
                Quaternion targetRot = Quaternion.Slerp(lower.rotation, spine2.rotation, t);
                spine1.rotation = Quaternion.Slerp(spine1.rotation, targetRot, rotW * posW);
            }
        }

        private void ApplyMaleShoulderFollowFromNeckDriver(Vector3 targetHeadPos, Quaternion targetHeadRot)
        {
            if (_settings == null || !_settings.MaleNeckShoulderFollowEnabled)
                return;
            if (!_maleSession.HasMaleNeckFromHeadBaseline)
                return;

            if (!_maleSession.HasMaleNeckShoulderPrevPose)
            {
                Vector3 initDriverPos = targetHeadPos + (targetHeadRot * _maleSession.MaleNeckFromHeadPosLocal);
                Quaternion initDriverRot = targetHeadRot * _maleSession.MaleNeckFromHeadRotLocal;
                _maleSession.MaleNeckShoulderPrevPos = initDriverPos;
                _maleSession.MaleNeckShoulderPrevRot = initDriverRot;
                _maleSession.HasMaleNeckShoulderPrevPose = true;
                return;
            }

            Vector3 driverPos = targetHeadPos + (targetHeadRot * _maleSession.MaleNeckFromHeadPosLocal);
            Quaternion driverRot = targetHeadRot * _maleSession.MaleNeckFromHeadRotLocal;
            Vector3 prevNeckPos = _maleSession.MaleNeckShoulderPrevPos;
            Quaternion prevNeckRot = _maleSession.MaleNeckShoulderPrevRot;
            Vector3 neckDeltaPos = driverPos - prevNeckPos;
            Quaternion neckDeltaRot = driverRot * Quaternion.Inverse(prevNeckRot);

            _maleSession.MaleNeckShoulderPrevPos = driverPos;
            _maleSession.MaleNeckShoulderPrevRot = driverRot;

            if (neckDeltaPos.sqrMagnitude <= 0.00000001f && Quaternion.Angle(Quaternion.identity, neckDeltaRot) <= 0.001f)
                return;

            ApplyMaleShoulderFollowByIndex(
                BIK_LS,
                prevNeckPos,
                neckDeltaPos,
                neckDeltaRot);
            ApplyMaleShoulderFollowByIndex(
                BIK_RS,
                prevNeckPos,
                neckDeltaPos,
                neckDeltaRot);
        }

        private bool ApplyMaleShoulderFollowByIndex(int idx, Vector3 prevNeckPos, Vector3 neckDeltaPos, Quaternion neckDeltaRot)
        {
            if (!GetMaleControlEnabled(idx))
                return false;

            EnsureMaleControlProxy(idx);
            MaleControlState state = GetMaleControlState(idx);
            if (state == null || state.Proxy == null || state.GizmoDragging)
                return false;

            float controlW = GetMaleControlWeight(idx);
            if (controlW <= 0f)
                return false;

            float posW = Mathf.Clamp01(_settings.MaleNeckShoulderFollowPositionWeight) * controlW;
            if (posW <= 0f)
                return false;

            Vector3 targetPos = neckDeltaRot * (state.Proxy.position - prevNeckPos) + prevNeckPos + neckDeltaPos;
            Vector3 nextPos = state.Proxy.position;
            Quaternion nextRot = state.Proxy.rotation;
            bool changed = false;

            Vector3 blendedPos = Vector3.Lerp(nextPos, targetPos, posW);
            if ((blendedPos - nextPos).sqrMagnitude > 0.00000001f)
            {
                nextPos = blendedPos;
                changed = true;
            }

            if (!changed)
                return false;

            state.Proxy.SetPositionAndRotation(nextPos, nextRot);

            Transform target = GetMaleControlTargetByIndex(idx);
            if (target != null && !ReferenceEquals(target, state.Proxy))
                target.SetPositionAndRotation(nextPos, nextRot);

            if (_maleSession.MaleFbbik != null)
                ApplyMaleControlSolverBinding(idx, enabled: true, weight: controlW, target: target ?? state.Proxy);

            return true;
        }

        private void TryApplyMaleBodyCenterPullFromHead(Vector3 targetHeadPos, Quaternion targetHeadRot, float headPosWeight, float roundness)
        {
            if (_settings == null || !_settings.MaleHeadIkBodyPullEnabled)
                return;
            if (!GetMaleControlEnabled(BIK_BODY))
                return;

            MaleControlState state = GetMaleControlState(BIK_BODY);
            if (state == null || state.Proxy == null || state.GizmoDragging)
                return;

            Transform head = _maleSession.MaleHeadBone;
            if (head == null)
                return;

            float bodyWeight = GetMaleControlWeight(BIK_BODY);
            if (bodyWeight <= 0f)
                return;

            Vector3 nextPos = state.Proxy.position;
            Quaternion nextRot = state.Proxy.rotation;
            bool changed = false;

            float posPull = Mathf.Clamp01(_settings.MaleHeadIkBodyPullPositionWeight) * Mathf.Clamp01(headPosWeight) * bodyWeight;
            if (posPull > 0f)
            {
                Vector3 headError = targetHeadPos - head.position;
                float nearBoost = Mathf.Lerp(1f, 1.35f, Mathf.Clamp01(roundness));
                Vector3 step = headError * (posPull * nearBoost);
                float maxStep = Mathf.Max(0f, _settings.MaleHeadIkBodyPullMaxStep);
                if (maxStep > 0f)
                    step = Vector3.ClampMagnitude(step, maxStep);

                if (step.sqrMagnitude > 0.00000001f)
                {
                    nextPos += step;
                    changed = true;
                }
            }

            float rotPull = Mathf.Clamp01(_settings.MaleHeadIkBodyPullRotationWeight) * bodyWeight;
            if (rotPull > 0f)
            {
                Quaternion blended = Quaternion.Slerp(nextRot, targetHeadRot, rotPull);
                if (Quaternion.Angle(nextRot, blended) > 0.001f)
                {
                    nextRot = blended;
                    changed = true;
                }
            }

            if (!changed)
                return;

            state.Proxy.SetPositionAndRotation(nextPos, nextRot);

            Transform target = GetMaleControlTargetByIndex(BIK_BODY);
            if (target != null && !ReferenceEquals(target, state.Proxy))
                target.SetPositionAndRotation(nextPos, nextRot);

            if (_maleSession.MaleFbbik != null)
            {
                ApplyMaleControlSolverBinding(BIK_BODY, enabled: true, weight: bodyWeight, target: target ?? state.Proxy);
                _maleSession.MaleFbbik.UpdateSolverExternal();
            }

            if (_settings.MaleHmdDiagnosticLog)
            {
                LogMaleHmdDiag(
                    "[MaleHMD-BODYPULL] headErr="
                    + (targetHeadPos - head.position).ToString("F4")
                    + " posW=" + posPull.ToString("F3")
                    + " rotW=" + rotPull.ToString("F3")
                    + " round=" + roundness.ToString("F3")
                    + " stepMax=" + _settings.MaleHeadIkBodyPullMaxStep.ToString("F3")
                    + " proxyPos=" + nextPos.ToString("F4"));
            }
        }

        private void ApplyMaleSpinePositionOffsetFromBodyDelta(float headPosWeight)
        {
            if (_settings == null || !_settings.MaleHeadIkSpineBodyOffsetEnabled)
                return;

            Vector3 bodyDelta = GetMaleBodyProxyLocalDeltaWorld();
            if (bodyDelta.sqrMagnitude <= 0.00000001f)
                return;

            bodyDelta *= Mathf.Clamp01(headPosWeight);
            float maxDist = Mathf.Max(0f, _settings.MaleHeadIkSpineBodyOffsetMax);
            if (maxDist > 0f)
                bodyDelta = Vector3.ClampMagnitude(bodyDelta, maxDist);

            ApplyMaleSpineBodyOffsetToBone(_maleSession.MaleSpine1Bone, MALE_IK_SPINE1, bodyDelta, _settings.MaleHeadIkSpineBodyOffsetSpine1Weight);
            ApplyMaleSpineBodyOffsetToBone(_maleSession.MaleSpine2Bone, MALE_IK_SPINE2, bodyDelta, _settings.MaleHeadIkSpineBodyOffsetSpine2Weight);
            ApplyMaleSpineBodyOffsetToBone(_maleSession.MaleSpineBone, MALE_IK_SPINE3, bodyDelta, _settings.MaleHeadIkSpineBodyOffsetSpine3Weight);

            if (_settings.MaleHmdDiagnosticLog)
            {
                LogMaleHmdDiag(
                    "[MaleHMD-SPINEPOS] bodyDelta="
                    + bodyDelta.ToString("F4")
                    + " w1=" + _settings.MaleHeadIkSpineBodyOffsetSpine1Weight.ToString("F3")
                    + " w2=" + _settings.MaleHeadIkSpineBodyOffsetSpine2Weight.ToString("F3")
                    + " w3=" + _settings.MaleHeadIkSpineBodyOffsetSpine3Weight.ToString("F3")
                    + " max=" + _settings.MaleHeadIkSpineBodyOffsetMax.ToString("F3"));
            }
        }

        private Vector3 GetMaleBodyProxyLocalDelta()
        {
            if (!_maleSession.HasMaleHmdBaseline)
                return Vector3.zero;
            if (!GetMaleControlEnabled(BIK_BODY))
                return Vector3.zero;

            MaleControlState state = GetMaleControlState(BIK_BODY);
            if (state == null || state.Proxy == null)
                return Vector3.zero;

            Transform maleRoot = _maleSession.TargetMaleCha != null ? _maleSession.TargetMaleCha.transform : null;
            if (maleRoot == null)
                return Vector3.zero;

            Vector3 currentLocal = maleRoot.InverseTransformPoint(state.Proxy.position);
            return currentLocal - _maleSession.MaleHmdBaseBodyPosLocal;
        }

        private Vector3 GetMaleBodyProxyLocalDeltaWorld()
        {
            Vector3 localDelta = GetMaleBodyProxyLocalDelta();
            if (localDelta.sqrMagnitude <= 0.00000001f)
                return Vector3.zero;

            Transform maleRoot = _maleSession.TargetMaleCha != null ? _maleSession.TargetMaleCha.transform : null;
            if (maleRoot == null)
                return Vector3.zero;

            return maleRoot.TransformVector(localDelta);
        }

        private void ApplyMaleSpineBodyOffsetToBone(Transform bone, int ikIdx, Vector3 bodyDelta, float baseWeight)
        {
            if (bone == null || bodyDelta.sqrMagnitude <= 0.00000001f)
                return;
            if (!GetMaleIkEnabled(ikIdx))
                return;

            float w = Mathf.Clamp01(baseWeight) * GetMaleIkWeight(ikIdx);
            if (w <= 0f)
                return;

            bone.position += bodyDelta * w;
        }

        private Vector3 GetMaleHeadBodyCompensationDelta()
        {
            if (_settings == null || !_settings.MaleHeadIkCompensateBodyOffset)
                return Vector3.zero;
            if (!_maleSession.HasMaleHmdBaseline)
                return Vector3.zero;
            if (!GetMaleControlEnabled(BIK_BODY))
                return Vector3.zero;

            MaleControlState state = GetMaleControlState(BIK_BODY);
            if (state == null || state.Proxy == null)
                return Vector3.zero;

            Vector3 rawDelta = GetMaleBodyProxyLocalDeltaWorld();
            float weight = Mathf.Clamp(_settings.MaleHeadIkCompensateBodyOffsetWeight, 0f, 2f);
            Vector3 delta = rawDelta * weight;

            float maxDist = Mathf.Max(0f, _settings.MaleHeadIkCompensateBodyOffsetMax);
            if (maxDist > 0f)
                delta = Vector3.ClampMagnitude(delta, maxDist);

            return delta;
        }

        private void ApplyMaleIkBoneTowardTarget(int ikIdx, Transform pivotBone, Transform endBone, Vector3 targetPos, float baseWeight)
        {
            if (!GetMaleIkEnabled(ikIdx))
                return;
            RotateBoneTowardTarget(pivotBone, endBone, targetPos, baseWeight * GetMaleIkWeight(ikIdx));
        }

        private bool GetMaleIkEnabled(int idx)
        {
            if (_settings == null || _settings.MaleIkEnabled == null || idx < 0 || idx >= _settings.MaleIkEnabled.Length)
                return false;
            return _settings.MaleIkEnabled[idx];
        }

        private float GetMaleIkWeight(int idx)
        {
            if (_settings == null || _settings.MaleIkWeights == null || idx < 0 || idx >= _settings.MaleIkWeights.Length)
                return 0f;
            return Mathf.Clamp01(_settings.MaleIkWeights[idx]);
        }

        private static void RotateBoneTowardTarget(Transform pivotBone, Transform endBone, Vector3 targetPos, float weight)
        {
            if (pivotBone == null || endBone == null || weight <= 0f)
                return;

            Vector3 fromDir = endBone.position - pivotBone.position;
            Vector3 toDir = targetPos - pivotBone.position;
            if (fromDir.sqrMagnitude < 0.000001f || toDir.sqrMagnitude < 0.000001f)
                return;

            Quaternion delta = Quaternion.FromToRotation(fromDir, toDir);
            Quaternion targetRot = delta * pivotBone.rotation;
            pivotBone.rotation = Quaternion.Slerp(pivotBone.rotation, targetRot, Mathf.Clamp01(weight));
        }

        private static Transform FindBoneInCache(Transform[] cache, string boneName)
        {
            if (cache == null)
                return null;

            string target = StripWorkSuffix(boneName);
            for (int i = 0; i < cache.Length; i++)
            {
                Transform t = cache[i];
                if (t == null)
                    continue;

                string currentName = t.name;
                if (string.Equals(currentName, boneName, System.StringComparison.Ordinal))
                    return t;

                // "(work)" 付き命名との互換マッチ（例: cf_j_hand_L(work)）。
                if (string.Equals(StripWorkSuffix(currentName), target, System.StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        private void LogMaleResolvedRefsIfChanged()
        {
            if (_maleSession.MaleBoneCache == null)
                return;

            string summary = BuildMaleResolvedRefsSummary();
            if (string.Equals(_maleResolvedRefsSnapshot, summary, System.StringComparison.Ordinal))
                return;

            _maleResolvedRefsSnapshot = summary;
            LogMaleHmdDiag("[MaleHMD-RESOLVE] " + summary);
        }

        private string BuildMaleResolvedRefsSummary()
        {
            var sb = new System.Text.StringBuilder(2048);
            sb.Append("male=");
            sb.Append(_maleSession.TargetMaleCha != null ? (_maleSession.TargetMaleCha.name ?? "(unnamed)") : "(null)");
            sb.Append(" headSel=");
            sb.Append(GetMaleHeadBoneSelectionLabel());
            sb.Append(" maleFbbik=");
            if (_maleSession.MaleFbbik == null)
                sb.Append("null");
            else
                sb.Append(_maleSession.MaleFbbikInjectedByPlugin ? "plugin" : "existing");
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("waist", _maleSession.MaleWaistBone, _maleSession.MaleBoneCache,
                "cf_j_waist01", "cf_J_Waist01", "cm_j_waist01", "cm_J_Waist01"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("lShoulder", _maleSession.MaleLeftShoulderBone, _maleSession.MaleBoneCache,
                "cm_j_shoulder_L", "cm_J_Shoulder_L", "cf_j_shoulder_L", "cf_J_Shoulder_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("rShoulder", _maleSession.MaleRightShoulderBone, _maleSession.MaleBoneCache,
                "cm_j_shoulder_R", "cm_J_Shoulder_R", "cf_j_shoulder_R", "cf_J_Shoulder_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("lHand", _maleSession.MaleLeftHandBone, _maleSession.MaleBoneCache,
                "cf_j_hand_L", "cf_J_Hand_L", "cm_j_hand_L", "cm_J_Hand_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("rHand", _maleSession.MaleRightHandBone, _maleSession.MaleBoneCache,
                "cf_j_hand_R", "cf_J_Hand_R", "cm_j_hand_R", "cm_J_Hand_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("head", _maleSession.MaleHeadBone, _maleSession.MaleBoneCache,
                "cf_j_head", "cf_J_Head", "cm_j_head", "cm_J_Head"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("neck", _maleSession.MaleNeckBone, _maleSession.MaleBoneCache,
                "cf_j_neck", "cf_J_Neck", "cm_j_neck", "cm_J_Neck"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("spine03", _maleSession.MaleSpineBone, _maleSession.MaleBoneCache,
                "cf_j_spine03", "cf_J_Spine03", "cm_j_spine03", "cm_J_Spine03"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("spine02", _maleSession.MaleSpine2Bone, _maleSession.MaleBoneCache,
                "cf_j_spine02", "cf_J_Spine02", "cm_j_spine02", "cm_J_Spine02"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("spine01", _maleSession.MaleSpine1Bone, _maleSession.MaleBoneCache,
                "cf_j_spine01", "cf_J_Spine01", "cm_j_spine01", "cm_J_Spine01"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("hips", _maleSession.MaleHipsBone, _maleSession.MaleBoneCache,
                "cm_j_waist01", "cm_J_Waist01", "cf_j_waist01", "cf_J_Waist01"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("ikHips", _maleSession.MaleIkHipsTarget, _maleSession.MaleBoneCache, "cf_ik_hips"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvHandL", _maleSession.MalePvHandL, _maleSession.MaleBoneCache, "cf_pv_hand_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvHandR", _maleSession.MalePvHandR, _maleSession.MaleBoneCache, "cf_pv_hand_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvElboL", _maleSession.MalePvElboL, _maleSession.MaleBoneCache, "cf_pv_elbo_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvElboR", _maleSession.MalePvElboR, _maleSession.MaleBoneCache, "cf_pv_elbo_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvLegL", _maleSession.MalePvLegL, _maleSession.MaleBoneCache, "cf_pv_leg_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvLegR", _maleSession.MalePvLegR, _maleSession.MaleBoneCache, "cf_pv_leg_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvKneeL", _maleSession.MalePvKneeL, _maleSession.MaleBoneCache, "cf_pv_knee_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvKneeR", _maleSession.MalePvKneeR, _maleSession.MaleBoneCache, "cf_pv_knee_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvShoulderL", _maleSession.MalePvShoulderL, _maleSession.MaleBoneCache, "cf_pv_shoulder_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvShoulderR", _maleSession.MalePvShoulderR, _maleSession.MaleBoneCache, "cf_pv_shoulder_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvWaistL", _maleSession.MalePvWaistL, _maleSession.MaleBoneCache, "cf_pv_waist_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("pvWaistR", _maleSession.MalePvWaistR, _maleSession.MaleBoneCache, "cf_pv_waist_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tHandL", GetMaleSyntheticTargetAt(BIK_LH), _maleSession.MaleBoneCache, "cf_t_hand_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tHandR", GetMaleSyntheticTargetAt(BIK_RH), _maleSession.MaleBoneCache, "cf_t_hand_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tLegL", GetMaleSyntheticTargetAt(BIK_LF), _maleSession.MaleBoneCache, "cf_t_leg_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tLegR", GetMaleSyntheticTargetAt(BIK_RF), _maleSession.MaleBoneCache, "cf_t_leg_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tShoulderL", GetMaleSyntheticTargetAt(BIK_LS), _maleSession.MaleBoneCache, "cf_t_shoulder_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tShoulderR", GetMaleSyntheticTargetAt(BIK_RS), _maleSession.MaleBoneCache, "cf_t_shoulder_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tWaistL", GetMaleSyntheticTargetAt(BIK_LT), _maleSession.MaleBoneCache, "cf_t_waist_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tWaistR", GetMaleSyntheticTargetAt(BIK_RT), _maleSession.MaleBoneCache, "cf_t_waist_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tElboL", GetMaleSyntheticTargetAt(BIK_LE), _maleSession.MaleBoneCache, "cf_t_elbo_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tElboR", GetMaleSyntheticTargetAt(BIK_RE), _maleSession.MaleBoneCache, "cf_t_elbo_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tKneeL", GetMaleSyntheticTargetAt(BIK_LK), _maleSession.MaleBoneCache, "cf_t_knee_L"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tKneeR", GetMaleSyntheticTargetAt(BIK_RK), _maleSession.MaleBoneCache, "cf_t_knee_R"));
            sb.Append(" ");
            sb.Append(BuildMaleResolvedEntry("tHips", GetMaleSyntheticTargetAt(BIK_BODY), _maleSession.MaleBoneCache, "cf_t_hips"));
            return sb.ToString();
        }

        private Transform GetMaleSyntheticTargetAt(int idx)
        {
            if (_maleSession.MaleSyntheticIkTargets == null)
                return null;
            if (idx < 0 || idx >= _maleSession.MaleSyntheticIkTargets.Length)
                return null;
            return _maleSession.MaleSyntheticIkTargets[idx];
        }

        private static string BuildMaleResolvedEntry(string label, Transform resolved, Transform[] cache, params string[] candidates)
        {
            string finalName = resolved != null ? resolved.name : "(null)";
            string hitNames = CollectMatchingBoneNames(cache, candidates);
            string query = JoinBoneCandidates(candidates);
            return label + "{final=" + finalName + ",hits=" + hitNames + ",query=" + query + "}";
        }

        private static string JoinBoneCandidates(string[] candidates)
        {
            if (candidates == null || candidates.Length == 0)
                return "-";

            var sb = new System.Text.StringBuilder(candidates.Length * 16);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (i > 0)
                    sb.Append("|");
                sb.Append(candidates[i] ?? string.Empty);
            }
            return sb.ToString();
        }

        private static string CollectMatchingBoneNames(Transform[] cache, string[] candidates)
        {
            if (cache == null || candidates == null || candidates.Length == 0)
                return "-";

            var normalizedTargets = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < candidates.Length; i++)
            {
                string c = candidates[i];
                if (string.IsNullOrEmpty(c))
                    continue;
                normalizedTargets.Add(StripWorkSuffix(c));
            }

            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var hits = new System.Collections.Generic.List<string>();
            for (int i = 0; i < cache.Length; i++)
            {
                Transform t = cache[i];
                if (t == null || string.IsNullOrEmpty(t.name))
                    continue;

                string normalizedCurrent = StripWorkSuffix(t.name);
                if (!normalizedTargets.Contains(normalizedCurrent))
                    continue;

                if (seen.Add(t.name))
                    hits.Add(t.name);
            }

            if (hits.Count == 0)
                return "-";

            return string.Join("|", hits.ToArray());
        }

        private static string StripWorkSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            string text = name.Trim();
            int paren = text.LastIndexOf('(');
            if (paren < 0 || !text.EndsWith(")", System.StringComparison.Ordinal))
                return text;

            string suffix = text.Substring(paren).Trim();
            if (!suffix.Equals("(work)", System.StringComparison.OrdinalIgnoreCase))
                return text;

            return text.Substring(0, paren).TrimEnd();
        }

        private static Transform FindFirstBoneInCache(Transform[] cache, params string[] boneNames)
        {
            if (cache == null || boneNames == null)
                return null;
            for (int i = 0; i < boneNames.Length; i++)
            {
                Transform t = FindBoneInCache(cache, boneNames[i]);
                if (t != null)
                    return t;
            }

            return null;
        }

        private Transform GetMaleIkBoneByIndex(int idx)
        {
            switch (idx)
            {
                case MALE_IK_WAIST: return _maleSession.MaleWaistBone;
                case MALE_IK_SPINE1: return _maleSession.MaleSpine1Bone;
                case MALE_IK_SPINE2: return _maleSession.MaleSpine2Bone;
                case MALE_IK_SPINE3: return _maleSession.MaleSpineBone;
                case MALE_IK_NECK: return _maleSession.MaleNeckBone;
                case MALE_IK_HEAD: return _maleSession.MaleHeadBone;
                default: return null;
            }
        }

        private void UpdateMaleIkDebugMarkers()
        {
            bool shouldShow = _settings != null && _settings.MaleIkDebugVisible;
            float markerSize = _settings != null ? Mathf.Clamp(_settings.BoneMarkerSize, 0.01f, 0.15f) : 0.04f;

            for (int i = 0; i < MALE_IK_BONE_TOTAL; i++)
            {
                Transform bone = GetMaleIkBoneByIndex(i);
                if (!shouldShow || bone == null)
                {
                    if (_maleIkDebugMarkers[i] != null)
                        _maleIkDebugMarkers[i].SetActive(false);
                    continue;
                }

                if (_maleIkDebugMarkers[i] == null)
                    _maleIkDebugMarkers[i] = CreateMaleIkDebugMarker(i);

                GameObject marker = _maleIkDebugMarkers[i];
                marker.SetActive(true);
                marker.transform.position = bone.position;
                marker.transform.localScale = Vector3.one * markerSize;

                Renderer mr = marker.GetComponent<Renderer>();
                if (mr != null && mr.sharedMaterial != null)
                {
                    bool enabled = GetMaleIkEnabled(i);
                    mr.sharedMaterial.color = enabled ? new Color(0.15f, 0.95f, 1f) : new Color(0.35f, 0.35f, 0.35f);
                }
            }
        }

        private GameObject CreateMaleIkDebugMarker(int idx)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "__MaleIkMarker_" + idx + "_" + MaleIkLabels[idx];
            go.hideFlags = HideFlags.HideAndDontSave;
            Destroy(go.GetComponent<Collider>());
            Renderer mr = go.GetComponent<Renderer>();
            if (mr != null)
            {
                Material mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                mat.color = new Color(0.15f, 0.95f, 1f);
                mr.sharedMaterial = mat;
            }
            go.layer = VR.Active ? 0 : 31;
            return go;
        }

        private void DestroyMaleIkDebugMarkers()
        {
            for (int i = 0; i < _maleIkDebugMarkers.Length; i++)
            {
                GameObject marker = _maleIkDebugMarkers[i];
                if (marker == null)
                    continue;
                Renderer mr = marker.GetComponent<Renderer>();
                if (mr != null && mr.sharedMaterial != null)
                    Destroy(mr.sharedMaterial);
                Destroy(marker);
                _maleIkDebugMarkers[i] = null;
            }
        }
    }
}

