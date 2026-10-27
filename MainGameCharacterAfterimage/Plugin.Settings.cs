using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGameCharacterAfterimage
{
    public sealed partial class Plugin
    {
        [Serializable]
        internal sealed class PluginSettings
        {
            public int SettingsVersion = 4;
            public string UiLanguage = "ja";
            public bool Enabled = true;
            public bool VerboseLog = false;

            public int FadeFrames = 10;
            public int MaxAfterimageSlots = 10;
            public int CaptureIntervalFrames = 1;

            public bool UseScreenSize = true;
            public int CaptureWidth = 0;
            public int CaptureHeight = 0;

            public string[] CharacterLayerNames = new[] { "Chara" };

            public int MiddleCameraDepthOffsetMilli = 50;
            public int TopCharacterCameraDepthOffsetMilli = 100;

            public string OverlayShaderName = "Unlit/Transparent";
            public float OverlayTintR = 1f;
            public float OverlayTintG = 1f;
            public float OverlayTintB = 1f;
            public float OverlayTintA = 1f;
            public float AfterimageAlphaScale = 0.07f;
            public bool OverlayInFrontOfCharacter = false;
            public float FrontOverlayTargetTotalAlpha = 0.22f;
            public bool DrawNewestLast = true;

            public bool PreferCameraMain = true;
            public string SourceCameraNameContains = "";
            public int SourceCameraFallbackIndex = 0;

            public float StatusLogIntervalSec = 0f;
        }

        private const string SettingsFileName = "MainGameCharacterAfterimageSettings.json";

        private PluginSettings _settings = new PluginSettings();
        private string _settingsPath;

        private void LoadSettings()
        {
            if (!File.Exists(_settingsPath))
            {
                _settings = new PluginSettings();
                SaveSettings(createBackup: false);
                LogInfo("settings created: " + _settingsPath);
                return;
            }

            try
            {
                string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                PluginSettings loaded = JsonUtility.FromJson<PluginSettings>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                }
                ClampSettings();
                LogInfo("settings loaded: " + _settingsPath);
            }
            catch (Exception ex)
            {
                LogWarn("settings load failed: " + ex.Message);
                _settings = new PluginSettings();
                ClampSettings();
            }
        }

        private void SaveSettings(bool createBackup)
        {
            try
            {
                ClampSettings();
                if (createBackup && File.Exists(_settingsPath))
                {
                    string backupPath = _settingsPath + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
                    File.Copy(_settingsPath, backupPath, overwrite: false);
                    LogInfo("settings backup created: " + backupPath);
                }

                string json = JsonUtility.ToJson(_settings, true);
                File.WriteAllText(_settingsPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogWarn("settings save failed: " + ex.Message);
            }
        }

        private void ClampSettings()
        {
            if (_settings == null)
            {
                _settings = new PluginSettings();
            }

            _settings.FadeFrames = Mathf.Clamp(_settings.FadeFrames, 1, 300);
            _settings.MaxAfterimageSlots = Mathf.Clamp(_settings.MaxAfterimageSlots, 1, 300);
            _settings.CaptureIntervalFrames = Mathf.Clamp(_settings.CaptureIntervalFrames, 1, 60);
            _settings.CaptureWidth = Mathf.Clamp(_settings.CaptureWidth, 0, 8192);
            _settings.CaptureHeight = Mathf.Clamp(_settings.CaptureHeight, 0, 8192);
            _settings.MiddleCameraDepthOffsetMilli = Mathf.Clamp(_settings.MiddleCameraDepthOffsetMilli, 1, 1000);
            _settings.TopCharacterCameraDepthOffsetMilli = Mathf.Clamp(_settings.TopCharacterCameraDepthOffsetMilli, 2, 2000);
            if (_settings.TopCharacterCameraDepthOffsetMilli <= _settings.MiddleCameraDepthOffsetMilli)
            {
                _settings.TopCharacterCameraDepthOffsetMilli = _settings.MiddleCameraDepthOffsetMilli + 1;
            }
            _settings.OverlayTintR = Mathf.Clamp01(_settings.OverlayTintR);
            _settings.OverlayTintG = Mathf.Clamp01(_settings.OverlayTintG);
            _settings.OverlayTintB = Mathf.Clamp01(_settings.OverlayTintB);
            _settings.OverlayTintA = Mathf.Clamp01(_settings.OverlayTintA);
            _settings.AfterimageAlphaScale = Mathf.Clamp01(_settings.AfterimageAlphaScale);
            _settings.FrontOverlayTargetTotalAlpha = Mathf.Clamp(_settings.FrontOverlayTargetTotalAlpha, 0.01f, 1f);
            _settings.SourceCameraFallbackIndex = Mathf.Clamp(_settings.SourceCameraFallbackIndex, 0, 64);
            _settings.StatusLogIntervalSec = Mathf.Clamp(_settings.StatusLogIntervalSec, 0f, 3600f);

            if (_settings.CharacterLayerNames == null || _settings.CharacterLayerNames.Length == 0)
            {
                _settings.CharacterLayerNames = new[] { "Chara" };
            }
            if (string.IsNullOrEmpty(_settings.OverlayShaderName))
            {
                _settings.OverlayShaderName = "Unlit/Transparent";
            }
            if (_settings.SourceCameraNameContains == null)
            {
                _settings.SourceCameraNameContains = string.Empty;
            }
            _settings.UiLanguage = "ja";
        }
    }
}
