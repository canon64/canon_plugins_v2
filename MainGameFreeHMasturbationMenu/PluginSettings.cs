using System.Runtime.Serialization;

namespace MainGameFreeHMasturbationMenu
{
    [DataContract]
    internal sealed class PluginSettings
    {
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (string.IsNullOrWhiteSpace(ButtonText))
            {
                ButtonText = "オナニー";
            }

            if (TemplateButtonIndex < 0)
            {
                TemplateButtonIndex = 0;
            }

            if (AnchorButtonIndex < 0)
            {
                AnchorButtonIndex = 0;
            }

            if (PosePanelWidth < 120f)
            {
                PosePanelWidth = 120f;
            }

            if (PosePanelHeight < 80f)
            {
                PosePanelHeight = 80f;
            }

            if (PoseButtonHeight < 22f)
            {
                PoseButtonHeight = 22f;
            }

            if (PosePanelSideGap < 0f)
            {
                PosePanelSideGap = 0f;
            }

            if (WheelSpeedStep < 0f)
            {
                WheelSpeedStep = 0f;
            }

            if (WheelMasturbationMotionStep < 0f)
            {
                WheelMasturbationMotionStep = 0f;
            }

            if (WheelMasturbationMotionMin < 0f)
            {
                WheelMasturbationMotionMin = 0f;
            }

            if (WheelMasturbationMotionMax > 1f)
            {
                WheelMasturbationMotionMax = 1f;
            }

            if (WheelMasturbationMotionMax < WheelMasturbationMotionMin)
            {
                WheelMasturbationMotionMax = WheelMasturbationMotionMin;
            }

            if (MasturbationAnimatorSpeedMin < 0.05f)
            {
                MasturbationAnimatorSpeedMin = 0.05f;
            }

            if (MasturbationAnimatorSpeedMax < MasturbationAnimatorSpeedMin)
            {
                MasturbationAnimatorSpeedMax = MasturbationAnimatorSpeedMin;
            }
        }

        [DataMember(Order = 0)]
        public bool Enabled = true;

        [DataMember(Order = 1)]
        public bool FreeHOnly = true;

        [DataMember(Order = 2)]
        public string ButtonText = "オナニー";

        [DataMember(Order = 3)]
        public float ButtonOffsetX = 0f;

        [DataMember(Order = 4)]
        public float ButtonOffsetY = 120f;

        [DataMember(Order = 5)]
        public int TemplateButtonIndex = 0;

        [DataMember(Order = 6)]
        public int AnchorButtonIndex = 0;

        [DataMember(Order = 7)]
        public bool CycleMasturbationPoses = true;

        [DataMember(Order = 8)]
        public bool StartFromCurrentWhenInMasturbation = true;

        [DataMember(Order = 9)]
        public bool KeepActionMenuVisibleInMasturbation = true;

        [DataMember(Order = 10)]
        public bool AutoRecoverTransitionFromMasturbation = true;

        [DataMember(Order = 11)]
        public bool VerboseLog = false;

        [DataMember(Order = 12)]
        public bool EnablePoseSelectionMenu = true;

        [DataMember(Order = 13)]
        public bool ClosePosePanelAfterSelect = true;

        [DataMember(Order = 14)]
        public float PosePanelOffsetX = 20f;

        [DataMember(Order = 15)]
        public float PosePanelOffsetY = 170f;

        [DataMember(Order = 16)]
        public float PosePanelWidth = 300f;

        [DataMember(Order = 17)]
        public float PosePanelHeight = 290f;

        [DataMember(Order = 18)]
        public float PoseButtonHeight = 30f;

        [DataMember(Order = 19)]
        public bool EnableMouseWheelSpeedControl = true;

        [DataMember(Order = 20)]
        public bool WheelRequireCtrl = false;

        [DataMember(Order = 21)]
        public float WheelSpeedStep = 0.05f;

        [DataMember(Order = 22)]
        public bool PosePanelPlaceLeftOfIcon = true;

        [DataMember(Order = 23)]
        public float PosePanelSideGap = 8f;

        [DataMember(Order = 24)]
        public bool EnableMasturbationMotionOverride = true;

        [DataMember(Order = 25)]
        public bool ResetMasturbationMotionOverrideOnModeChange = true;

        [DataMember(Order = 26)]
        public float WheelMasturbationMotionStep = 0.06f;

        [DataMember(Order = 27)]
        public float WheelMasturbationMotionMin = 0f;

        [DataMember(Order = 28)]
        public float WheelMasturbationMotionMax = 1f;

        [DataMember(Order = 29)]
        public bool EnableMasturbationGaugeSpeedLink = true;

        [DataMember(Order = 30)]
        public bool KeepSpeedGaugeVisibleInMasturbation = true;

        [DataMember(Order = 31)]
        public float MasturbationAnimatorSpeedMin = 0.35f;

        [DataMember(Order = 32)]
        public float MasturbationAnimatorSpeedMax = 2.2f;

        [DataMember(Order = 33)]
        public bool LockMaleGaugeDuringMasturbation = true;

        [DataMember(Order = 34)]
        public bool ForceMasturbationModeContext = true;
    }
}
