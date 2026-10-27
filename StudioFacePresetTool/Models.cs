using System;

namespace StudioFacePresetTool
{
    [Serializable]
    public sealed class FacePresetCollection
    {
        public FacePreset[] Presets = Array.Empty<FacePreset>();
    }

    [Serializable]
    public sealed class FacePreset
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public int Eyebrow;
        public int Eye;
        public int Mouth;
        public float EyeMin;
        public float EyeMax = 1f;
        public float MouthMin;
        public float MouthMax = 1f;
        public int Tears;
        public float Cheek;
    }

    [Serializable]
    public sealed class FacePresetToolSettings
    {
        public bool UiVisible;
        public bool DetailLogEnabled = false;
        public string ToggleUiKey = "F9";
        public WindowRectData Window = new WindowRectData();
        public bool IdleCursorUnlock = true;
        public int SelectedPresetIndex = -1;
    }

    [Serializable]
    public sealed class WindowRectData
    {
        public float X = 40f;
        public float Y = 40f;
        public float Width = 760f;
        public float Height = 620f;
    }
}
