using System.Runtime.Serialization;

namespace MainGameHandySync
{
    [DataContract]
    internal sealed class PluginSettings
    {
        [DataMember(Name = "ConnectionKey")]
        public string ConnectionKey = "";

        [DataMember(Name = "UpdateRateMs")]
        public int UpdateRateMs = 50;

        [DataMember(Name = "StrokeMin")]
        public int StrokeMin = 5;

        [DataMember(Name = "StrokeMax")]
        public int StrokeMax = 95;

        [DataMember(Name = "Invert")]
        public bool Invert = false;

        [DataMember(Name = "SpeedMultiplier")]
        public float SpeedMultiplier = 1.0f;

        [DataMember(Name = "AutoExportFunscript")]
        public bool AutoExportFunscript = true;
    }
}
