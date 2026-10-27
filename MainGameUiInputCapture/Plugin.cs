using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MainGameLogRelay;
using UnityEngine;

namespace MainGameUiInputCapture
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInProcess("CharaStudio")]
    [BepInDependency(MainGameLogRelay.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string Guid = "com.kks.maingame.uiinputcapture";
        public const string PluginName = "MainGameUiInputCapture";
        public const string Version = "1.0.0";
        private const string RelayOwner = Guid;
        private const string RelayLogKey = "main/" + PluginName;

        internal static Plugin Instance { get; private set; }

        private string _pluginDir;
        private readonly SimpleFileLogger _file = new SimpleFileLogger();
        private ConfigEntry<bool> _cfgEnableLogs;
        private ConfigEntry<BepInEx.Configuration.KeyboardShortcut> _cfgPanicKey;
        private ConfigEntry<bool> _cfgBlockDiagnostics;
        private ConfigEntry<bool> _cfgAutoUnstick;
        private UiInputCaptureSettings _settings;
        private UiInputCaptureRuntime _runtime;
        private Harmony _harmony;
        private readonly object _debugOwnersSync = new object();
        private readonly HashSet<string> _debugEnabledOwners = new HashSet<string>(System.StringComparer.Ordinal);

        private void Awake()
        {
            Instance = this;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            InputBindingsApi.Initialize(_pluginDir);
            // 診断は必ずファイルへ落とす。
            // BepInEx の Logger は output_log.txt を書かない設定だと消えてしまい、
            // 「ログを見ろ」と言った先に何も無い状態になる。
            _file.Initialize(Path.Combine(_pluginDir, "MainGameUiInputCapture.log"));
            _cfgEnableLogs = Config.Bind(
                "Logging",
                "EnableLogs",
                false,
                "MainGameLogRelay経由ログのON/OFF");
            _cfgPanicKey = Config.Bind(
                "Input",
                "PanicReleaseKey",
                new BepInEx.Configuration.KeyboardShortcut(KeyCode.F11, KeyCode.LeftControl),
                "掴みっぱなしを全部強制解放してカメラ操作を取り戻す緊急キー。");
            _cfgBlockDiagnostics = Config.Bind(
                "Logging",
                "CameraBlockDiagnostics",
                false,
                "カメラ操作が誰に止められているかを output_log.txt へ出す（状態が変わった時だけ1行）。");
            _cfgAutoUnstick = Config.Bind(
                "Camera",
                "AutoUnstickCamera",
                true,
                "誰も掴んでいないのにカメラ操作が禁止されたままなら、数秒後に自動で解除する。");
            _cfgEnableLogs.SettingChanged += (_, __) => ApplyRelayLoggingState();
            ApplyRelayLoggingState();

            _settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
            _runtime = new UiInputCaptureRuntime(this);
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(UiInputCaptureHSceneWheelPatches));
            _harmony.PatchAll(typeof(UiInputCaptureCursorPatches));

            LogInfo("loaded");
            LogInfo("settings=" + Path.Combine(_pluginDir, SettingsStore.FileName));
        }

        private void Update()
        {
            InputBindingsApi.TickEditor();
            if (_cfgPanicKey != null && InputBindingsApi.Down("MainGameUiInputCapture.panic", _cfgPanicKey.Value.ToString()))
            {
                LogAlways("PANIC RELEASE requested by hotkey");
                _runtime?.ReleaseAll("panic-hotkey");
                _runtime?.ForceCameraControlBack("panic-hotkey");
            }

            _runtime?.FrameUpdate();
        }

        private void LateUpdate()
        {
            _runtime?.FrameUpdate();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { InputBindingsApi.CancelEditor(); _runtime?.ReleaseAll("focus-lost"); }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) { InputBindingsApi.CancelEditor(); _runtime?.ReleaseAll("application-paused"); }
        }

        private void OnDisable() { InputBindingsApi.CancelEditor(); _runtime?.ReleaseAll("plugin-disabled"); }

        private void OnDestroy()
        {
            InputBindingsApi.CancelEditor();
            _harmony?.UnpatchSelf();
            _harmony = null;
            _runtime?.ReleaseAll("plugin destroy");
            SaveSettings();
            LogInfo("destroyed");
            _runtime = null;
            Instance = null;
        }

        private void SaveSettings()
        {
            SettingsStore.Save(_pluginDir, _settings, LogWarn);
        }

        private void ApplyRelayLoggingState()
        {
            if (!LogRelayApi.IsAvailable)
                return;

            bool enabled = _cfgEnableLogs != null && _cfgEnableLogs.Value;
            LogRelayApi.SetOwnerLogKey(RelayOwner, RelayLogKey);
            LogRelayApi.SetOwnerEnabled(RelayOwner, enabled);
        }

        internal UiInputCaptureRuntime Runtime => _runtime;
        internal bool DetailLogEnabled => _settings != null && _settings.DetailLogEnabled;
        internal bool LogStateOnTransition => _settings != null && _settings.LogStateOnTransition;

        internal void SetOwnerDebug(string ownerKey, bool enabled)
        {
            if (!TryNormalizeKey(ownerKey, out string owner))
                return;

            lock (_debugOwnersSync)
            {
                if (enabled)
                    _debugEnabledOwners.Add(owner);
                else
                    _debugEnabledOwners.Remove(owner);
            }
        }

        internal bool IsOwnerDebugEnabled(string ownerKey)
        {
            if (!TryNormalizeKey(ownerKey, out string owner))
                return false;

            lock (_debugOwnersSync)
            {
                return _debugEnabledOwners.Contains(owner);
            }
        }

        internal void LogDebugForOwner(string ownerKey, string message)
        {
            if (!IsOwnerDebugEnabled(ownerKey))
                return;

            LogDebugRaw(message);
        }

        internal void LogDebugRaw(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (LogRelayApi.IsAvailable)
            {
                LogRelayApi.Debug(RelayOwner, message);
                return;
            }

            Logger.LogInfo("[" + PluginName + "] " + message);
        }

        internal void LogInfo(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (LogRelayApi.IsAvailable)
            {
                LogRelayApi.Info(RelayOwner, message);
                return;
            }

            Logger.LogInfo("[" + PluginName + "] " + message);
        }

        /// <summary>
        /// 中継ログのON/OFFに関係なく必ず output_log.txt に出す。
        /// 「掴みが取り残された」「カメラが止められている」等、
        /// 出ないと原因が追えなくなる事実だけに使う。
        /// </summary>
        internal void LogAlways(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            _file.Write("INFO", message);
            Logger.LogWarning("[" + PluginName + "] " + message);
        }

        internal bool BlockDiagnosticsEnabled => _cfgBlockDiagnostics != null && _cfgBlockDiagnostics.Value;

        internal bool AutoUnstickEnabled => _cfgAutoUnstick == null || _cfgAutoUnstick.Value;

        internal void LogWarn(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (LogRelayApi.IsAvailable)
            {
                LogRelayApi.Warn(RelayOwner, message);
                return;
            }

            Logger.LogWarning("[" + PluginName + "] " + message);
        }

        internal void LogError(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (LogRelayApi.IsAvailable)
            {
                LogRelayApi.Error(RelayOwner, message);
                return;
            }

            Logger.LogError("[" + PluginName + "] " + message);
        }

        internal void LogDebug(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (!DetailLogEnabled)
                return;
            LogDebugRaw(message);
        }

        private static bool TryNormalizeKey(string raw, out string normalized)
        {
            normalized = raw == null ? string.Empty : raw.Trim();
            return normalized.Length > 0;
        }
    }
}
