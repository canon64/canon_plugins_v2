using System.Runtime.Serialization;

namespace MainGameVrHipIkVoiceImpactBoost
{
    // JSONマスター用の素のDTO。
    // ConfigEntry のデフォルト値と必ず一致させる(初回JSON書き出し時の比較で食い違いが出ないように)。
    [DataContract]
    internal sealed class SettingsJsonDto
    {
        [DataMember] public bool Enabled = true;
        [DataMember] public bool MoanModeEnabled = false;
        [DataMember] public bool VerboseLog = false;
        [DataMember] public bool RequireVrActive = true;
        [DataMember] public bool RequireBodyIkRunning = true;

        [DataMember] public float VelocityThresholdMps = 0.45f;
        [DataMember] public float MinDeltaMeters = 0.005f;
        [DataMember] public float SmoothingFactor = 0.35f;
        [DataMember] public int CooldownMs = 250;

        [DataMember] public bool EnableSpeedScaling = true;
        [DataMember] public float SpeedMaxMps = 1.60f;
        [DataMember] public float MinPeakMultiplier = 1.35f;
        [DataMember] public float MaxPeakMultiplier = 2.00f;
        [DataMember] public int MinSilenceMs = 60;
        [DataMember] public int MaxSilenceMs = 180;

        [DataMember] public float PeakMultiplier = 1.8f;
        [DataMember] public int AttackMs = 20;
        [DataMember] public int HoldMs = 20;
        [DataMember] public int ReleaseMs = 220;
        [DataMember] public int SilenceMs = 100;
        [DataMember] public string Easing = "CosineInOut";
    }
}
