using System;
using System.IO;
using BepInEx;
using HarmonyLib;

namespace MainGameAlbumAspect
{
    /// <summary>
    /// KKS のアルバム（撮影画像ビューア = CapFileViewer）が画像を引き伸ばして表示する問題を、
    /// 元のアスペクト比を保つように補正するプラグイン。
    /// </summary>
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.albumaspect";
        public const string PluginName = "MainGameAlbumAspect";
        public const string Version = "0.1.0";

        internal static Plugin Instance { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;

            // ログはプラグイン DLL と同じフォルダへ。Init() が truncate するので起動ごとに空になる。
            string pluginDir = null;
            try
            {
                pluginDir = Path.GetDirectoryName(Info.Location);
            }
            catch
            {
                pluginDir = null;
            }

            if (string.IsNullOrEmpty(pluginDir))
            {
                pluginDir = Paths.PluginPath;
            }

            try
            {
                Directory.CreateDirectory(pluginDir);
            }
            catch
            {
                // 作れなくてもログが出ないだけ。
            }

            SimpleFileLogger.Init(Path.Combine(pluginDir, PluginName + ".log"));

            SetupConfig();

            LogInfo(PluginName + " " + Version + " loaded");
            LogInfo("log path: " + (SimpleFileLogger.Path ?? "(none)"));

            try
            {
                _harmony = new Harmony(Guid);
                AlbumAspectPatches.Apply(_harmony);
            }
            catch (Exception e)
            {
                LogWarn("Harmony パッチの適用に失敗: " + e);
            }
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception e)
            {
                LogWarn("UnpatchSelf 失敗: " + e.Message);
            }

            _harmony = null;
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        internal void LogInfo(string message)
        {
            Logger.LogInfo(message);
            SimpleFileLogger.Info(message);
        }

        internal void LogWarn(string message)
        {
            Logger.LogWarning(message);
            SimpleFileLogger.Warn(message);
        }

        internal void LogDebug(string message)
        {
            if (CfgVerboseLog == null || !CfgVerboseLog.Value)
            {
                return;
            }

            SimpleFileLogger.Debug(message);
        }

        /// <summary>Instance が未生成でも安全に呼べる警告ログ。</summary>
        internal static void SafeWarn(string message)
        {
            var instance = Instance;
            if (instance != null)
            {
                instance.LogWarn(message);
            }
            else
            {
                SimpleFileLogger.Warn(message);
            }
        }

        /// <summary>Instance が未生成でも安全に呼べる詳細ログ。</summary>
        internal static void SafeDebug(string message)
        {
            Instance?.LogDebug(message);
        }
    }
}
