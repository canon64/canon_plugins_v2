using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CanonPlugins.Shared;
using BeatController = MainGameBeatSyncSpeed.Plugin;
using SpeedController = MainGameSpeedLimitBreak.Plugin;

namespace MainGameRhythmSync
{
    [BepInPlugin(Guid, "MainGameRhythmSync", "0.2.0")]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.rhythmsync";
        public static Plugin Instance { get; private set; }
        internal ConfigFile BeatConfig { get; private set; }
        internal ConfigFile SpeedConfig { get; private set; }
        internal ManualLogSource BeatLogger { get; private set; }
        internal ManualLogSource SpeedLogger { get; private set; }
        internal ConfigEntry<bool> LogOutputEnabled { get; private set; }
        internal bool LoggingEnabled => false;

        private Harmony _harmony;
        private ConfigEntry<float> _phaseCorrectionSec;
        private ConfigEntry<float> _phaseOffsetSec;
        private ConfigEntry<bool> _phaseLockEnabled;

        public bool IsPhaseSyncActive => BpmRequested && _groupReady;

        private void Awake()
        {
            Instance = this;
            string dir = Path.GetDirectoryName(Info.Location);
            Directory.CreateDirectory(dir);
            MigrateFile(Path.Combine(Paths.PluginPath, "canon_plugins", "MainGameBeatSyncSpeed", "SongBpmMap.json"), Path.Combine(dir, "SongBpmMap.json"));
            MigrateFile(Path.Combine(Paths.PluginPath, "canon_plugins", "MainGameSpeedLimitBreak", "SpeedLimitBreakSettings.json"), Path.Combine(dir, "SpeedLimitBreakSettings.json"));
            MigrateFile(Path.Combine(Paths.PluginPath, "canon_plugins", "MainGameSpeedLimitBreak", "SpeedTimeline.json"), Path.Combine(dir, "SpeedTimeline.json"));
            string oldSpeedDir = Path.Combine(Paths.PluginPath, "canon_plugins", "MainGameSpeedLimitBreak");
            if (Directory.Exists(oldSpeedDir))
            {
                foreach (string file in Directory.GetFiles(oldSpeedDir, "*.json", SearchOption.TopDirectoryOnly))
                    MigrateFile(file, Path.Combine(dir, Path.GetFileName(file)));
            }
            string beatCfg = Path.Combine(dir, "BeatSyncSettings.cfg");
            string speedCfg = Path.Combine(dir, "SpeedLimitSettings.cfg");
            MigrateFile(Path.Combine(Paths.ConfigPath, "com.kks.maingame.beatsyncseed.cfg"), beatCfg);
            MigrateFile(Path.Combine(Paths.ConfigPath, "com.kks.maingame.speedlimitbreak.cfg"), speedCfg);
            BeatConfig = new ConfigFile(beatCfg, true);
            SpeedConfig = new ConfigFile(speedCfg, true);
            BeatLogger = BepInEx.Logging.Logger.CreateLogSource("MainGameRhythmSync.Beat");
            SpeedLogger = BepInEx.Logging.Logger.CreateLogSource("MainGameRhythmSync.Speed");
            LogOutputEnabled = Config.Bind("Logging", "LogEnabled", false,
                "ログ出力の有効/無効。拍・速度・男女同期・ホイール診断のログをまとめて切り替えます。リズム同期の動作は変更しません。");
            _phaseLockEnabled = Config.Bind("Phase Lock", "Enabled", true, "BPM phase lock");
            _phaseCorrectionSec = Config.Bind("Phase Lock", "CorrectionSeconds", 0.8f,
                new ConfigDescription("Time used to correct phase drift smoothly", new AcceptableValueRange<float>(0.2f, 3f)));
            _phaseOffsetSec = Config.Bind("Phase Lock", "PhaseOffsetSeconds", 0f, "Manual phase offset in seconds");

            BeatController.Host = this;
            SpeedController.Host = this;
            gameObject.AddComponent<BeatController>();
            gameObject.AddComponent<SpeedController>();
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            if (LoggingEnabled)
                Logger.LogInfo("RhythmSync 0.2.0 loaded: shared speed + pair phase, weak=4 mid=2 strong=1");
        }

        private static void MigrateFile(string oldPath, string newPath)
        {
            if (File.Exists(oldPath) && !File.Exists(newPath))
                File.Copy(oldPath, newPath);
        }

        private void OnDisable() => ReleaseGroup();

        private void OnDestroy()
        {
            ReleaseGroup();
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
    }
}
