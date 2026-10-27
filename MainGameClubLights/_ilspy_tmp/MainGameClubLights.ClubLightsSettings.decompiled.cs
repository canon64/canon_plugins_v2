using System;
using System.Collections.Generic;

namespace MainGameClubLights;

[Serializable]
public sealed class ClubLightsSettings
{
	public List<LightInstanceSettings> Lights = new List<LightInstanceSettings>();

	public List<LightPreset> Presets = new List<LightPreset>();

	public List<VideoPresetMapping> VideoPresetMappings = new List<VideoPresetMapping>();

	public NativeLightSettings NativeLight = new NativeLightSettings();

	public float BeatLowThreshold = 0.4f;

	public float BeatHighThreshold = 0.75f;

	public bool UiVisible;

	public float UiX = 20f;

	public float UiY = 20f;
}
