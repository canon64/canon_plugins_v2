using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGamePortraitCapture
{
    [Serializable]
    internal sealed class PluginSettings
    {
        public int Version = 4;
        public bool Enabled = true;
        public bool DevelopmentHotkeyEnabled = true;
        public string CaptureKey = "None";
        public int TargetFemaleIndex = 0;
        public bool LookAtCameraDuringCapture = true;
        public int EyeLookPatternDuringCapture = 1;
        public bool TurnHeadToCameraDuringCapture = true;
        public int NeckLookPatternDuringCapture = 1;
        public bool PreserveHeadTiltDuringCapture = true;
        public float HeadLookMinPitch = -5f;
        public float HeadLookMaxPitch = 5f;
        public float HeadLookMinYaw = -10f;
        public float HeadLookMaxYaw = 10f;
        public float HeadLookMinRoll = -6f;
        public float HeadLookMaxRoll = 6f;
        public bool HeadLookFollowPitchEnabled = true;
        public bool HeadLookFollowYawEnabled = true;
        public bool HeadLookFollowRollEnabled = true;
        public bool HeadLookPitchDeadZoneEnabled = false;
        public float HeadLookPitchDeadZoneMin = -1f;
        public float HeadLookPitchDeadZoneMax = 1f;
        public bool HeadLookYawDeadZoneEnabled = false;
        public float HeadLookYawDeadZoneMin = -3f;
        public float HeadLookYawDeadZoneMax = 3f;
        public bool HeadLookRollDeadZoneEnabled = false;
        public float HeadLookRollDeadZoneMin = -2f;
        public float HeadLookRollDeadZoneMax = 2f;
        public bool AutoRotateLyingPose = true;
        public float MoveDuration = 0.6f;
        public float HoldBeforeCapture = 2f;
        public float ReturnDuration = 0.6f;
        // 撮影後に元のカメラ位置へ戻すか。false なら撮影した構図のまま留まる。
        public bool ReturnCameraAfterCapture = true;
        // 撮影画角を毎フレーム強制的に維持する。既定OFF。
        // FOVを毎フレーム書いている相手には正当な理由がある場合があり、
        // 素性が分からないうちに踏み潰さないため。
        public bool ForceKeepCaptureFov = false;
        public int CaptureWidth = 1080;
        public int CaptureHeight = 1920;
        public string OutputDirectory = "captures";
        public float MinYaw = -45f;
        public float MaxYaw = 45f;
        public float HeadDirectionPriority = 0.7f;
        public float HipDirectionPriority = 0.3f;
        public float DirectionPriorityRandomness = 0.2f;
        public float MinPitch = -30f;
        public float MaxPitch = 30f;
        public float MinRoll = -15f;
        public float MaxRoll = 15f;
        public float MinFov = 30f;
        public float MaxFov = 42f;
        public float MinDistance = 0.8f;
        public float MaxDistance = 3f;
        public float ViewportMargin = 0.08f;
        public float HeadUpperMargin = 0.3f;
        public float CrotchLowerMargin = 0.15f;
        public int RandomAttempts = 10;
        public bool DetailLogEnabled = false;
    }

    internal static class SettingsStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static PluginSettings LoadOrCreate(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    PluginSettings loaded = JsonUtility.FromJson<PluginSettings>(File.ReadAllText(path, Encoding.UTF8));
                    if (loaded != null)
                    {
                        if (loaded.Version < 2)
                        {
                            loaded.HeadLookMinPitch = -5f;
                            loaded.HeadLookMaxPitch = 5f;
                            loaded.HeadLookMinYaw = -10f;
                            loaded.HeadLookMaxYaw = 10f;
                            loaded.HeadLookMinRoll = -6f;
                            loaded.HeadLookMaxRoll = 6f;
                            loaded.Version = 2;
                        }
                        if (loaded.Version < 3)
                        {
                            loaded.HeadLookPitchDeadZoneEnabled = false;
                            loaded.HeadLookPitchDeadZoneMin = -1f;
                            loaded.HeadLookPitchDeadZoneMax = 1f;
                            loaded.HeadLookYawDeadZoneEnabled = false;
                            loaded.HeadLookYawDeadZoneMin = -3f;
                            loaded.HeadLookYawDeadZoneMax = 3f;
                            loaded.HeadLookRollDeadZoneEnabled = false;
                            loaded.HeadLookRollDeadZoneMin = -2f;
                            loaded.HeadLookRollDeadZoneMax = 2f;
                            loaded.Version = 3;
                        }
                        if (loaded.Version < 4)
                        {
                            loaded.HeadLookFollowPitchEnabled = true;
                            loaded.HeadLookFollowYawEnabled = true;
                            loaded.HeadLookFollowRollEnabled = true;
                            loaded.Version = 4;
                        }
                        return loaded;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogWarning("settings load failed; defaults kept: " + ex);
                }
            }
            PluginSettings settings = new PluginSettings();
            Save(path, settings);
            return settings;
        }

        internal static void Save(string path, PluginSettings settings)
        {
            string temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temp, JsonUtility.ToJson(settings, true), Utf8NoBom);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
    }
}
