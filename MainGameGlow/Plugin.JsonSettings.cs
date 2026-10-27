using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameGlow
{
    public sealed partial class Plugin
    {
        [DataContract]
        internal sealed class GlowSettings
        {
            [DataMember(Name = "Enabled")] public bool Enabled = true;
            [DataMember(Name = "VerboseLog")] public bool VerboseLog = false;

            [DataMember(Name = "UseScreenSize")] public bool UseScreenSize = true;
            [DataMember(Name = "CaptureWidth")] public int CaptureWidth = 1280;
            [DataMember(Name = "CaptureHeight")] public int CaptureHeight = 720;
            [DataMember(Name = "CharaLayer")] public string CharaLayer = "Chara";

            [DataMember(Name = "GlowThreshold")] public float GlowThreshold = 0.5f;
            [DataMember(Name = "GlowStrength")] public float GlowStrength = 3f;
            [DataMember(Name = "GlowBlurPercent")] public float GlowBlurPercent = 30f;

            [DataMember(Name = "TintR")] public float TintR = 0.5f;
            [DataMember(Name = "TintG")] public float TintG = 0.5f;
            [DataMember(Name = "TintB")] public float TintB = 0.5f;
            [DataMember(Name = "TintA")] public float TintA = 1f;
            [DataMember(Name = "OverlayAlpha")] public float OverlayAlpha = 1f;

            [DataMember(Name = "PreferCameraMain")] public bool PreferCameraMain = true;
            [DataMember(Name = "CameraNameFilter")] public string CameraNameFilter = "";
            [DataMember(Name = "CameraFallbackIndex")] public int CameraFallbackIndex = 0;
        }

        private static readonly UTF8Encoding JsonUtf8NoBom = new UTF8Encoding(false);

        private string _settingsJsonPath;
        private bool _suppressJsonWrite;

        private void LoadOrCreateSettingsJson()
        {
            string pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            _settingsJsonPath = Path.Combine(pluginDir, "MainGameGlowSettings.json");

            try
            {
                if (File.Exists(_settingsJsonPath))
                {
                    GlowSettings loaded = LoadSettingsJsonFromFile(_settingsJsonPath);
                    if (loaded != null)
                    {
                        ApplySettingsToConfig(loaded);
                        LogPlugin("INFO", "settings.json loaded: " + _settingsJsonPath);
                        return;
                    }
                    LogPlugin("WARN", "settings.json read failed (null), regenerating from cfg");
                }
                else
                {
                    LogPlugin("INFO", "settings.json not found, creating from cfg");
                }

                SaveSettingsJsonFromConfig();
            }
            catch (Exception ex)
            {
                LogPlugin("WARN", "settings.json load/create failed: " + ex.Message);
            }
        }

        private GlowSettings LoadSettingsJsonFromFile(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GlowSettings));
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as GlowSettings;
            }
        }

        private void SaveSettingsJsonFromConfig()
        {
            if (string.IsNullOrEmpty(_settingsJsonPath))
                return;

            try
            {
                GlowSettings snapshot = BuildSettingsFromConfig();
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GlowSettings));
                using (MemoryStream stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, snapshot);
                    File.WriteAllText(_settingsJsonPath, Encoding.UTF8.GetString(stream.ToArray()), JsonUtf8NoBom);
                }
            }
            catch (Exception ex)
            {
                LogPlugin("WARN", "settings.json save failed: " + ex.Message);
            }
        }

        private GlowSettings BuildSettingsFromConfig()
        {
            return new GlowSettings
            {
                Enabled = _cfgEnabled.Value,
                VerboseLog = _cfgVerboseLog.Value,
                UseScreenSize = _cfgUseScreenSize.Value,
                CaptureWidth = _cfgCaptureWidth.Value,
                CaptureHeight = _cfgCaptureHeight.Value,
                CharaLayer = _cfgCharaLayer.Value ?? "Chara",
                GlowThreshold = _cfgGlowThreshold.Value,
                GlowStrength = _cfgGlowStrength.Value,
                GlowBlurPercent = _cfgGlowBlurPercent.Value,
                TintR = _cfgTintR.Value,
                TintG = _cfgTintG.Value,
                TintB = _cfgTintB.Value,
                TintA = _cfgTintA.Value,
                OverlayAlpha = _cfgOverlayAlpha.Value,
                PreferCameraMain = _cfgPreferCameraMain.Value,
                CameraNameFilter = _cfgCameraNameFilter.Value ?? "",
                CameraFallbackIndex = _cfgCameraFallbackIndex.Value
            };
        }

        private void ApplySettingsToConfig(GlowSettings s)
        {
            _suppressJsonWrite = true;
            try
            {
                _cfgEnabled.Value = s.Enabled;
                _cfgVerboseLog.Value = s.VerboseLog;
                _cfgUseScreenSize.Value = s.UseScreenSize;
                _cfgCaptureWidth.Value = s.CaptureWidth;
                _cfgCaptureHeight.Value = s.CaptureHeight;
                _cfgCharaLayer.Value = string.IsNullOrEmpty(s.CharaLayer) ? "Chara" : s.CharaLayer;
                _cfgGlowThreshold.Value = s.GlowThreshold;
                _cfgGlowStrength.Value = s.GlowStrength;
                _cfgGlowBlurPercent.Value = s.GlowBlurPercent;
                _cfgTintR.Value = s.TintR;
                _cfgTintG.Value = s.TintG;
                _cfgTintB.Value = s.TintB;
                _cfgTintA.Value = s.TintA;
                _cfgOverlayAlpha.Value = s.OverlayAlpha;
                _cfgPreferCameraMain.Value = s.PreferCameraMain;
                _cfgCameraNameFilter.Value = s.CameraNameFilter ?? "";
                _cfgCameraFallbackIndex.Value = s.CameraFallbackIndex;
            }
            finally
            {
                _suppressJsonWrite = false;
            }
        }
    }
}
