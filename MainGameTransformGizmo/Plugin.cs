using System;
using BepInEx;
using BepInEx.Configuration;
using MainGameLogRelay;
using MainGameUiInputCapture;
using CanonPlugins.Shared;

namespace MainGameTransformGizmo
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency(MainGameLogRelay.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MainGameUiInputCapture.Plugin.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kks.maingame.transformgizmo";
        public const string PluginName = "MainGameTransformGizmo";
        public const string Version = "0.1.0";
        private const string RelayOwner = GUID;
        private const string RelayLogKey = "main/" + PluginName;
        private ConfigEntry<bool> _cfgEnableLogs;

        public static Plugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            _cfgEnableLogs = Config.Bind(
                "Logging",
                "EnableLogs",
                false,
                "MainGameLogRelay経由ログのON/OFF");
            _cfgEnableLogs.SettingChanged += (_, __) => ApplyRelayLoggingState();
            ApplyRelayLoggingState();

            WriteLog("INFO", $"=== {PluginName} {Version} start ===");
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            GizmoInputCaptureRuntime.Tick();
        }

        private void OnDestroy()
        {
            GizmoInputCaptureRuntime.Shutdown();
            WriteLog("INFO", "plugin destroy");
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        internal void LogInfo(string message)
        {
            WriteLog("INFO", message);
        }

        private void ApplyRelayLoggingState()
        {
            GizmoLog.Enabled = _cfgEnableLogs != null && _cfgEnableLogs.Value;
            if (!LogRelayApi.IsAvailable)
                return;

            bool enabled = _cfgEnableLogs != null && _cfgEnableLogs.Value;
            LogRelayApi.SetOwnerLogKey(RelayOwner, RelayLogKey);
            LogRelayApi.SetOwnerEnabled(RelayOwner, enabled);
        }

        private void WriteLog(string level, string message)
        {
            if (LogRelayApi.IsAvailable)
            {
                switch (level)
                {
                    case "ERROR":
                        LogRelayApi.Error(RelayOwner, message);
                        return;
                    case "WARN":
                        LogRelayApi.Warn(RelayOwner, message);
                        return;
                    case "DEBUG":
                        LogRelayApi.Debug(RelayOwner, message);
                        return;
                    default:
                        LogRelayApi.Info(RelayOwner, message);
                        return;
                }
            }

            switch (level)
            {
                case "ERROR":
                    Logger.LogError("[" + PluginName + "] " + message);
                    return;
                case "WARN":
                    Logger.LogWarning("[" + PluginName + "] " + message);
                    return;
                default:
                    Logger.LogInfo("[" + PluginName + "] " + message);
                    return;
            }
        }
    }
}
