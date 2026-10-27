using System;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlShoulderIkStabilizer;

internal sealed class ShoulderRotator : MonoBehaviour
{
	private FullBodyBipedIK _ik;

	private Transform _chaRoot;

	private PluginSettings _settings;

	private bool _hooked;

	private float _nextLeftDiagnosticLogTime;

	private float _nextRightDiagnosticLogTime;

	private bool _leftArmIkRunning;

	private bool _rightArmIkRunning;

	internal void Configure(FullBodyBipedIK ik, Transform chaRoot, PluginSettings settings, bool leftArmIkRunning, bool rightArmIkRunning)
	{
		if ((object)_ik != ik)
		{
			UnhookSolver();
			_ik = ik;
			HookSolver();
		}
		_chaRoot = chaRoot;
		_settings = settings;
		_leftArmIkRunning = leftArmIkRunning;
		_rightArmIkRunning = rightArmIkRunning;
	}

	private void OnEnable()
	{
		HookSolver();
	}

	private void OnDisable()
	{
		UnhookSolver();
	}

	private void OnDestroy()
	{
		UnhookSolver();
	}

	private void HookSolver()
	{
		if (!_hooked && !(_ik == null) && _ik.solver != null)
		{
			IKSolverFullBodyBiped solver = _ik.solver;
			solver.OnPostUpdate = (IKSolver.UpdateDelegate)Delegate.Combine(solver.OnPostUpdate, new IKSolver.UpdateDelegate(RotateShoulders));
			_hooked = true;
		}
	}

	private void UnhookSolver()
	{
		if (!_hooked || _ik == null || _ik.solver == null)
		{
			_hooked = false;
			return;
		}
		IKSolverFullBodyBiped solver = _ik.solver;
		solver.OnPostUpdate = (IKSolver.UpdateDelegate)Delegate.Remove(solver.OnPostUpdate, new IKSolver.UpdateDelegate(RotateShoulders));
		_hooked = false;
	}

	private void RotateShoulders()
	{
		if (_ik == null || _settings == null || !_settings.ShoulderRotationEnabled)
		{
			return;
		}

		IKSolverFullBodyBiped solver = _ik.solver;
		if (solver == null || solver.IKPositionWeight <= 0f)
		{
			return;
		}

		float leftWeight = _settings.ShoulderWeight;
		float leftOffset = _settings.ShoulderOffset;
		float rightWeight = (_settings.IndependentShoulders ? _settings.ShoulderRightWeight : _settings.ShoulderWeight);
		float rightOffset = (_settings.IndependentShoulders ? _settings.ShoulderRightOffset : _settings.ShoulderOffset);
		bool leftApplied = RotateShoulder(FullBodyBipedChain.LeftArm, leftWeight, leftOffset, _settings.ReverseShoulderL);
		bool rightApplied = RotateShoulder(FullBodyBipedChain.RightArm, rightWeight, rightOffset, _settings.ReverseShoulderR);

		// OnPostUpdateで肩補正を適用する。追加の solver.Update() は行わない。
		if (!leftApplied && !rightApplied)
		{
			return;
		}
	}

	private bool RotateShoulder(FullBodyBipedChain chain, float weight, float offset, bool reverseWhenLowered)
	{
		if (_ik == null || _ik.solver == null)
		{
			return false;
		}
		IKSolverFullBodyBiped solver = _ik.solver;
		IKMappingLimb limbMapping = solver.GetLimbMapping(chain);
		IKEffector endEffector = solver.GetEndEffector(chain);
		FBIKChain chainData = solver.GetChain(chain);
		IKMapping.BoneMap parentBoneMap = GetParentBoneMap(chain);
		if (limbMapping == null || endEffector == null || chainData == null || parentBoneMap == null || limbMapping.bone1 == null || limbMapping.parentBone == null || parentBoneMap.transform == null || chainData.nodes == null || chainData.nodes.Length < 2)
		{
			return false;
		}

		bool shouldLogDiagnostics = ShouldLogShoulderDiagnostic(chain);
		float configuredWeight = weight;
		float configuredOffset = offset;
		float yDelta = GetLocalArmYDelta(endEffector, limbMapping);
		Transform bendGoalTransform = ResolveBendGoalTransform(chain);
		string bendGoalName = bendGoalTransform != null ? bendGoalTransform.name : "null";
		bool lowered = yDelta < 0f;
		float elbowAngleBefore = -1f;
		float elbowAngleAfter = -1f;
		float forearmTargetDirErrorBefore = -1f;
		float forearmTargetDirErrorAfter = -1f;
		float forearmBendGoalErrorBefore = -1f;
		float forearmBendGoalErrorAfter = -1f;
		bool bendGoalPlaneUsed = false;
		Vector3 beforeLocalEuler = Vector3.zero;
		Vector3 beforeWorldEuler = Vector3.zero;
		if (shouldLogDiagnostics)
		{
			elbowAngleBefore = GetElbowAngleDeg(limbMapping);
			forearmTargetDirErrorBefore = GetForearmTargetDirErrorDeg(limbMapping, endEffector);
			forearmBendGoalErrorBefore = GetForearmBendGoalErrorDeg(limbMapping, bendGoalTransform);
			beforeLocalEuler = NormalizeEulerSigned(limbMapping.parentBone.localEulerAngles);
			beforeWorldEuler = NormalizeEulerSigned(limbMapping.parentBone.eulerAngles);
		}

		float solverEffectorToBoneDistance = GetEffectorToBoneDistance(endEffector, limbMapping);
		float targetToBoneDistance = GetTargetToBoneDistance(endEffector, limbMapping);
		bool armIkEnabled = IsArmIkRunning(chain);
		if (!armIkEnabled)
		{
			if (shouldLogDiagnostics)
			{
				EmitShoulderIkDisabledDiagnostic(
					chain,
					lowered,
					yDelta,
					solverEffectorToBoneDistance,
					targetToBoneDistance,
					endEffector.positionWeight,
					solver.IKPositionWeight,
					beforeLocalEuler,
					beforeWorldEuler);
			}

			return false;
		}

		if (lowered)
		{
			float loweredScale = Mathf.Clamp01(_settings.LoweredArmScale);
			weight *= loweredScale;
			offset *= loweredScale;
		}

		float adjustedWeight = weight;
		float adjustedOffset = offset;
		Vector3 toTarget = endEffector.position - parentBoneMap.transform.position;
		Quaternion fromTo = Quaternion.FromToRotation(parentBoneMap.swingDirection, toTarget);
		Vector3 limbVector = endEffector.position - limbMapping.bone1.position;
		float chainLength = chainData.nodes[0].length + chainData.nodes[1].length;
		if (chainLength <= 0.0001f)
		{
			return false;
		}

		float stretchRatio = limbVector.magnitude / chainLength;
		float rawBlend = (stretchRatio - 1f + offset) * weight;
		float blend01 = Mathf.Clamp(rawBlend, 0f, 1f);
		float solverBlend = blend01 * endEffector.positionWeight * solver.IKPositionWeight;

		Quaternion delta = Quaternion.Lerp(Quaternion.identity, fromTo, solverBlend);
		float fromToAngle = Quaternion.Angle(Quaternion.identity, fromTo);
		float deltaAnglePreReverse = Quaternion.Angle(Quaternion.identity, delta);
		bool reversed = false;
		if (lowered && reverseWhenLowered)
		{
			delta = Quaternion.Inverse(delta);
			reversed = true;
		}

		float deltaAnglePostReverse = Quaternion.Angle(Quaternion.identity, delta);
		float appliedDeltaAngle = Quaternion.Angle(Quaternion.identity, delta);
		limbMapping.parentBone.rotation = delta * limbMapping.parentBone.rotation;
		float handTargetDistanceBefore = 0f;
		float handTargetDistanceAfter = 0f;
		bool armChainAligned = false;
		if (solverBlend > 0f)
		{
			armChainAligned = TryAlignArmChainToTarget(limbMapping, endEffector, bendGoalTransform, out handTargetDistanceBefore, out handTargetDistanceAfter, out bendGoalPlaneUsed);
		}
		else
		{
			handTargetDistanceBefore = GetHandTargetDistance(endEffector, limbMapping);
			handTargetDistanceAfter = handTargetDistanceBefore;
		}

		if (shouldLogDiagnostics)
		{
			Vector3 afterLocalEuler = NormalizeEulerSigned(limbMapping.parentBone.localEulerAngles);
			Vector3 afterWorldEuler = NormalizeEulerSigned(limbMapping.parentBone.eulerAngles);
			elbowAngleAfter = GetElbowAngleDeg(limbMapping);
			forearmTargetDirErrorAfter = GetForearmTargetDirErrorDeg(limbMapping, endEffector);
			forearmBendGoalErrorAfter = GetForearmBendGoalErrorDeg(limbMapping, bendGoalTransform);

			EmitShoulderDiagnostic(
				chain,
				lowered,
				reversed,
				configuredWeight,
				configuredOffset,
				adjustedWeight,
				adjustedOffset,
				solverEffectorToBoneDistance,
				targetToBoneDistance,
				yDelta,
				stretchRatio,
				rawBlend,
				blend01,
				solverBlend,
				endEffector.positionWeight,
				solver.IKPositionWeight,
				armChainAligned,
				handTargetDistanceBefore,
				handTargetDistanceAfter,
				fromToAngle,
				deltaAnglePreReverse,
				deltaAnglePostReverse,
				appliedDeltaAngle,
				beforeLocalEuler,
				afterLocalEuler,
				beforeWorldEuler,
				afterWorldEuler,
				elbowAngleBefore,
				elbowAngleAfter,
				forearmTargetDirErrorBefore,
				forearmTargetDirErrorAfter,
				forearmBendGoalErrorBefore,
				forearmBendGoalErrorAfter,
				bendGoalPlaneUsed,
				bendGoalName);
		}

		return solverBlend > 0f;
	}

	private bool TryAlignArmChainToTarget(
		IKMappingLimb limbMapping,
		IKEffector endEffector,
		Transform bendGoalTransform,
		out float handTargetDistanceBefore,
		out float handTargetDistanceAfter,
		out bool bendGoalPlaneUsed)
	{
		bendGoalPlaneUsed = false;
		handTargetDistanceBefore = GetHandTargetDistance(endEffector, limbMapping);
		handTargetDistanceAfter = handTargetDistanceBefore;
		if (limbMapping == null || endEffector == null || endEffector.target == null || endEffector.positionWeight <= 0f)
		{
			return false;
		}

		Transform upperArm = limbMapping.bone1;
		Transform forearm = limbMapping.bone2;
		Transform hand = limbMapping.bone3;
		if (upperArm == null || forearm == null || hand == null)
		{
			return false;
		}

		Vector3 root = upperArm.position;
		Vector3 joint = forearm.position;
		Vector3 end = hand.position;
		Vector3 weightedTarget = Vector3.Lerp(end, endEffector.target.position, Mathf.Clamp01(endEffector.positionWeight));
		float upperLength = Vector3.Distance(root, joint);
		float foreLength = Vector3.Distance(joint, end);
		if (upperLength <= 0.0001f || foreLength <= 0.0001f)
		{
			return false;
		}

		Vector3 rootToTarget = weightedTarget - root;
		float targetDistance = rootToTarget.magnitude;
		if (targetDistance <= 0.0001f)
		{
			return false;
		}

		float minReach = Mathf.Abs(upperLength - foreLength) + 0.0001f;
		float maxReach = upperLength + foreLength - 0.0001f;
		float clampedDistance = Mathf.Clamp(targetDistance, minReach, maxReach);
		Vector3 targetDirection = rootToTarget / targetDistance;

		Vector3 bendDirection = Vector3.zero;
		if (bendGoalTransform != null)
		{
			bendDirection = Vector3.ProjectOnPlane(bendGoalTransform.position - root, targetDirection);
			if (bendDirection.sqrMagnitude >= 1E-08f)
			{
				bendGoalPlaneUsed = true;
			}
		}

		if (!bendGoalPlaneUsed)
		{
			bendDirection = Vector3.ProjectOnPlane(joint - root, targetDirection);
			if (bendDirection.sqrMagnitude < 1E-08f)
			{
				bendDirection = Vector3.ProjectOnPlane(upperArm.up, targetDirection);
			}

			if (bendDirection.sqrMagnitude < 1E-08f)
			{
				bendDirection = Vector3.ProjectOnPlane(upperArm.right, targetDirection);
			}

			if (bendDirection.sqrMagnitude < 1E-08f)
			{
				bendDirection = Vector3.Cross(targetDirection, Vector3.up);
			}

			if (bendDirection.sqrMagnitude < 1E-08f)
			{
				bendDirection = Vector3.Cross(targetDirection, Vector3.right);
			}
		}

		bendDirection.Normalize();

		float x = (upperLength * upperLength - foreLength * foreLength + clampedDistance * clampedDistance) / (2f * clampedDistance);
		float y2 = Mathf.Max(upperLength * upperLength - x * x, 0f);
		float y = Mathf.Sqrt(y2);
		Vector3 desiredJoint = root + targetDirection * x + bendDirection * y;

		Vector3 upperCurrent = joint - root;
		Vector3 upperDesired = desiredJoint - root;
		if (upperCurrent.sqrMagnitude > 1E-08f && upperDesired.sqrMagnitude > 1E-08f)
		{
			Quaternion upperDelta = Quaternion.FromToRotation(upperCurrent, upperDesired);
			upperArm.rotation = upperDelta * upperArm.rotation;
		}

		Vector3 foreCurrent = hand.position - forearm.position;
		Vector3 foreDesired = weightedTarget - forearm.position;
		if (foreCurrent.sqrMagnitude > 1E-08f && foreDesired.sqrMagnitude > 1E-08f)
		{
			Quaternion foreDelta = Quaternion.FromToRotation(foreCurrent, foreDesired);
			forearm.rotation = foreDelta * forearm.rotation;
		}

		if (endEffector.rotationWeight > 0f)
		{
			hand.rotation = Quaternion.Slerp(hand.rotation, endEffector.target.rotation, Mathf.Clamp01(endEffector.rotationWeight));
		}

		handTargetDistanceAfter = GetHandTargetDistance(endEffector, limbMapping);
		return true;
	}

	private IKMapping.BoneMap GetParentBoneMap(FullBodyBipedChain chain)
	{
		if (_ik == null || _ik.solver == null)
		{
			return null;
		}
		return _ik.solver.GetLimbMapping(chain)?.GetBoneMap(IKMappingLimb.BoneMapType.Parent);
	}

	private bool IsArmIkRunning(FullBodyBipedChain chain)
	{
		if (chain == FullBodyBipedChain.LeftArm)
		{
			return _leftArmIkRunning;
		}

		if (chain == FullBodyBipedChain.RightArm)
		{
			return _rightArmIkRunning;
		}

		return false;
	}

	private bool ShouldLogShoulderDiagnostic(FullBodyBipedChain chain)
	{
		if (_settings == null || !_settings.ShoulderDiagnosticLog)
		{
			return false;
		}

		float interval = Mathf.Clamp(_settings.ShoulderDiagnosticLogInterval, 0.05f, 2f);
		float now = Time.unscaledTime;

		if (chain == FullBodyBipedChain.LeftArm)
		{
			if (now < _nextLeftDiagnosticLogTime)
			{
				return false;
			}

			_nextLeftDiagnosticLogTime = now + interval;
			return true;
		}

		if (chain == FullBodyBipedChain.RightArm)
		{
			if (now < _nextRightDiagnosticLogTime)
			{
				return false;
			}

			_nextRightDiagnosticLogTime = now + interval;
			return true;
		}

		return false;
	}

	private static string GetChainLabel(FullBodyBipedChain chain)
	{
		if (chain == FullBodyBipedChain.LeftArm)
		{
			return "左肩";
		}

		if (chain == FullBodyBipedChain.RightArm)
		{
			return "右肩";
		}

		return chain.ToString();
	}

	private static string BoolToYesNo(bool value)
	{
		return value ? "はい" : "いいえ";
	}

	private static float NormalizeSignedAngle(float angle)
	{
		angle %= 360f;
		if (angle > 180f)
		{
			angle -= 360f;
		}
		else if (angle < -180f)
		{
			angle += 360f;
		}

		return angle;
	}

	private static Vector3 NormalizeEulerSigned(Vector3 euler)
	{
		return new Vector3(
			NormalizeSignedAngle(euler.x),
			NormalizeSignedAngle(euler.y),
			NormalizeSignedAngle(euler.z));
	}

	private static string FormatEuler(Vector3 euler)
	{
		return "("
			+ euler.x.ToString("F1") + ","
			+ euler.y.ToString("F1") + ","
			+ euler.z.ToString("F1") + ")";
	}

	private void EmitShoulderDiagnostic(
		FullBodyBipedChain chain,
		bool lowered,
		bool reversed,
		float configuredWeight,
		float configuredOffset,
		float adjustedWeight,
		float adjustedOffset,
		float solverEffectorToBoneDistance,
		float targetToBoneDistance,
		float yDelta,
		float stretchRatio,
		float rawBlend,
		float blend01,
		float solverBlend,
		float effectorWeight,
		float ikPositionWeight,
		bool armChainAligned,
		float handTargetDistanceBefore,
		float handTargetDistanceAfter,
		float fromToAngle,
		float deltaAnglePreReverse,
		float deltaAnglePostReverse,
		float appliedDeltaAngle,
		Vector3 beforeLocalEuler,
		Vector3 afterLocalEuler,
		Vector3 beforeWorldEuler,
		Vector3 afterWorldEuler,
		float elbowAngleBefore,
		float elbowAngleAfter,
		float forearmTargetDirErrorBefore,
		float forearmTargetDirErrorAfter,
		float forearmBendGoalErrorBefore,
		float forearmBendGoalErrorAfter,
		bool bendGoalPlaneUsed,
		string bendGoalName)
	{
		ShoulderIkStabilizerPlugin plugin = ShoulderIkStabilizerPlugin.Instance;
		if (plugin == null)
		{
			return;
		}

		string message = "frame=" + Time.frameCount
			+ " 肩=" + GetChainLabel(chain)
			+ " 腕IK有効判定=はい"
			+ " IKターゲット距離=" + targetToBoneDistance.ToString("F4")
			+ " IKソルバー距離=" + solverEffectorToBoneDistance.ToString("F4")
			+ " 腕下げ判定=" + BoolToYesNo(lowered)
			+ " 反転適用=" + BoolToYesNo(reversed)
			+ " 再計算方式=後段適用(OnPostUpdate/再解決なし)"
			+ " 腕の上下差Y=" + yDelta.ToString("F4")
			+ " 重み(設定→実効)=" + configuredWeight.ToString("F3") + "→" + adjustedWeight.ToString("F3")
			+ " オフセット(設定→実効)=" + configuredOffset.ToString("F3") + "→" + adjustedOffset.ToString("F3")
			+ " 腕の伸び率=" + stretchRatio.ToString("F4")
			+ " 補正強度(raw/0-1/最終)=" + rawBlend.ToString("F4") + "/" + blend01.ToString("F4") + "/" + solverBlend.ToString("F4")
			+ " ソルバー重み(エフェクタ/IK全体)=" + effectorWeight.ToString("F3") + "/" + ikPositionWeight.ToString("F3")
			+ " 腕再整列(2骨IK)=" + BoolToYesNo(armChainAligned)
			+ " BendGoal平面使用=" + BoolToYesNo(bendGoalPlaneUsed)
			+ " BendGoal=" + bendGoalName
			+ " 手先誤差(前→後)=" + handTargetDistanceBefore.ToString("F4") + "→" + handTargetDistanceAfter.ToString("F4")
			+ " 角度(目標差/反転前/反転後/最終)="
			+ fromToAngle.ToString("F3") + "/"
			+ deltaAnglePreReverse.ToString("F3") + "/"
			+ deltaAnglePostReverse.ToString("F3") + "/"
			+ appliedDeltaAngle.ToString("F3")
			+ " 肘角度deg(前→後)=" + elbowAngleBefore.ToString("F2") + "→" + elbowAngleAfter.ToString("F2")
			+ " 前腕目標方向誤差deg(前→後)=" + forearmTargetDirErrorBefore.ToString("F2") + "→" + forearmTargetDirErrorAfter.ToString("F2")
			+ " 前腕BendGoal誤差deg(前→後)=" + forearmBendGoalErrorBefore.ToString("F2") + "→" + forearmBendGoalErrorAfter.ToString("F2")
			+ " 肩向きローカル(適用前→適用後)=" + FormatEuler(beforeLocalEuler) + "→" + FormatEuler(afterLocalEuler)
			+ " 肩向きワールド(適用前→適用後)=" + FormatEuler(beforeWorldEuler) + "→" + FormatEuler(afterWorldEuler);

		plugin.LogShoulderDiagnostic(message);
	}

	private void EmitShoulderIkDisabledDiagnostic(
		FullBodyBipedChain chain,
		bool lowered,
		float yDelta,
		float solverEffectorToBoneDistance,
		float targetToBoneDistance,
		float effectorWeight,
		float ikPositionWeight,
		Vector3 localEuler,
		Vector3 worldEuler)
	{
		ShoulderIkStabilizerPlugin plugin = ShoulderIkStabilizerPlugin.Instance;
		if (plugin == null)
		{
			return;
		}

		string message = "frame=" + Time.frameCount
			+ " 肩=" + GetChainLabel(chain)
			+ " 腕IK有効判定=いいえ"
			+ " ソルバー重み(エフェクタ/IK全体)=" + effectorWeight.ToString("F3") + "/" + ikPositionWeight.ToString("F3")
			+ " IKターゲット距離=" + targetToBoneDistance.ToString("F4")
			+ " IKソルバー距離=" + solverEffectorToBoneDistance.ToString("F4")
			+ " 腕下げ判定=" + BoolToYesNo(lowered)
			+ " 腕の上下差Y=" + yDelta.ToString("F4")
			+ " 補正=スキップ(腕IKオフ)"
			+ " 肩向きローカル(現在)=" + FormatEuler(localEuler)
			+ " 肩向きワールド(現在)=" + FormatEuler(worldEuler);

		plugin.LogShoulderDiagnostic(message);
	}

	private float GetEffectorToBoneDistance(IKEffector endEffector, IKMappingLimb limbMapping)
	{
		if (endEffector == null)
		{
			return float.MaxValue;
		}

		Transform referenceBone = endEffector.bone ?? limbMapping?.bone3 ?? limbMapping?.bone2 ?? limbMapping?.bone1;
		if (referenceBone == null)
		{
			return float.MaxValue;
		}

		return Vector3.Distance(endEffector.position, referenceBone.position);
	}

	private float GetTargetToBoneDistance(IKEffector endEffector, IKMappingLimb limbMapping)
	{
		if (endEffector == null || endEffector.target == null)
		{
			return 0f;
		}

		Transform referenceBone = endEffector.bone ?? limbMapping?.bone3 ?? limbMapping?.bone2 ?? limbMapping?.bone1;
		if (referenceBone == null)
		{
			return 0f;
		}

		return Vector3.Distance(endEffector.target.position, referenceBone.position);
	}

	private float GetHandTargetDistance(IKEffector endEffector, IKMappingLimb limbMapping)
	{
		if (endEffector == null || endEffector.target == null)
		{
			return 0f;
		}

		Transform hand = limbMapping?.bone3 ?? endEffector.bone;
		if (hand == null)
		{
			return 0f;
		}

		return Vector3.Distance(hand.position, endEffector.target.position);
	}

	private static float GetElbowAngleDeg(IKMappingLimb limbMapping)
	{
		if (limbMapping == null || limbMapping.bone1 == null || limbMapping.bone2 == null || limbMapping.bone3 == null)
		{
			return -1f;
		}

		Vector3 upper = limbMapping.bone1.position - limbMapping.bone2.position;
		Vector3 lower = limbMapping.bone3.position - limbMapping.bone2.position;
		if (upper.sqrMagnitude <= 1E-08f || lower.sqrMagnitude <= 1E-08f)
		{
			return -1f;
		}

		return Vector3.Angle(upper, lower);
	}

	private static float GetForearmTargetDirErrorDeg(IKMappingLimb limbMapping, IKEffector endEffector)
	{
		if (limbMapping == null || limbMapping.bone2 == null || limbMapping.bone3 == null || endEffector == null || endEffector.target == null)
		{
			return -1f;
		}

		Vector3 forearmDir = limbMapping.bone3.position - limbMapping.bone2.position;
		Vector3 targetDir = endEffector.target.position - limbMapping.bone2.position;
		if (forearmDir.sqrMagnitude <= 1E-08f || targetDir.sqrMagnitude <= 1E-08f)
		{
			return -1f;
		}

		return Vector3.Angle(forearmDir, targetDir);
	}

	private Transform ResolveBendGoalTransform(FullBodyBipedChain chain)
	{
		if (_ik == null || _ik.solver == null)
		{
			return null;
		}

		IKConstraintBend bend = _ik.solver.GetBendConstraint(chain);
		return bend?.bendGoal;
	}

	private static float GetForearmBendGoalErrorDeg(IKMappingLimb limbMapping, Transform bendGoalTransform)
	{
		if (limbMapping == null || limbMapping.bone2 == null || limbMapping.bone3 == null || bendGoalTransform == null)
		{
			return -1f;
		}

		Vector3 forearmDir = limbMapping.bone3.position - limbMapping.bone2.position;
		Vector3 bendGoalDir = bendGoalTransform.position - limbMapping.bone2.position;
		if (forearmDir.sqrMagnitude <= 1E-08f || bendGoalDir.sqrMagnitude <= 1E-08f)
		{
			return -1f;
		}

		return Vector3.Angle(forearmDir, bendGoalDir);
	}

	private float GetLocalArmYDelta(IKEffector endEffector, IKMappingLimb limbMapping)
	{
		if (_chaRoot == null || endEffector == null || limbMapping == null || limbMapping.bone1 == null)
		{
			return 0f;
		}
		Vector3 vector = _chaRoot.InverseTransformPoint(endEffector.position);
		Vector3 upperLocal = _chaRoot.InverseTransformPoint(limbMapping.bone1.position);
		return (vector - upperLocal).y;
	}
}
