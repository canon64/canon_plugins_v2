using System.Runtime.Serialization;

namespace MainGameAllPoseMap
{
    [DataContract]
    internal sealed class PluginSettings
    {
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            // 旧デフォルト900は民宿マップと競合するため自動マイグレーション
            if (TargetMapNo == 900 || TargetMapNo == 9000) TargetMapNo = 5682352;
            if (TargetMapNo <= 0) TargetMapNo = 5682352;
            if (string.IsNullOrWhiteSpace(VirtualPointAnchorName)) VirtualPointAnchorName = "h_free";
            if (VirtualPointsPerRing <= 0) VirtualPointsPerRing = 12;
            if (VirtualPointRadius < 0.1f) VirtualPointRadius = 1.5f;
            if (VirtualPointRingStep < 0f) VirtualPointRingStep = 0f;
            if (string.IsNullOrWhiteSpace(CategoriesOverrideCsv)) CategoriesOverrideCsv = string.Empty;
        }

        [DataMember(Order = 0)]
        public bool Enabled = true;

        [DataMember(Order = 1)]
        public int TargetMapNo = 5682352;

        [DataMember(Order = 2)]
        public bool EnableAllPoseInFreeH = true;

        [DataMember(Order = 3)]
        public bool BypassFreeHProgressLocks = true;

        [DataMember(Order = 4)]
        public bool DisableSpecialMapJump = true;

        [DataMember(Order = 5)]
        public bool EnableVirtualPoints = true;

        [DataMember(Order = 6)]
        public bool KeepOriginalClosePoints = false;

        [DataMember(Order = 7)]
        public string VirtualPointAnchorName = "h_free";

        [DataMember(Order = 8)]
        public int VirtualPointsPerRing = 12;

        [DataMember(Order = 9)]
        public float VirtualPointRadius = 1.5f;

        [DataMember(Order = 10)]
        public float VirtualPointRingStep = 0.8f;

        [DataMember(Order = 11)]
        public float VirtualPointHeightOffset = 0f;

        [DataMember(Order = 12)]
        public float VirtualPointVerticalStep = 0.2f;

        [DataMember(Order = 13)]
        public string CategoriesOverrideCsv = "";

        [DataMember(Order = 14)]
        public bool VerboseLog = false;
    }
}
