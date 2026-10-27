using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MainGameLiveLookCamera
{
    [DataContract]
    internal sealed class PluginSettings
    {
        [DataMember(Name = "UiVisible")]
        public bool UiVisible = true;

        [DataMember(Name = "ApplyFov")]
        public bool ApplyFov = true;

        [DataMember(Name = "DefaultFov")]
        public float DefaultFov = 35f;

        [DataMember(Name = "DetailLogEnabled")]
        public bool DetailLogEnabled = false;

        [DataMember(Name = "WindowX")]
        public float WindowX = 20f;

        [DataMember(Name = "WindowY")]
        public float WindowY = 100f;

        [DataMember(Name = "SelectedSaveMode")]
        public int SelectedSaveMode = 0;

        [DataMember(Name = "SelectedBoneTarget")]
        public int SelectedBoneTarget = 0;

        [DataMember(Name = "PresetName")]
        public string PresetName = string.Empty;

        [DataMember(Name = "Presets")]
        public List<LiveLookPreset> Presets = new List<LiveLookPreset>();
    }

#pragma warning disable 0649
    [DataContract]
    internal sealed class LiveLookPreset
    {
        [DataMember(Name = "Name")]
        public string Name;

        [DataMember(Name = "UseBoneLook")]
        public bool UseBoneLook;

        [DataMember(Name = "TargetPosition")]
        public float[] TargetPosition;

        [DataMember(Name = "CameraDirection")]
        public float[] CameraDirection;

        [DataMember(Name = "Rotation")]
        public float[] Rotation;

        [DataMember(Name = "Fov")]
        public float Fov;

        [DataMember(Name = "BoneTarget")]
        public int BoneTarget;

        [DataMember(Name = "BoneName")]
        public string BoneName;

        [DataMember(Name = "LookAtOffsetLocal")]
        public float[] LookAtOffsetLocal;
    }
#pragma warning restore 0649
}
