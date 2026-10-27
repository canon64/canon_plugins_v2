using System;

namespace MainGameClubLights;

[Serializable]
public sealed class LightInstanceSettings
{
	public string Id = "";

	public string Name = "Light";

	public bool Enabled = true;

	public bool FollowCamera;

	public float OffsetX;

	public float OffsetY = 1f;

	public float OffsetZ = 2f;

	public float WorldPosX;

	public float WorldPosY = 1f;

	public float WorldPosZ = 2f;

	public float Intensity = 2f;

	public float Range = 10f;

	public float SpotAngle = 179f;

	public float InnerSpotAngle;

	public float ColorR = 1f;

	public float ColorG = 1f;

	public float ColorB = 1f;

	public bool ShowMarker = true;

	public bool ShowArrow = true;

	public bool ShowGizmo = true;

	public float MarkerSize = 0.08f;

	public float ArrowScale = 1f;

	public float GizmoSize = 1f;

	public float RotX;

	public float RotY;

	public float RotZ;

	public bool LookAtFemale;

	public float LookAtOffsetX;

	public float LookAtOffsetY;

	public float LookAtOffsetZ;

	public bool RevolutionEnabled;

	public float RevolutionRadius = 2f;

	public float RevolutionSpeed = 45f;

	public float RevolutionCenterX;

	public float RevolutionCenterY = 1f;

	public float RevolutionCenterZ = 2f;

	public bool RotationEnabled;

	public float RotationSpeed = 90f;

	[NonSerialized]
	public float RainbowHue;

	[NonSerialized]
	public float RevolutionAngleDeg;

	[NonSerialized]
	public float RotationAngleDeg;

	[NonSerialized]
	public bool SpotAnglePinnedByUser;

	public RainbowSettings Rainbow = new RainbowSettings();

	public StrobeSettings Strobe = new StrobeSettings();

	public BeatPresetAssignment Beat = new BeatPresetAssignment();

	public LoopSettings IntensityLoop = new LoopSettings
	{
		MinValue = 0.5f,
		MaxValue = 1f,
		SpeedHz = 0.3f
	};

	public LoopSettings RangeLoop = new LoopSettings
	{
		MinValue = 1f,
		MaxValue = 10f
	};

	public LoopSettings SpotAngleLoop = new LoopSettings
	{
		MinValue = 10f,
		MaxValue = 60f
	};

	public MirrorballCookieSettings Mirrorball = new MirrorballCookieSettings();
}
