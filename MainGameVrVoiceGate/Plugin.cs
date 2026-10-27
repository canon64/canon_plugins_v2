using Bindings = CanonPlugins.Shared.PluginInput;
using BepInEx;
using BepInEx.Logging;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;
using CanonPlugins.Shared;

namespace MainGameVrVoiceGate
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string GUID = "com.kks.maingame.vrvoicegate";
        public const string PluginName = "MainGameVrVoiceGate";
        public const string Version = "0.1.0";

        internal static new ManualLogSource Logger { get; private set; }
        internal static PluginSettings Settings { get; private set; }

        private static readonly object FileLogLock = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private string _pluginDir;
        private string _logFilePath;
        private bool _lastGateActive;
        private float _nextHeartbeatTime;
        private GUIStyle _overlayLabelStyle;
        private GUIStyle _overlayBoxStyle;
        private DateTime _nextSendErrorLogAt = DateTime.MinValue;
        private DateTime _nextInputErrorLogAt = DateTime.MinValue;
        private EVRButtonId _recordButton = EVRButtonId.k_EButton_Axis0;

        private void Awake()
        {
            Logger = base.Logger;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            Directory.CreateDirectory(_pluginDir);
            _logFilePath = Path.Combine(_pluginDir, PluginName + ".log");
            if (_runtimeLoggingEnabled) File.WriteAllText(
                _logFilePath,
                $"[{DateTime.Now:HH:mm:ss}] === {PluginName} {Version} started ==={Environment.NewLine}",
                Utf8NoBom);

            ReloadSettings();
            LogInfo("loaded");
        }

        private void OnDisable()
        {
            SendStopIfActive("plugin_disable");
        }

        private void OnDestroy()
        {
            SendStopIfActive("plugin_destroy");
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (Settings != null && Settings.EnableCtrlRReload && IsCtrlRDown())
            {
                ReloadSettings();
                LogInfo("settings reloaded by Ctrl+R");
            }

            bool pressed = Settings != null && Settings.Enabled && IsRecordButtonPressed();
            if (pressed != _lastGateActive)
            {
                _lastGateActive = pressed;
                SendGateCommand(pressed, pressed ? "press" : "release", forceLog: true);
                _nextHeartbeatTime = Time.unscaledTime + GetHeartbeatSeconds();
                return;
            }

            if (_lastGateActive && Time.unscaledTime >= _nextHeartbeatTime)
            {
                SendGateCommand(true, "heartbeat", forceLog: false);
                _nextHeartbeatTime = Time.unscaledTime + GetHeartbeatSeconds();
            }
        }

        private void OnGUI()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (!_lastGateActive || Settings == null || !Settings.OverlayEnabled)
                return;

            EnsureOverlayStyles();

            float width = Settings.OverlayWidth;
            float height = Settings.OverlayHeight;
            float x = Settings.OverlayX >= 0f ? Settings.OverlayX : (Screen.width - width) * 0.5f;
            float y = Settings.OverlayY;
            var rect = new Rect(x, y, width, height);

            Color oldColor = GUI.color;
            GUI.color = new Color(0.88f, 0.02f, 0.02f, Settings.OverlayAlpha);
            GUI.Box(rect, GUIContent.none, _overlayBoxStyle);
            GUI.color = oldColor;

            GUI.Label(rect, Settings.OverlayText, _overlayLabelStyle);
        }

        private void ReloadSettings()
        {
            Settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
            _recordButton = ParseButton(Settings.RecordButton, EVRButtonId.k_EButton_Axis0);
            _overlayLabelStyle = null;
            _overlayBoxStyle = null;
            LogInfo(
                "settings loaded: enabled=" + Settings.Enabled
                + " udp=" + Settings.UdpHost + ":" + Settings.UdpPort
                + " heartbeat=" + Settings.HeartbeatSeconds.ToString("0.00") + "s"
                + " recordButton=" + _recordButton);

            if (!Settings.Enabled)
                SendStopIfActive("settings_disabled");
        }

        private bool IsRecordButtonPressed()
        {
            try
            {
                if (!VR.Active || VR.Mode == null || VR.Mode.Right == null)
                    return false;

                Controller right = VR.Mode.Right;
                if (right.Input == null)
                    return false;

                return Bindings.VrHeld("MainGameVrVoiceGate.record", right.Input, _recordButton.ToString());
            }
            catch (Exception ex)
            {
                LogInputError("[input] record button read failed: " + ex.Message);
                return false;
            }
        }

        private static EVRButtonId ParseButton(string raw, EVRButtonId fallback)
        {
            string value = raw == null ? string.Empty : raw.Trim();
            if (value.Length == 0)
                return fallback;

            if (Enum.TryParse(value, true, out EVRButtonId parsed))
                return parsed;

            switch (value.ToLowerInvariant())
            {
                case "b":
                case "rightb":
                case "axis0":
                case "trackpad":
                case "thumbstick":
                    return EVRButtonId.k_EButton_Axis0;
                case "trigger":
                case "axis1":
                    return EVRButtonId.k_EButton_Axis1;
                case "grip":
                    return EVRButtonId.k_EButton_Grip;
                case "menu":
                    return EVRButtonId.k_EButton_ApplicationMenu;
                default:
                    return fallback;
            }
        }

        private void SendStopIfActive(string reason)
        {
            if (!_lastGateActive)
                return;

            _lastGateActive = false;
            SendGateCommand(false, reason, forceLog: true);
        }

        private void SendGateCommand(bool active, string reason, bool forceLog)
        {
            if (Settings == null)
                return;

            string cmd = active ? "start" : "stop";
            string host = Settings.UdpHost;
            int port = Settings.UdpPort;
            string payload = BuildPayload(cmd, reason, Settings.AuthToken);
            byte[] bytes = Encoding.UTF8.GetBytes(payload);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var udp = new UdpClient())
                    {
                        udp.Send(bytes, bytes.Length, host, port);
                    }
                }
                catch (Exception ex)
                {
                    LogSendError("[udp] send failed: " + ex.Message);
                }
            });

            if (forceLog || Settings.VerboseLog)
                LogInfo("[gate] " + cmd + " reason=" + reason + " target=" + host + ":" + port);
        }

        private static string BuildPayload(string cmd, string reason, string token)
        {
            return "{"
                + "\"cmd\":\"" + JsonEscape(cmd) + "\","
                + "\"token\":\"" + JsonEscape(token ?? string.Empty) + "\","
                + "\"source\":\"" + PluginName + "\","
                + "\"reason\":\"" + JsonEscape(reason ?? string.Empty) + "\""
                + "}";
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        private float GetHeartbeatSeconds()
        {
            return Settings != null ? Mathf.Clamp(Settings.HeartbeatSeconds, 0.1f, 5f) : 0.35f;
        }

        private void EnsureOverlayStyles()
        {
            if (_overlayLabelStyle == null)
            {
                _overlayLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Settings.OverlayFontSize,
                    fontStyle = FontStyle.Bold
                };
                _overlayLabelStyle.normal.textColor = Color.white;
            }

            if (_overlayBoxStyle == null)
            {
                _overlayBoxStyle = new GUIStyle(GUI.skin.box);
            }
        }

        private static bool IsCtrlRDown()
        {
            return Bindings.Down("MainGameVrVoiceGate.reload", "R + Ctrl");
        }

        private void LogInfo(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogInfo(message);
            WriteFileLog("INFO", message);
        }

        private void LogWarn(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogWarning(message);
            WriteFileLog("WARN", message);
        }

        private void LogError(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogError(message);
            WriteFileLog("ERROR", message);
        }

        private void LogSendError(string message)
        {
            DateTime now = DateTime.Now;
            if (now < _nextSendErrorLogAt)
                return;
            _nextSendErrorLogAt = now.AddSeconds(5);
            LogWarn(message);
        }

        private void LogInputError(string message)
        {
            DateTime now = DateTime.Now;
            if (now < _nextInputErrorLogAt)
                return;
            _nextInputErrorLogAt = now.AddSeconds(5);
            LogWarn(message);
        }

        private void WriteFileLog(string level, string message)
        {
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                lock (FileLogLock)
                {
                    File.AppendAllText(_logFilePath, line, Utf8NoBom);
                }
            }
            catch
            {
            }
        }
    }
}
