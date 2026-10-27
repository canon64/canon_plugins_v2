using System.Runtime.Serialization;

namespace MainGameAutoHVoice
{
    [DataContract]
    internal sealed class PluginSettings
    {
        [DataMember] public bool Enabled = true;
        [DataMember] public bool ShowGui = true;
        [DataMember] public bool VerboseLog = false;
        [DataMember] public int TargetMainIndex = 0;
        [DataMember] public float AutoIntervalSeconds = 18f;
        [DataMember] public float MinimumSpacingSeconds = 6f;
        [DataMember] public float CaptureExpireSeconds = 45f;
        [DataMember] public bool RequireModeMatch = true;
        [DataMember] public bool AllowManualTriggerWhenNoCapture = false;
        [DataMember] public float WindowX = 40f;
        [DataMember] public float WindowY = 40f;
    }
}
