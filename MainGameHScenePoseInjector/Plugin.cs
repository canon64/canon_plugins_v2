using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameHScenePoseInjector
{
    // LateUpdate を必ず最後に回す。
    // Hシーンのキャラは FinalIK(FullBodyBipedIK) が LateUpdate で解いており、
    // 実行順を指定しないとポーズ遷移の骨補間がIKに上書きされて、
    // 補間が一切見えないまま新ポーズへ飛ぶ(ワープ)。
    [DefaultExecutionOrder(20000)]
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInProcess("KoikatsuSunshine")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string GUID = "canon.maingame.hscene.poseinjector";
        public const string NAME = "MainGameHScenePoseInjector";
        public const string VERSION = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static string PluginDir;
        internal static StreamWriter LogFile;
        internal static bool RuntimeLoggingEnabled = false;

        private PluginSettings _settings;
        private string _settingsPath;
        private string _logPath;
        private int? _lockedEyebrowPtn;
        private int? _lockedEyesPtn;
        private int? _lockedMouthPtn;
        private float _lastExpressionRestoreLogTime = -10f;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);
            _settingsPath = Path.Combine(PluginDir, "MainGameHScenePoseInjectorSettings.json");
            _logPath = Path.Combine(PluginDir, "MainGameHScenePoseInjector.log");

            LogFile = null;

            _settings = SettingsStore.LoadOrCreate(_settingsPath, LogInfo, LogWarn);
            RegisterUnifiedTabIfAvailable();
            LogInfo("plugin awake version=" + VERSION + " dir=" + PluginDir);
        }

        private void OnDestroy()
        {
            try
            {
                RestoreAllSnapshots("plugin-destroy");
                LogFile?.Flush();
                LogFile?.Close();
            }
            catch { }
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            try
            {
                RegisterUnifiedTabIfAvailable();
                UpdateHSceneDetect();
                if (!BlankRoomGate.IsInBlankRoom && !IsInHScene()) return;
                AutoRestoreIfControllerChanged();
                UpdateAutoCycle();
                TickEffectCycle();
            }
            catch (Exception ex)
            {
                LogWarn("update exception: " + ex.Message);
            }
        }

        private void LateUpdate()
        {
            if (_settings == null || (!BlankRoomGate.IsInBlankRoom && !IsInHScene())) return;
            try
            {
                // 表情維持は設定に従うが、首のカメラ追従はゲーム側の
                // チェックボックスが持ち主なので、この設定とは切り離す。
                if (_settings.KeepSelectedExpressionWhilePoseApplied)
                    MaintainSelectedExpression();

                MaintainNeckLookCamera();
                // 骨補間はアニメーション適用の後に上書きする必要があるため LateUpdate で行う。
                UpdatePoseBlend();
                // 補間を書いた後に、今表示されている姿勢を控える。
                // ここが実行順の最後尾(IKの後)なので、これが実際に見えている姿勢。
                CaptureLiveSnapshot();
                // 頬赤・涙はゲーム側が毎フレーム書き換えてくるので押し切る。
                MaintainValueChannels();
                MaintainEffectChannels();
            }
            catch (Exception ex)
            {
                LogWarn("expression maintain exception: " + ex.Message);
            }
        }

        private void OnGUI()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            // ポーズUIは総合UIのPoseタブからのみ描画する。
            // 従来のF4独立ウィンドウは廃止。
        }

        internal void SaveSettings()
        {
            SettingsStore.Save(_settingsPath, _settings, LogWarn);
        }

        internal static void LogInfo(string msg)
        {
            if (!RuntimeLoggingEnabled) return;
            try
            {
                Log?.LogInfo(msg);
                LogFile?.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [INFO] " + msg);
            }
            catch { }
        }

        internal static void LogWarn(string msg)
        {
            if (!RuntimeLoggingEnabled) return;
            try
            {
                Log?.LogWarning(msg);
                LogFile?.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [WARN] " + msg);
            }
            catch { }
        }
    }
}
