using System;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed class ScenarioStateSnapshot
    {
        public DateTime TimestampUtc;
        public bool HasHScene;
        public string ModeName = string.Empty;
        public int ModeValue;
        public string PostureName = string.Empty;
        public string ActionLabel = string.Empty;
        public string ReactionLabel = string.Empty;
        public string MaleFeelLabel = string.Empty;
        public string SpeedFeelLabel = string.Empty;
        public string RecentChangeText = string.Empty;
        public string CurrentSongLabel = string.Empty;
        public string KissLabel = string.Empty;
        public string ArousalLabel = string.Empty;
        public string SensitivityLabel = string.Empty;
        public float SpeedCalc;
        public float FemaleGauge;
        public float MaleGauge;
    }
}
