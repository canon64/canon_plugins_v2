using System;

namespace MainGirlShoulderIkStabilizer;

[Serializable]
internal sealed class PluginSettings
{
	public bool Enabled = true;

	public bool VerboseLog = false;

	public bool ShoulderDiagnosticLog;

	public float ShoulderDiagnosticLogInterval = 0.2f;

	public bool ShoulderRotationEnabled = true;

	public bool IndependentShoulders;

	public bool ReverseShoulderL;

	public bool ReverseShoulderR;

	public float ShoulderWeight = 1.5f;

	public float ShoulderOffset = 0.2f;

	public float ShoulderRightWeight = 1.5f;

	public float ShoulderRightOffset = 0.2f;

	public float LoweredArmScale = 0.67f;

	public float RaisedArmStartY = 0.03f;

	public float RaisedArmFullY = 0.22f;

	public float RaisedArmScaleMin = 0.25f;

	public float MaxShoulderDeltaAngleDeg = 35f;

	public float MaxSolverBlend = 0.8f;
}
