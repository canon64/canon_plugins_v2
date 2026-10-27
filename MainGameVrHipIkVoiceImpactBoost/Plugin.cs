using BepInEx;
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using VRGIN.Core;
using CanonPlugins.Shared;

namespace MainGameVrHipIkVoiceImpactBoost
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInDependency("com.kks.maingame.voicefaceeventbridge", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.kks.main.girlbodyikgizmo", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kks.main.vrhipikvoiceimpactboost";
        public const string PluginName = "MainGameVrHipIkVoiceImpactBoost";
        public const string Version = "0.3.0";

        private string _pluginDir;
        private PluginFileLogger _logger;
        private PluginSettings _settings;
        private HipIkMotionDetector _detector;
        private VoiceImpactBoostService _boostService;

        private void Awake()
        {
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? Directory.GetCurrentDirectory();
            _logger = new PluginFileLogger(base.Logger, _pluginDir, PluginName + ".log");

            _settings = new PluginSettings(Config);

            // 起動時: JSON が存在すればその値を cfg(ConfigEntry) に流し込む。
            //         JSON 未存在なら ConfigEntry のデフォルト値で JSON を新規作成。
            SettingsJsonDto loaded = SettingsStore.LoadOrNull(_pluginDir, LogWarn, LogError);
            if (loaded != null)
            {
                _settings.ApplyJson(loaded);
            }
            else
            {
                SettingsStore.Save(_pluginDir, _settings.ToJson(), LogError);
                _logger.LogInfo("settings json created: " + SettingsStore.GetPath(_pluginDir));
            }

            // 以降の cfg 変更(ConfigurationManager 等)は即 JSON に反映。
            _settings.Changed += OnSettingsChanged;

            _boostService = new VoiceImpactBoostService(_settings, _logger);
            _detector = new HipIkMotionDetector(
                _settings,
                _logger,
                new HipIkTrackingPositionSource(_logger),
                _boostService.TryRequestBoost);

            _logger.LogInfo(
                "started v" + Version
                + " process=" + Process.GetCurrentProcess().ProcessName
                + " json=" + SettingsStore.GetPath(_pluginDir));
        }

        private void OnDestroy()
        {
            MainGameVoiceFaceEventBridge.PublicApi.SetImpactCueEnabled(GUID, false);
            if (_settings != null)
            {
                _settings.Changed -= OnSettingsChanged;
            }
        }

        private void Update()
        {
            UpdateCueMode();
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom || _settings == null || !_settings.Enabled)
            {
                _detector?.Reset("disabled_or_scene_exit");
                return;
            }

            if (_settings.RequireVrActive && (!VR.Active || VR.Mode == null))
            {
                _detector.Reset("vr_inactive");
                return;
            }

            _detector.Update(Time.unscaledDeltaTime);
        }

        private void OnSettingsChanged()
        {
            UpdateCueMode();
            try
            {
                SettingsStore.Save(_pluginDir, _settings.ToJson(), LogError);
                if (_settings.VerboseLog)
                {
                    _logger.LogInfo("settings json synced from cfg");
                }
            }
            catch (Exception ex)
            {
                LogError("settings json sync failed: " + ex.Message);
            }
        }

        private void LogWarn(string message)
        {
            _logger?.LogWarning(message);
        }

        private void UpdateCueMode()
        {
            MainGameVoiceFaceEventBridge.PublicApi.SetImpactCueEnabled(GUID,
                _settings != null && _settings.Enabled && _settings.MoanModeEnabled && BlankRoomGate.IsInBlankRoom
                && (!_settings.RequireVrActive || (VR.Active && VR.Mode != null)));
        }

        private void LogError(string message)
        {
            _logger?.LogError(message);
        }
    }
}
