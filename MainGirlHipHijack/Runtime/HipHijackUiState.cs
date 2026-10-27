using UnityEngine;

namespace MainGirlHipHijack.Runtime
{
    internal sealed class HipHijackUiState
    {
        internal Rect WindowRect = new Rect(20f, 20f, 620f, 620f);
        internal Vector2 Scroll;
        internal string NotificationText;
        internal float NotificationEndTime;
        internal string FemaleLeftHandPoseIndexText = "0";
        internal string FemaleRightHandPoseIndexText = "0";
        internal string FemalePosePresetNameDraft = "pose";
        internal string MalePosePresetNameDraft = "male_pose";
        internal bool StatusExpanded;
        internal bool VrExpanded;
        internal bool HipExpanded;
        internal bool BodyIkExpanded;
        internal bool PoseExpanded;
        internal bool MaleExpanded;
        internal bool HSceneParametersExpanded;
        internal bool MaleHmdParametersExpanded;
    }
}
