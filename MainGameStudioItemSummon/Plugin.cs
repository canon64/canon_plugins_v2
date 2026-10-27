using System;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace MainGameStudioItemSummon
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        internal const string GUID = "com.kks.maingamestudioitemsummon";
        internal const string PluginName = "MainGameStudioItemSummon";
        internal const string Version = "0.1.0";

        internal static Plugin Instance { get; private set; }

        private PluginSettings _settings;
        private string _pluginDir;
        private string _logPath;
        private readonly object _logLock = new object();

        private bool _hudVisible = true;

        private void Awake()
        {
            Instance = this;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;

            string logDir = Path.Combine(_pluginDir, "_logs");
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, "info.txt");
            File.WriteAllText(_logPath, string.Empty, Encoding.UTF8);

            _settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
            SetupConfig();

            LogInfo($"=== {PluginName} {Version} start === [DIAG: HUD/OnGUI removed]");
            LogInfo("idle. nothing runs automatically. operate from ConfigManager.");
        }

        private void LogCore(string level, string msg)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            string line = $"[{stamp}] [{level}] {msg}";

            if (level == "ERROR")
                Logger.LogError(msg);
            else if (level == "WARN")
                Logger.LogWarning(msg);
            else
                Logger.LogInfo(msg);

            lock (_logLock)
            {
                try { File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8); }
                catch { }
            }
        }

        private void LogInfo(string msg) => LogCore("INFO", msg);
        private void LogWarn(string msg) => LogCore("WARN", msg);
        private void LogError(string msg) => LogCore("ERROR", msg);
    }
}
