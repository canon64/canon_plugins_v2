using System.Runtime.Serialization;

namespace MainGameVrVoiceGate
{
    [DataContract]
    internal sealed class PluginSettings
    {
        [DataMember] public bool Enabled = true;
        [DataMember] public bool VerboseLog = false;
        [DataMember] public bool EnableCtrlRReload = true;

        [DataMember] public string UdpHost = "127.0.0.1";
        [DataMember] public int UdpPort = 17911;
        [DataMember] public string AuthToken = "";
        [DataMember] public float HeartbeatSeconds = 0.35f;
        [DataMember] public string RecordButton = "k_EButton_Axis0";

        [DataMember] public bool OverlayEnabled = true;
        [DataMember] public string OverlayText = "録音中";
        [DataMember] public float OverlayX = -1f;
        [DataMember] public float OverlayY = 40f;
        [DataMember] public float OverlayWidth = 220f;
        [DataMember] public float OverlayHeight = 62f;
        [DataMember] public int OverlayFontSize = 28;
        [DataMember] public float OverlayAlpha = 0.82f;

        internal void Normalize()
        {
            if (string.IsNullOrWhiteSpace(UdpHost))
                UdpHost = "127.0.0.1";

            if (UdpPort < 1)
                UdpPort = 1;
            if (UdpPort > 65535)
                UdpPort = 65535;

            if (AuthToken == null)
                AuthToken = "";

            if (string.IsNullOrWhiteSpace(RecordButton))
                RecordButton = "k_EButton_Axis0";

            if (HeartbeatSeconds < 0.1f)
                HeartbeatSeconds = 0.1f;
            if (HeartbeatSeconds > 5f)
                HeartbeatSeconds = 5f;

            if (string.IsNullOrWhiteSpace(OverlayText))
                OverlayText = "録音中";

            if (OverlayWidth < 80f)
                OverlayWidth = 80f;
            if (OverlayWidth > 1200f)
                OverlayWidth = 1200f;

            if (OverlayHeight < 30f)
                OverlayHeight = 30f;
            if (OverlayHeight > 300f)
                OverlayHeight = 300f;

            if (OverlayFontSize < 10)
                OverlayFontSize = 10;
            if (OverlayFontSize > 96)
                OverlayFontSize = 96;

            if (OverlayAlpha < 0.1f)
                OverlayAlpha = 0.1f;
            if (OverlayAlpha > 1f)
                OverlayAlpha = 1f;
        }
    }
}
