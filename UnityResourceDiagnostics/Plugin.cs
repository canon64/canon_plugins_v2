using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace UnityResourceDiagnostics
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kks.unityresourcediagnostics";
        public const string PluginName = "UnityResourceDiagnostics";
        public const string Version = "0.3.0";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object LogLock = new object();

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _verbose;
        private ConfigEntry<float> _intervalSeconds;
        private ConfigEntry<int> _topNames;
        private ConfigEntry<int> _meshSampleCount;
        private ConfigEntry<bool> _enableBakeMeshTrace;
        private ConfigEntry<int> _bakeMeshTraceLimit;
        private ConfigEntry<KeyboardShortcut> _snapshotKey;

        private string _logPath;
        private string _bakeMeshLogPath;
        private Harmony _harmony;
        private float _nextSnapshotTime;
        private Snapshot _lastSnapshot;

        private void Awake()
        {
            string pluginDir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            string logDir = Path.Combine(pluginDir, "_logs");
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, "resource_diagnostics.txt");
            _bakeMeshLogPath = Path.Combine(logDir, "bake_mesh_trace.txt");
            File.WriteAllText(_bakeMeshLogPath, string.Empty, Utf8NoBom);

            _enabled = Config.Bind("General", "Enabled", false, "Enable resource diagnostics snapshots.");
            _verbose = Config.Bind("General", "VerboseLog", false, "Write top object names for each resource type.");
            _intervalSeconds = Config.Bind(
                "General",
                "SnapshotIntervalSeconds",
                0f,
                new ConfigDescription("Automatic snapshot interval in seconds. Set 0 to disable automatic snapshots.", new AcceptableValueRange<float>(0f, 3600f)));
            _topNames = Config.Bind(
                "General",
                "TopNameCount",
                20,
                new ConfigDescription("Number of top object names written when VerboseLog is true.", new AcceptableValueRange<int>(0, 100)));
            _meshSampleCount = Config.Bind(
                "Mesh",
                "MeshSampleCount",
                20,
                new ConfigDescription("Number of new/orphan mesh samples written in each snapshot.", new AcceptableValueRange<int>(0, 200)));
            _enableBakeMeshTrace = Config.Bind("Mesh", "EnableBakeMeshTrace", false, "Trace SkinnedMeshRenderer.BakeMesh calls. Keep false unless investigating orphan meshes.");
            _bakeMeshTraceLimit = Config.Bind(
                "Mesh",
                "BakeMeshTraceLimit",
                10,
                new ConfigDescription("Maximum BakeMesh trace entries when BakeMesh tracing is enabled.", new AcceptableValueRange<int>(0, 1000)));
            _snapshotKey = Config.Bind("Input", "SnapshotKey", new KeyboardShortcut(KeyCode.F9, KeyCode.LeftControl), "Manual snapshot key.");
            BakeMeshTrace.Configure(_bakeMeshLogPath, () => _enableBakeMeshTrace.Value, () => Mathf.Max(0, _bakeMeshTraceLimit.Value));

            File.WriteAllText(
                _logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === {PluginName} {Version} start ==={Environment.NewLine}" +
                $"process={System.Diagnostics.Process.GetCurrentProcess().ProcessName}{Environment.NewLine}" +
                $"log={_logPath}{Environment.NewLine}",
                Utf8NoBom);

            Logger.LogInfo($"{PluginName} loaded. log={_logPath}");
            if (_enabled.Value)
            {
                ApplyPatches();
                WriteSnapshot("startup");
            }
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
                Logger.LogInfo($"{PluginName} Harmony patches removed.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"{PluginName} failed to remove Harmony patches: {ex}");
            }
        }

        private void Update()
        {
            if (!_enabled.Value)
                return;

            if (Bindings.Down("UnityResourceDiagnostics.snapshot", _snapshotKey.Value.ToString()))
            {
                WriteSnapshot("manual-key");
                ResetNextSnapshotTime();
                return;
            }

            float interval = Mathf.Max(0f, _intervalSeconds.Value);
            if (interval <= 0f)
                return;

            if (Time.unscaledTime >= _nextSnapshotTime)
            {
                WriteSnapshot("interval");
                ResetNextSnapshotTime();
            }
        }

        private void ResetNextSnapshotTime()
        {
            _nextSnapshotTime = Time.unscaledTime + Mathf.Max(0f, _intervalSeconds.Value);
        }

        private void ApplyPatches()
        {
            try
            {
                _harmony = new Harmony(GUID);
                BakeMeshTracePatcher.Apply(_harmony);
                Logger.LogInfo($"{PluginName} Harmony patches applied.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"{PluginName} failed to apply Harmony patches: {ex}");
            }
        }

        private void WriteSnapshot(string reason)
        {
            try
            {
                Snapshot snapshot = ResourceSnapshot.Capture();
                SnapshotDelta delta = SnapshotDelta.From(_lastSnapshot, snapshot);
                _lastSnapshot = snapshot;

                StringBuilder sb = SnapshotLogFormatter.Build(
                    reason,
                    snapshot,
                    delta,
                    _verbose.Value,
                    Mathf.Max(0, _topNames.Value),
                    Mathf.Max(0, _meshSampleCount.Value));

                lock (LogLock)
                    File.AppendAllText(_logPath, sb.ToString(), Utf8NoBom);
            }
            catch (Exception ex)
            {
                lock (LogLock)
                    File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ERROR snapshot failed: {ex}{Environment.NewLine}", Utf8NoBom);
            }
        }
    }
}
