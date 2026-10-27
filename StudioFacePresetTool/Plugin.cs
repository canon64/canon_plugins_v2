using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using MainGameLogRelay;
using MainGameUiInputCapture;
using StudioFacePresetTool.Services;
using StudioFacePresetTool.UI;

namespace StudioFacePresetTool
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("CharaStudio")]
    [BepInDependency(MainGameLogRelay.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MainGameUiInputCapture.Plugin.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.studio.facepresettool";
        public const string PluginName = "StudioFacePresetTool";
        public const string Version = "1.0.0";

        private const string RelayOwner = Guid;
        private const string RelayLogKey = "studio/StudioFacePresetTool";
        private const string InputOwner = Guid + ".ui";

        private ConfigEntry<bool> _cfgEnableRelayLogs;
        private ConfigEntry<bool> _cfgDetailLogs;
        private ConfigEntry<bool> _cfgUiVisible;
        private ConfigEntry<string> _cfgToggleUiKey;
        private ConfigEntry<bool> _cfgIdleCursorUnlock;

        private PluginLogger _log;
        private FacePresetWindowController _controller;

        private void Awake()
        {
            string pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;

            BindConfigEntries();
            _log = new PluginLogger(
                Logger,
                Path.Combine(pluginDir, "StudioFacePresetTool.log"),
                RelayOwner,
                RelayLogKey,
                _cfgDetailLogs.Value);
            _log.ConfigureRelay(_cfgEnableRelayLogs.Value);
            _log.Info("awake start pluginDir=" + pluginDir);

            var settingsStore = new FacePresetSettingsStore(pluginDir, _log);
            var presetStore = new FacePresetStore(pluginDir, _log);
            var runtime = new StudioFaceRuntime(_log);

            _controller = new FacePresetWindowController(_log, runtime, settingsStore, presetStore, InputOwner);
            _controller.SetUiVisible(_cfgUiVisible.Value);
            _controller.SetToggleKey(_cfgToggleUiKey.Value);
            _controller.SetIdleCursorUnlock(_cfgIdleCursorUnlock.Value);

            HookConfigEvents();
            _log.Info("awake completed");
        }

        private void Update()
        {
            _controller?.Tick();
        }

        private void OnGUI()
        {
            _controller?.OnGui();
        }

        private void OnDestroy()
        {
            if (_cfgEnableRelayLogs != null)
                _cfgEnableRelayLogs.SettingChanged -= OnRelayChanged;
            if (_cfgDetailLogs != null)
                _cfgDetailLogs.SettingChanged -= OnDetailChanged;
            if (_cfgUiVisible != null)
                _cfgUiVisible.SettingChanged -= OnUiVisibleChanged;
            if (_cfgToggleUiKey != null)
                _cfgToggleUiKey.SettingChanged -= OnToggleKeyChanged;
            if (_cfgIdleCursorUnlock != null)
                _cfgIdleCursorUnlock.SettingChanged -= OnIdleCursorUnlockChanged;

            _controller?.Shutdown();
            _controller = null;
            _log?.Info("destroy completed");
        }

        private void BindConfigEntries()
        {
            _cfgEnableRelayLogs = Config.Bind(
                "Logging",
                "EnableRelayLogs",
                false,
                "MainGameLogRelay経由ログを有効化する");

            _cfgDetailLogs = Config.Bind(
                "Logging",
                "DetailLogs",
                false,
                "詳細ログ（分岐ログ）を有効化する");

            _cfgUiVisible = Config.Bind(
                "UI",
                "Visible",
                false,
                "UI表示の有効/無効");

            _cfgToggleUiKey = Config.Bind(
                "UI",
                "ToggleKey",
                "F9",
                "UI表示切替キー");

            _cfgIdleCursorUnlock = Config.Bind(
                "UI",
                "IdleCursorUnlock",
                true,
                "UI表示中にカーソル解放を維持する");
        }

        private void HookConfigEvents()
        {
            _cfgEnableRelayLogs.SettingChanged += OnRelayChanged;
            _cfgDetailLogs.SettingChanged += OnDetailChanged;
            _cfgUiVisible.SettingChanged += OnUiVisibleChanged;
            _cfgToggleUiKey.SettingChanged += OnToggleKeyChanged;
            _cfgIdleCursorUnlock.SettingChanged += OnIdleCursorUnlockChanged;
        }

        private void OnRelayChanged(object sender, EventArgs e)
        {
            _log?.ConfigureRelay(_cfgEnableRelayLogs.Value);
        }

        private void OnDetailChanged(object sender, EventArgs e)
        {
            if (_log != null)
                _log.DetailEnabled = _cfgDetailLogs.Value;
        }

        private void OnUiVisibleChanged(object sender, EventArgs e)
        {
            _controller?.SetUiVisible(_cfgUiVisible.Value);
        }

        private void OnToggleKeyChanged(object sender, EventArgs e)
        {
            _controller?.SetToggleKey(_cfgToggleUiKey.Value);
        }

        private void OnIdleCursorUnlockChanged(object sender, EventArgs e)
        {
            _controller?.SetIdleCursorUnlock(_cfgIdleCursorUnlock.Value);
        }
    }
}
