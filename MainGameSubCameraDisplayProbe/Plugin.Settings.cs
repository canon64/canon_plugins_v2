using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    [DataContract]
    internal sealed class ProbeSettings
    {
        [DataMember(Name = "DisplayBrightness")]
        public float DisplayBrightness = 1f;
        [DataMember(Name = "Enabled")]
        public bool Enabled = true;
        [DataMember(Name = "LoggingEnabled")]
        public bool LoggingEnabled = false;
        [DataMember(Name = "UiVisible")]
        public bool UiVisible = false;
        [DataMember(Name = "ToggleUiKey")]
        public KeyCode ToggleUiKey = KeyCode.F8;
        [DataMember(Name = "AutoCreateOnStart")]
        public bool AutoCreateOnStart = true;
        [DataMember(Name = "VoiceFaceEventSubCameraTriggerEnabled")]
        public bool VoiceFaceEventSubCameraTriggerEnabled = true;

        [DataMember(Name = "WindowX")]
        public float WindowX = 20f;
        [DataMember(Name = "WindowY")]
        public float WindowY = 20f;
        [DataMember(Name = "WindowWidth")]
        public float WindowWidth = 560f;
        [DataMember(Name = "WindowHeight")]
        public float WindowHeight = 520f;

        [DataMember(Name = "RenderWidth")]
        public int RenderWidth = 640;
        [DataMember(Name = "RenderHeight")]
        public int RenderHeight = 360;
        [DataMember(Name = "RenderFilterMode")]
        public string RenderFilterMode = "Point";
        [DataMember(Name = "RenderCustomPresets")]
        public RenderResolutionPreset[] RenderCustomPresets = new RenderResolutionPreset[0];
        [DataMember(Name = "UseDisplayOverlayCamera")]
        public bool UseDisplayOverlayCamera = true;
        [DataMember(Name = "DisplayLayer")]
        public int DisplayLayer = 30;
        [DataMember(Name = "CameraFieldOfView")]
        public float CameraFieldOfView = 60f;
        [DataMember(Name = "CameraNearClip")]
        public float CameraNearClip = 0.03f;
        [DataMember(Name = "CameraFarClip")]
        public float CameraFarClip = 500f;
        [DataMember(Name = "PresetName")]
        public string PresetName = "SubCamera";
        [DataMember(Name = "SelectedSaveMode")]
        public int SelectedSaveMode = 0;
        [DataMember(Name = "SelectedBoneTarget")]
        public int SelectedBoneTarget = 0;
        [DataMember(Name = "SaveBoneCameraPosition")]
        public bool SaveBoneCameraPosition = false;
        [DataMember(Name = "SaveCameraPoseOverrides")]
        public bool SaveCameraPoseOverrides = false;
        [DataMember(Name = "Presets")]
        public SubCameraPreset[] Presets = new SubCameraPreset[0];
        [DataMember(Name = "DisplayPresetName")]
        public string DisplayPresetName = "Display";
        [DataMember(Name = "SaveDisplayPoseOverrides")]
        public bool SaveDisplayPoseOverrides = false;
        [DataMember(Name = "DisplayPresets")]
        public DisplayPreset[] DisplayPresets = new DisplayPreset[0];
        [DataMember(Name = "PosePresetAutoApply")]
        public bool PosePresetAutoApply = false;
        [DataMember(Name = "PosePresets")]
        public PosePreset[] PosePresets = new PosePreset[0];

        [DataMember(Name = "SpawnDistance")]
        public float SpawnDistance = 0.3f;
        [DataMember(Name = "CameraPosX")]
        public float CameraPosX = 0f;
        [DataMember(Name = "CameraPosY")]
        public float CameraPosY = 1.2f;
        [DataMember(Name = "CameraPosZ")]
        public float CameraPosZ = 0.8f;
        [DataMember(Name = "CameraRotX")]
        public float CameraRotX = 10f;
        [DataMember(Name = "CameraRotY")]
        public float CameraRotY = 180f;
        [DataMember(Name = "CameraRotZ")]
        public float CameraRotZ = 0f;

        [DataMember(Name = "DisplayPosX")]
        public float DisplayPosX = 0f;
        [DataMember(Name = "DisplayPosY")]
        public float DisplayPosY = 1.0f;
        [DataMember(Name = "DisplayPosZ")]
        public float DisplayPosZ = 1.5f;
        [DataMember(Name = "DisplayRotX")]
        public float DisplayRotX = 0f;
        [DataMember(Name = "DisplayRotY")]
        public float DisplayRotY = 180f;
        [DataMember(Name = "DisplayRotZ")]
        public float DisplayRotZ = 0f;
        [DataMember(Name = "DisplayWidth")]
        public float DisplayWidth = 1.0f;
        [DataMember(Name = "DisplayHeight")]
        public float DisplayHeight = 1.0f;
        [DataMember(Name = "DisplayAspectInitialized")]
        public bool DisplayAspectInitialized = true;

        [DataMember(Name = "GizmoSizeMultiplier")]
        public float GizmoSizeMultiplier = 1f;
        [DataMember(Name = "CameraVisualScale")]
        public float CameraVisualScale = 1f;
        [DataMember(Name = "CameraGizmoVisible")]
        public bool CameraGizmoVisible = true;
        [DataMember(Name = "DisplayGizmoVisible")]
        public bool DisplayGizmoVisible = true;

        [DataMember(Name = "TransitionSeconds")]
        public float TransitionSeconds = 1.0f;
        [DataMember(Name = "TransitionEasing")]
        public int TransitionEasing = 0;

        [DataMember(Name = "BeatFovEnabled")]
        public bool BeatFovEnabled = false;
        [DataMember(Name = "BeatFovZoomMultiplier")]
        public float BeatFovZoomMultiplier = 1.5f;

        [DataMember(Name = "AutoCycleEnabled")]
        public bool AutoCycleEnabled = false;
        [DataMember(Name = "AutoCycleIntervalSeconds")]
        public float AutoCycleIntervalSeconds = 5f;
        [DataMember(Name = "AutoCycleRandomOrder")]
        public bool AutoCycleRandomOrder = false;
        [DataMember(Name = "AutoCyclePauseAfterExternalSeconds")]
        public float AutoCyclePauseAfterExternalSeconds = 15f;

        [DataMember(Name = "CharactersOnlyMode")]
        public bool CharactersOnlyMode = false;

        [DataMember(Name = "EnableVrGrip")]
        public bool EnableVrGrip = true;
        [DataMember(Name = "ExternalGripOffset")]
        public float[] ExternalGripOffset;
        [DataMember(Name = "VrGripStartDistance")]
        public float VrGripStartDistance = 0.25f;
        [DataMember(Name = "VrFovStickEnabled")]
        public bool VrFovStickEnabled = true;
        [DataMember(Name = "VrFovStickSpeed")]
        public float VrFovStickSpeed = 30f;
        [DataMember(Name = "VrFovStickDeadzone")]
        public float VrFovStickDeadzone = 0.2f;
        [DataMember(Name = "GripMovesCamera")]
        public bool GripMovesCamera = true;
        [DataMember(Name = "GripMovesDisplay")]
        public bool GripMovesDisplay = false;
        [DataMember(Name = "ShowHandPreviewWhileGrabbingCamera")]
        public bool ShowHandPreviewWhileGrabbingCamera = true;
        [DataMember(Name = "HandPreviewOffsetX")]
        public float HandPreviewOffsetX = 0f;
        [DataMember(Name = "HandPreviewOffsetY")]
        public float HandPreviewOffsetY = 0.14f;
        [DataMember(Name = "HandPreviewOffsetZ")]
        public float HandPreviewOffsetZ = -0.18f;
        [DataMember(Name = "PhotoCaptureOnTrigger")]
        public bool PhotoCaptureOnTrigger = true;
        [DataMember(Name = "PhotoOutputFolder")]
        public string PhotoOutputFolder = "image";
        [DataMember(Name = "VideoTriggerHoldSeconds")]
        public float VideoTriggerHoldSeconds = 0.5f;
        [DataMember(Name = "VideoOutputFolder")]
        public string VideoOutputFolder = "video";
        [DataMember(Name = "VideoFps")]
        public float VideoFps = 30f;
        [DataMember(Name = "VideoBitrateKbps")]
        public float VideoBitrateKbps = 1500f;
        [DataMember(Name = "VideoEncoder")]
        public string VideoEncoder = "h264_nvenc";
        [DataMember(Name = "VideoFfmpegPath")]
        public string VideoFfmpegPath = "..\\..\\_tools\\ffmpeg\\bin\\ffmpeg.exe";
    }

    [DataContract]
    internal sealed class RenderResolutionPreset
    {
        [DataMember(Name = "Name")]
        public string Name;
        [DataMember(Name = "Width")]
        public int Width;
        [DataMember(Name = "Height")]
        public int Height;
    }

    [DataContract]
    internal sealed class ActorCameraShot
    {
        [DataMember(Name = "UseBoneLink")] public bool UseBoneLink;
        [DataMember(Name = "BoneTarget")] public int BoneTarget;
        [DataMember(Name = "BoneName")] public string BoneName;
        [DataMember(Name = "SaveCameraPosition")] public bool SaveCameraPosition;
        [DataMember(Name = "CameraPosition")] public float[] CameraPosition;
        [DataMember(Name = "CameraRotation")] public float[] CameraRotation;
        [DataMember(Name = "CameraOffsetLocal")] public float[] CameraOffsetLocal;
        [DataMember(Name = "LookAtPosition")] public float[] LookAtPosition;
        [DataMember(Name = "LookAtOffsetLocal")] public float[] LookAtOffsetLocal;
        [DataMember(Name = "Fov")] public float Fov;
    }

    [DataContract]
    internal sealed class SubCameraPreset
    {
        [DataMember(Name = "Name")]
        public string Name;
        [DataMember(Name = "UseBoneLink")]
        public bool UseBoneLink;
        [DataMember(Name = "BoneTarget")]
        public int BoneTarget;
        [DataMember(Name = "BoneName")]
        public string BoneName;
        [DataMember(Name = "SaveCameraPosition")]
        public bool SaveCameraPosition;
        [DataMember(Name = "CameraPosition")]
        public float[] CameraPosition;
        [DataMember(Name = "CameraRotation")]
        public float[] CameraRotation;
        [DataMember(Name = "CameraOffsetLocal")]
        public float[] CameraOffsetLocal;
        [DataMember(Name = "LookAtPosition")]
        public float[] LookAtPosition;
        [DataMember(Name = "LookAtOffsetLocal")]
        public float[] LookAtOffsetLocal;
        [DataMember(Name = "Fov")]
        public float Fov;
        [DataMember(Name = "AutoCycleInclude")]
        public bool AutoCycleInclude;
        [DataMember(Name = "UsePoseOverrides")]
        public bool UsePoseOverrides;
        [DataMember(Name = "PoseOverrides")]
        public CameraPoseOverride[] PoseOverrides;
        [DataMember(Name = "ActorCamera", EmitDefaultValue = false)]
        public ActorCameraShot ActorCamera;
    }

    [DataContract]
    internal sealed class PosePreset
    {
        [DataMember(Name = "Key")]
        public string Key;
        [DataMember(Name = "DisplayName")]
        public string DisplayName;
        [DataMember(Name = "CameraPosX")]
        public float CameraPosX;
        [DataMember(Name = "CameraPosY")]
        public float CameraPosY;
        [DataMember(Name = "CameraPosZ")]
        public float CameraPosZ;
        [DataMember(Name = "CameraRotX")]
        public float CameraRotX;
        [DataMember(Name = "CameraRotY")]
        public float CameraRotY;
        [DataMember(Name = "CameraRotZ")]
        public float CameraRotZ;
        [DataMember(Name = "CameraFov")]
        public float CameraFov;
        [DataMember(Name = "DisplayPosX")]
        public float DisplayPosX;
        [DataMember(Name = "DisplayPosY")]
        public float DisplayPosY;
        [DataMember(Name = "DisplayPosZ")]
        public float DisplayPosZ;
        [DataMember(Name = "DisplayRotX")]
        public float DisplayRotX;
        [DataMember(Name = "DisplayRotY")]
        public float DisplayRotY;
        [DataMember(Name = "DisplayRotZ")]
        public float DisplayRotZ;
        [DataMember(Name = "DisplayHeight")]
        public float DisplayHeight;
    }

    [DataContract]
    internal sealed class CameraPoseOverride
    {
        [DataMember(Name = "Key")]
        public string Key;
        [DataMember(Name = "DisplayName")]
        public string DisplayName;
        [DataMember(Name = "UseBoneLink")]
        public bool UseBoneLink;
        [DataMember(Name = "BoneTarget")]
        public int BoneTarget;
        [DataMember(Name = "BoneName")]
        public string BoneName;
        [DataMember(Name = "SaveCameraPosition")]
        public bool SaveCameraPosition;
        [DataMember(Name = "CameraPosition")]
        public float[] CameraPosition;
        [DataMember(Name = "CameraRotation")]
        public float[] CameraRotation;
        [DataMember(Name = "CameraOffsetLocal")]
        public float[] CameraOffsetLocal;
        [DataMember(Name = "LookAtPosition")]
        public float[] LookAtPosition;
        [DataMember(Name = "LookAtOffsetLocal")]
        public float[] LookAtOffsetLocal;
        [DataMember(Name = "Fov")]
        public float Fov;
    }

    [DataContract]
    internal sealed class DisplayPreset
    {
        [DataMember(Name = "Name")]
        public string Name;
        [DataMember(Name = "Position")]
        public float[] Position;
        [DataMember(Name = "Rotation")]
        public float[] Rotation;
        [DataMember(Name = "Width")]
        public float Width;
        [DataMember(Name = "Height")]
        public float Height;
        [DataMember(Name = "UsePoseOverrides")]
        public bool UsePoseOverrides;
        [DataMember(Name = "PoseOverrides")]
        public DisplayPoseOverride[] PoseOverrides;
    }

    [DataContract]
    internal sealed class DisplayPoseOverride
    {
        [DataMember(Name = "Key")]
        public string Key;
        [DataMember(Name = "DisplayName")]
        public string DisplayName;
        [DataMember(Name = "Position")]
        public float[] Position;
        [DataMember(Name = "Rotation")]
        public float[] Rotation;
        [DataMember(Name = "Width")]
        public float Width;
        [DataMember(Name = "Height")]
        public float Height;
    }

    internal static class SettingsStore
    {
        internal static ProbeSettings LoadOrCreate(string path, Action<string> logInfo, Action<string> logWarn)
        {
            try
            {
                if (!File.Exists(path))
                {
                    ProbeSettings created = Normalize(new ProbeSettings());
                    Save(path, created, logWarn);
                    logInfo?.Invoke("settings created: " + path);
                    return created;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                ProbeSettings loaded = Deserialize(json) ?? new ProbeSettings();
                bool saveLoaded = false;
                if (json.IndexOf("\"DisplayBrightness\"", StringComparison.Ordinal) < 0) { loaded.DisplayBrightness = 1f; saveLoaded = true; }
                if (json.IndexOf("\"DisplayAspectInitialized\"", StringComparison.Ordinal) < 0)
                {
                    ApplyDefaultDisplayAspect(loaded);
                    loaded.DisplayAspectInitialized = true;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"ShowHandPreviewWhileGrabbingCamera\"", StringComparison.Ordinal) < 0)
                {
                    loaded.ShowHandPreviewWhileGrabbingCamera = true;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"HandPreviewOffsetX\"", StringComparison.Ordinal) < 0)
                {
                    loaded.HandPreviewOffsetX = 0f;
                    loaded.HandPreviewOffsetY = 0.14f;
                    loaded.HandPreviewOffsetZ = -0.18f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"PhotoCaptureOnTrigger\"", StringComparison.Ordinal) < 0)
                {
                    loaded.PhotoCaptureOnTrigger = true;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"PhotoOutputFolder\"", StringComparison.Ordinal) < 0)
                {
                    loaded.PhotoOutputFolder = "image";
                    saveLoaded = true;
                }
                if (json.IndexOf("\"VideoOutputFolder\"", StringComparison.Ordinal) < 0)
                {
                    loaded.VideoOutputFolder = "video";
                    saveLoaded = true;
                }
                if (json.IndexOf("\"VideoTriggerHoldSeconds\"", StringComparison.Ordinal) < 0)
                {
                    loaded.VideoTriggerHoldSeconds = 0.5f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"VideoFps\"", StringComparison.Ordinal) < 0)
                {
                    loaded.VideoFps = 30f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"VideoBitrateKbps\"", StringComparison.Ordinal) < 0)
                {
                    loaded.VideoBitrateKbps = 1500f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"VideoEncoder\"", StringComparison.Ordinal) < 0)
                {
                    loaded.VideoEncoder = "h264_nvenc";
                    saveLoaded = true;
                }
                if (json.IndexOf("\"VideoFfmpegPath\"", StringComparison.Ordinal) < 0)
                {
                    loaded.VideoFfmpegPath = "..\\..\\_tools\\ffmpeg\\bin\\ffmpeg.exe";
                    saveLoaded = true;
                }
                if (json.IndexOf("\"VrFovStickEnabled\"", StringComparison.Ordinal) < 0)
                {
                    loaded.VrFovStickEnabled = true;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"VrFovStickSpeed\"", StringComparison.Ordinal) < 0)
                {
                    loaded.VrFovStickSpeed = 30f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"VrFovStickDeadzone\"", StringComparison.Ordinal) < 0)
                {
                    loaded.VrFovStickDeadzone = 0.2f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"TransitionSeconds\"", StringComparison.Ordinal) < 0)
                {
                    loaded.TransitionSeconds = 1.0f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"TransitionEasing\"", StringComparison.Ordinal) < 0)
                {
                    loaded.TransitionEasing = 0;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"BeatFovEnabled\"", StringComparison.Ordinal) < 0)
                {
                    loaded.BeatFovEnabled = false;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"BeatFovZoomMultiplier\"", StringComparison.Ordinal) < 0)
                {
                    loaded.BeatFovZoomMultiplier = 1.5f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"CameraVisualScale\"", StringComparison.Ordinal) < 0)
                {
                    loaded.CameraVisualScale = 1f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"AutoCycleEnabled\"", StringComparison.Ordinal) < 0)
                {
                    loaded.AutoCycleEnabled = false;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"AutoCycleIntervalSeconds\"", StringComparison.Ordinal) < 0)
                {
                    loaded.AutoCycleIntervalSeconds = 5f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"AutoCycleRandomOrder\"", StringComparison.Ordinal) < 0)
                {
                    loaded.AutoCycleRandomOrder = false;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"AutoCyclePauseAfterExternalSeconds\"", StringComparison.Ordinal) < 0)
                {
                    loaded.AutoCyclePauseAfterExternalSeconds = 15f;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"CharactersOnlyMode\"", StringComparison.Ordinal) < 0)
                {
                    loaded.CharactersOnlyMode = false;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"VoiceFaceEventSubCameraTriggerEnabled\"", StringComparison.Ordinal) < 0)
                {
                    loaded.VoiceFaceEventSubCameraTriggerEnabled = true;
                    saveLoaded = true;
                }
                if (json.IndexOf("\"LoggingEnabled\"", StringComparison.Ordinal) < 0)
                {
                    loaded.LoggingEnabled = false;
                    saveLoaded = true;
                }
                if (saveLoaded)
                {
                    Save(path, loaded, logWarn);
                }
                return Normalize(loaded);
            }
            catch (Exception ex)
            {
                logWarn?.Invoke("settings load failed: " + ex.Message);
                return Normalize(new ProbeSettings());
            }
        }

        internal static void Save(string path, ProbeSettings settings, Action<string> logWarn)
        {
            try
            {
                ProbeSettings normalized = Normalize(settings ?? new ProbeSettings());
                string json = Serialize(normalized);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                logWarn?.Invoke("settings save failed: " + ex.Message);
            }
        }

        private static ProbeSettings Normalize(ProbeSettings settings)
        {
            if (settings == null)
                settings = new ProbeSettings();

            settings.WindowWidth = Mathf.Max(420f, Round2(settings.WindowWidth));
            settings.WindowHeight = Mathf.Max(320f, Round2(settings.WindowHeight));
            settings.WindowX = Round2(settings.WindowX);
            settings.WindowY = Round2(settings.WindowY);
            settings.RenderWidth = Mathf.Clamp(settings.RenderWidth, 64, 4096);
            settings.RenderHeight = Mathf.Clamp(settings.RenderHeight, 64, 4096);
            settings.RenderCustomPresets = NormalizeRenderResolutionPresets(settings.RenderCustomPresets);
            if (string.IsNullOrWhiteSpace(settings.RenderFilterMode))
                settings.RenderFilterMode = "Point";
            settings.DisplayLayer = Mathf.Clamp(settings.DisplayLayer, 0, 30);
            settings.CameraFieldOfView = Mathf.Clamp(Round2(settings.CameraFieldOfView), 10f, 170f);
            settings.CameraNearClip = Mathf.Max(0.01f, Round2(settings.CameraNearClip));
            settings.CameraFarClip = Mathf.Max(settings.CameraNearClip + 1f, Round2(settings.CameraFarClip));
            if (string.IsNullOrWhiteSpace(settings.PresetName))
                settings.PresetName = "SubCamera";
            settings.SelectedSaveMode = Mathf.Clamp(settings.SelectedSaveMode, 0, 1);
            settings.SelectedBoneTarget = Mathf.Clamp(settings.SelectedBoneTarget, 0, 2);
            settings.Presets = NormalizePresets(settings.Presets);
            if (string.IsNullOrWhiteSpace(settings.DisplayPresetName))
                settings.DisplayPresetName = "Display";
            settings.DisplayPresets = NormalizeDisplayPresets(settings.DisplayPresets);
            settings.PosePresets = NormalizePosePresets(settings.PosePresets);
            settings.SpawnDistance = Mathf.Clamp(Round2(settings.SpawnDistance), 0.1f, 8f);
            settings.CameraPosX = Round2(settings.CameraPosX);
            settings.CameraPosY = Round2(settings.CameraPosY);
            settings.CameraPosZ = Round2(settings.CameraPosZ);
            settings.CameraRotX = Round2(settings.CameraRotX);
            settings.CameraRotY = Round2(settings.CameraRotY);
            settings.CameraRotZ = Round2(settings.CameraRotZ);
            settings.DisplayPosX = Round2(settings.DisplayPosX);
            settings.DisplayPosY = Round2(settings.DisplayPosY);
            settings.DisplayPosZ = Round2(settings.DisplayPosZ);
            settings.DisplayRotX = Round2(settings.DisplayRotX);
            settings.DisplayRotY = Round2(settings.DisplayRotY);
            settings.DisplayRotZ = Round2(settings.DisplayRotZ);
            settings.DisplayHeight = Mathf.Max(0.1f, Round2(settings.DisplayHeight));
            settings.DisplayWidth = CalculateDisplayWidth(settings);
            settings.GizmoSizeMultiplier = Mathf.Clamp(Round2(settings.GizmoSizeMultiplier), 0.2f, 4f);
            settings.CameraVisualScale = Mathf.Clamp(Round2(settings.CameraVisualScale), 0.1f, 3f);
            settings.TransitionSeconds = Mathf.Clamp(Round2(settings.TransitionSeconds), 0f, 3f);
            settings.TransitionEasing = Mathf.Clamp(settings.TransitionEasing, 0, 3);
            settings.BeatFovZoomMultiplier = Mathf.Clamp(Round2(settings.BeatFovZoomMultiplier), 1f, 3f);
            settings.AutoCycleIntervalSeconds = Mathf.Clamp(Round2(settings.AutoCycleIntervalSeconds), 1f, 60f);
            settings.AutoCyclePauseAfterExternalSeconds = Mathf.Clamp(Round2(settings.AutoCyclePauseAfterExternalSeconds), 0f, 120f);
            settings.EnableVrGrip = true;
            settings.VrGripStartDistance = Mathf.Clamp(Round2(settings.VrGripStartDistance), 0.05f, 1.5f);
            settings.VrFovStickSpeed = Mathf.Clamp(Round2(settings.VrFovStickSpeed), 1f, 120f);
            settings.VrFovStickDeadzone = Mathf.Clamp(Round2(settings.VrFovStickDeadzone), 0f, 0.95f);
            settings.HandPreviewOffsetX = Mathf.Clamp(Round2(settings.HandPreviewOffsetX), -1f, 1f);
            settings.HandPreviewOffsetY = Mathf.Clamp(Round2(settings.HandPreviewOffsetY), -1f, 1f);
            settings.HandPreviewOffsetZ = Mathf.Clamp(Round2(settings.HandPreviewOffsetZ), -1f, 1f);
            if (string.IsNullOrWhiteSpace(settings.PhotoOutputFolder))
                settings.PhotoOutputFolder = "image";
            settings.PhotoOutputFolder = settings.PhotoOutputFolder.Trim();
            if (string.IsNullOrWhiteSpace(settings.VideoOutputFolder))
                settings.VideoOutputFolder = "video";
            settings.VideoOutputFolder = settings.VideoOutputFolder.Trim();
            settings.VideoTriggerHoldSeconds = Mathf.Clamp(Round2(settings.VideoTriggerHoldSeconds), 0.1f, 5f);
            settings.VideoFps = Mathf.Clamp(Round2(settings.VideoFps), 1f, 120f);
            settings.VideoBitrateKbps = Mathf.Clamp(Round2(settings.VideoBitrateKbps), 300f, 50000f);
            settings.VideoEncoder = NormalizeVideoEncoder(settings.VideoEncoder);
            if (string.IsNullOrWhiteSpace(settings.VideoFfmpegPath))
                settings.VideoFfmpegPath = "..\\..\\_tools\\ffmpeg\\bin\\ffmpeg.exe";
            settings.VideoFfmpegPath = settings.VideoFfmpegPath.Trim();
            return settings;
        }

        internal static string NormalizeVideoEncoder(string raw)
        {
            string value = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
            switch (value)
            {
                case "h264_nvenc":
                case "hevc_nvenc":
                case "libx264":
                    return value;
                default:
                    return "h264_nvenc";
            }
        }

        private static SubCameraPreset[] NormalizePresets(SubCameraPreset[] presets)
        {
            if (presets == null || presets.Length == 0)
                return new SubCameraPreset[0];

            for (int i = 0; i < presets.Length; i++)
            {
                SubCameraPreset preset = presets[i];
                if (preset == null)
                    continue;

                if (string.IsNullOrWhiteSpace(preset.Name))
                    preset.Name = "Preset " + (i + 1);
                preset.BoneTarget = Mathf.Clamp(preset.BoneTarget, 0, 2);
                preset.CameraPosition = RoundArray(preset.CameraPosition);
                preset.CameraRotation = RoundArray(preset.CameraRotation);
                preset.CameraOffsetLocal = RoundArray(preset.CameraOffsetLocal);
                preset.LookAtPosition = RoundArray(preset.LookAtPosition);
                preset.LookAtOffsetLocal = RoundArray(preset.LookAtOffsetLocal);
                preset.Fov = Mathf.Clamp(Round2(preset.Fov <= 0f ? 60f : preset.Fov), 10f, 170f);
                if (preset.ActorCamera != null)
                {
                    ActorCameraShot shot = preset.ActorCamera;
                    shot.BoneTarget = Mathf.Clamp(shot.BoneTarget, 0, 2);
                    shot.CameraPosition = RoundArray(shot.CameraPosition);
                    shot.CameraRotation = RoundArray(shot.CameraRotation);
                    shot.CameraOffsetLocal = RoundArray(shot.CameraOffsetLocal);
                    shot.LookAtPosition = RoundArray(shot.LookAtPosition);
                    shot.LookAtOffsetLocal = RoundArray(shot.LookAtOffsetLocal);
                    shot.Fov = Mathf.Clamp(Round2(shot.Fov <= 0f ? 60f : shot.Fov), 10f, 170f);
                }
                preset.PoseOverrides = NormalizeCameraPoseOverrides(preset.PoseOverrides);
            }

            return presets;
        }

        private static RenderResolutionPreset[] NormalizeRenderResolutionPresets(RenderResolutionPreset[] presets)
        {
            if (presets == null || presets.Length == 0)
                return new RenderResolutionPreset[0];

            List<RenderResolutionPreset> normalized = new List<RenderResolutionPreset>();
            for (int i = 0; i < presets.Length; i++)
            {
                RenderResolutionPreset preset = presets[i];
                if (preset == null)
                    continue;

                preset.Width = Mathf.Clamp(preset.Width, 64, 4096);
                preset.Height = Mathf.Clamp(preset.Height, 64, 4096);
                if (string.IsNullOrWhiteSpace(preset.Name))
                    preset.Name = preset.Width + "x" + preset.Height;
                preset.Name = preset.Name.Trim();
                normalized.Add(preset);
            }

            return normalized.ToArray();
        }

        private static PosePreset[] NormalizePosePresets(PosePreset[] presets)
        {
            if (presets == null || presets.Length == 0)
                return new PosePreset[0];

            List<PosePreset> result = new List<PosePreset>(presets.Length);
            for (int i = 0; i < presets.Length; i++)
            {
                PosePreset preset = presets[i];
                if (preset == null || string.IsNullOrWhiteSpace(preset.Key))
                    continue;

                preset.Key = preset.Key.Trim();
                if (preset.DisplayName == null)
                    preset.DisplayName = string.Empty;
                preset.CameraPosX = Round2(preset.CameraPosX);
                preset.CameraPosY = Round2(preset.CameraPosY);
                preset.CameraPosZ = Round2(preset.CameraPosZ);
                preset.CameraRotX = Round2(preset.CameraRotX);
                preset.CameraRotY = Round2(preset.CameraRotY);
                preset.CameraRotZ = Round2(preset.CameraRotZ);
                preset.CameraFov = Mathf.Clamp(Round2(preset.CameraFov <= 0f ? 60f : preset.CameraFov), 10f, 170f);
                preset.DisplayPosX = Round2(preset.DisplayPosX);
                preset.DisplayPosY = Round2(preset.DisplayPosY);
                preset.DisplayPosZ = Round2(preset.DisplayPosZ);
                preset.DisplayRotX = Round2(preset.DisplayRotX);
                preset.DisplayRotY = Round2(preset.DisplayRotY);
                preset.DisplayRotZ = Round2(preset.DisplayRotZ);
                preset.DisplayHeight = Mathf.Max(0.1f, Round2(preset.DisplayHeight <= 0f ? 1f : preset.DisplayHeight));
                result.Add(preset);
            }
            return result.ToArray();
        }

        private static DisplayPreset[] NormalizeDisplayPresets(DisplayPreset[] presets)
        {
            if (presets == null || presets.Length == 0)
                return new DisplayPreset[0];

            for (int i = 0; i < presets.Length; i++)
            {
                DisplayPreset preset = presets[i];
                if (preset == null)
                    continue;

                if (string.IsNullOrWhiteSpace(preset.Name))
                    preset.Name = "Display " + (i + 1);
                preset.Position = RoundArray(preset.Position);
                preset.Rotation = RoundArray(preset.Rotation);
                preset.Width = Mathf.Max(0.1f, Round2(preset.Width));
                preset.Height = Mathf.Max(0.1f, Round2(preset.Height));
                preset.PoseOverrides = NormalizeDisplayPoseOverrides(preset.PoseOverrides);
            }

            return presets;
        }

        private static CameraPoseOverride[] NormalizeCameraPoseOverrides(CameraPoseOverride[] overrides)
        {
            if (overrides == null || overrides.Length == 0)
                return new CameraPoseOverride[0];

            List<CameraPoseOverride> result = new List<CameraPoseOverride>(overrides.Length);
            for (int i = 0; i < overrides.Length; i++)
            {
                CameraPoseOverride entry = overrides[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                entry.Key = entry.Key.Trim();
                if (entry.DisplayName == null)
                    entry.DisplayName = string.Empty;
                entry.CameraPosition = RoundArray(entry.CameraPosition);
                entry.CameraRotation = RoundArray(entry.CameraRotation);
                entry.CameraOffsetLocal = RoundArray(entry.CameraOffsetLocal);
                entry.LookAtPosition = RoundArray(entry.LookAtPosition);
                entry.LookAtOffsetLocal = RoundArray(entry.LookAtOffsetLocal);
                entry.Fov = Mathf.Clamp(Round2(entry.Fov <= 0f ? 60f : entry.Fov), 10f, 170f);
                result.Add(entry);
            }

            return result.ToArray();
        }

        private static DisplayPoseOverride[] NormalizeDisplayPoseOverrides(DisplayPoseOverride[] overrides)
        {
            if (overrides == null || overrides.Length == 0)
                return new DisplayPoseOverride[0];

            List<DisplayPoseOverride> result = new List<DisplayPoseOverride>(overrides.Length);
            for (int i = 0; i < overrides.Length; i++)
            {
                DisplayPoseOverride entry = overrides[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                entry.Key = entry.Key.Trim();
                if (entry.DisplayName == null)
                    entry.DisplayName = string.Empty;
                entry.Position = RoundArray(entry.Position);
                entry.Rotation = RoundArray(entry.Rotation);
                entry.Width = Mathf.Max(0.1f, Round2(entry.Width <= 0f ? 1f : entry.Width));
                entry.Height = Mathf.Max(0.1f, Round2(entry.Height <= 0f ? 1f : entry.Height));
                result.Add(entry);
            }

            return result.ToArray();
        }

        private static float[] RoundArray(float[] values)
        {
            if (values == null)
                return null;

            for (int i = 0; i < values.Length; i++)
                values[i] = Round2(values[i]);
            return values;
        }

        private static float Round2(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        internal static float CalculateDisplayWidth(ProbeSettings settings)
        {
            if (settings == null)
                return 1f;

            int renderWidth = Mathf.Max(1, settings.RenderWidth);
            int renderHeight = Mathf.Max(1, settings.RenderHeight);
            float displayHeight = Mathf.Max(0.1f, settings.DisplayHeight);
            float aspectWidth = displayHeight * (renderWidth / (float)renderHeight);
            return Mathf.Max(0.1f, Round2(aspectWidth));
        }

        private static string Serialize(ProbeSettings settings)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ProbeSettings));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, settings);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static ProbeSettings Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ProbeSettings));
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as ProbeSettings;
            }
        }

        private static void ApplyDefaultDisplayAspect(ProbeSettings settings)
        {
            if (settings == null)
                return;

            int width = Mathf.Max(1, settings.RenderWidth);
            int height = Mathf.Max(1, settings.RenderHeight);
            float aspectHeight = settings.DisplayWidth * (height / (float)width);
            settings.DisplayHeight = Round2(Mathf.Max(0.1f, aspectHeight));
            settings.DisplayWidth = CalculateDisplayWidth(settings);
        }
    }
}
